# Inscryption Online PvP — working notes

**How to read this.** It is a working log kept while building the mod, in the order things
were learned. The sections near the top are kept current; everything below "First working
round trip" is history, including a few claims that later entries correct. Where that
happens the correction says so.

If you are here to mod Inscryption yourself, **Key engine findings** is the part worth your
time.

## Target
Networked online PvP **inside the real game client**. Everything that exists today is
either local hotseat, Tabletop Simulator, or a browser/Godot reimplementation.

## Environment (verified)
- Install: `C:\Steam\steamapps\common\Inscryption`
- **Mono** Unity, **32-bit** (`UnityCrashHandler32.exe`, `MonoBleedingEdge/`)
- `Inscryption_Data/Managed/Assembly-CSharp.dll` — full readable C#, no obfuscation
- Uses Odin (Sirenix) serialization; `CardInfo : SerializedScriptableObject`
- Toolchain: dotnet SDK 8.0.422, git 2.53, `ilspycmd` 8.2.0.7535 (newer versions have a
  broken `DotnetToolSettings.xml` — pin this one)
- BepInEx packages are **not on nuget.org** — need the feed at
  `https://nuget.bepinex.dev/v3/index.json` (see `NuGet.config`)

## Key engine findings
`DiskCardGame` namespace. The battle state machine is clean and hookable:

- `TurnManager` (Singleton) — `GameSequence` → `PlayerTurn` / `OpponentTurn` coroutines.
  `OnCombatBellRang()` is what the bell calls to end the player's turn.
- `Opponent` — **abstract**, `QueueNewCards()` is `virtual`. This is the AI's decision point
  and therefore our injection point.
- `Opponent.SpawnOpponent(EncounterData)` — static factory, Harmony-postfixable.
- `BoardManager` (Singleton) — `PlayerSlotsCopy` / `OpponentSlotsCopy`,
  `CreateCardInSlot(CardInfo, CardSlot)`, `ResolveCardOnBoard(PlayableCard, CardSlot)`.
- `CardSlot.Index` and `CardSlot.IsPlayerSlot` give us stable slot addressing.
- `CardLoader.GetCardByName(string)` — card name is a sufficient wire identifier.

## Key engine findings, part two (acts 2 and 3, UI)
Learned bringing up the other acts. All verified against Assembly-CSharp, not guessed.

- **`GBC_CardBattle` has no `GameFlowManager`.** Act 1 and Act 3 are explorable scenes with
  one driving their game states; Act 2's battle scene is self-contained and its flow lives
  in the overworld it is normally entered from. Anything waiting on that singleton waits
  forever there — including `TurnManager.CleanupPhase`, which only calls
  `TransitionToNextGameState` when it exists, so a match never *ends* either.
- **Each act draws from its own deck.** They share an abstract `CardDrawPiles.DeckData`,
  but Act 1 and Act 3 go through `CardDrawPiles3D` while `GBC.PixelCardDrawPiles` reads
  `GBC.SaveData.Data.deck` — which `Initialize()` leaves empty. `Deck.GetFairHand` then
  indexes an empty list with no bounds check.
- **`AbilitiesUtil.GetInfo` is a list search**, not a switch:
  `ScriptableObjectLoader<AbilityInfo>.AllData.Find(x => x.ability == ability)`. An
  `AbilityInfo` injected into that list resolves everywhere, so custom sigils need no
  transpiling.
- **`CombatPhaseManager.SlotAttackSequence` never checks whose card it is.** It prompts the
  local player to aim any card with `Ability.Sniper`. Safe in single player, where only the
  player holds one; over a network it asks you to aim your opponent's card, at the wrong
  side of the board. `GetOpposingSlots` gets the same distinction right, so it is that
  branch specifically.
- **`GBC.PixelCardAbilityIcons` keeps one icon layout per sigil count** and picks it by
  index: `abilities.Count - 1 < abilityIconGroups.Count`. A card with more sigils than
  there are layouts matches nothing and draws **no** sigils rather than a truncated set.
- **`CardInfo.Clone()` gives the copy a fresh `Mods` list.** `CardLoader.GetCardByName`
  hands out one shared instance per card, so anything that adds abilities must clone first
  or it changes that card everywhere, campaign included.
- **`MenuCard` / `MenuSlot`**: the title screen is literally cards dropped into a slot,
  dispatched through a `MenuAction` switch in `OnCardReachedSlot`. `DisplayMenuCardTitle`
  falls back to rendering `TitleText` when a card has no `titleSprite`, so a new menu card
  needs no word art. Each card has its own face sprite (`menucard_options` and friends),
  and skipping `OnCardReachedSlot` outright leaves `DoingCardTransition` set, which freezes
  the whole menu.

### Two that cost real time
- **Patching an overloaded method by name throws `AmbiguousMatchException` — during
  `PatchAll`.** That aborts the entire pass, so *every* patch in the mod silently fails to
  apply and the plugin looks dead with no crash. Always pass the parameter types.
- **Unity ignores `GUIStyle.fontSize` for a non-dynamic font, but still measures layout
  with it.** Inscryption's `Marksman (Crisp)` is a bitmap font baked at 16. Styles asking
  for 12 reserved a 12pt box that then had 16pt glyphs drawn into it, and the overflow was
  clipped — text that would not fit its box no matter how wide the box got. Set `fontSize`
  to 0 so measurement and drawing agree, and scale the whole UI through `GUI.matrix` if you
  need it bigger.

## Architecture
Each client runs an **ordinary single-player battle**. You are always "the player" on your
own screen; your peer is always "the opponent". A card the peer plays into THEIR player
slot N materialises in OUR opponent slot N. Combat resolution is untouched vanilla.

Turn alternation falls out of the engine for free: your `PlayerTurn` is when you act,
your `OpponentTurn` is when the network feeds you.

Because peer cards land in **opponent** slots and we only capture plays into **player**
slots, there is no echo problem — the gate is structural, not a flag.

Transport is plain TCP, newline-delimited text. It's turn-based: ordering matters,
latency does not. The protocol is human-readable so the log is the debugger.

## Wire protocol
Protocol 3. Newline-delimited text, human-readable so the log is the debugger.

```
HELLO <proto> <modVersion>    greeting; both sides send it on connect
START <act>                   begin a match on act 1, 2 or 3
PLAY <cardName> <slotIndex>   peer played a card into their player slot N
SAC <slotIndex>               peer sacrificed the card in that slot
BOARD <slot>|<slot>|...       authoritative snapshot of the sender's player slots
AIM <slot> <t1,t2,...>        where the sender aimed a Sniper card
END                           peer ended their turn
OVER WON | OVER LOST          the sender is reporting their own result
```

A `BOARD` slot is `-` for empty, otherwise `Name:attack/health` with `:sigil,sigil`
appended when the card has gained any. Names alone were not enough: both clients simulate
combat independently and usually agree, but nothing corrected them when they didn't, so a
buffed or damaged card kept different numbers on each screen for the rest of the match.

**Older peers are not shut out.** The handshake carries the protocol version, so a client
that speaks 3 writes protocol 2 messages to a peer that speaks 2 and goes without the
extras. Only protocol 1 is refused, and for a real reason: it predates `START <act>`, so
one side would load Act 1's cabin while the other loaded Act 3's board.

## Status
- [x] Environment recon, decompile, battle state machine mapped
- [x] `Net` TCP transport + Steam lobbies and P2P
- [x] `NetworkOpponent : Opponent` — network-driven `QueueNewCards`
- [x] Standalone versus mode, launched from the main menu, campaign save untouched
- [x] Two real game clients, two machines, two Steam accounts
- [x] Full match played to a win, clean return to the main menu
- [x] Deck builder on the game's own card table, one deck per act
- [x] Version handshake, with older peers negotiated down rather than refused
- [x] **All three acts** — Act 1's cabin, Act 2's GBC board, Act 3's P03 table
- [x] Card stats and gained sigils synced, not just which card is where
- [x] Sniper aimed by the client that owns the card
- [x] Player-chosen sigils on deck cards
- [x] A multiplayer card on the title screen

## Known gaps / next
- **Acts 2 and 3 have only been played against the scripted peer**, not two real clients.
  Act 1 is the one verified across two machines.
- **A modified client is trusted.** Each client is authoritative over its own board, which
  is inherent to a peer-to-peer design without a referee. Play with people you trust.
- Sigils a card gains beyond what the act can lay out are clamped for display; Act 2 keeps
  one icon layout per sigil count and draws none at all past the end of that list.
- No spectating, no reconnect beyond the two-minute window, no matchmaking beyond Steam's
  public lobby list.

## Testing without a second copy of the game
Steam won't happily run two instances, so `tools/peer.py` speaks our protocol directly:

1. Launch the game (BepInEx is installed; it loads automatically).
2. **Press F9 to host BEFORE entering a battle.** `OpponentInjector` only swaps in
   `NetworkOpponent` if `Net.Connected` is true at `SpawnOpponent` time.
3. Walk into any Act 1 combat node.
4. `python tools/peer.py Wolf:1 Adder:2`
5. Watch `BepInEx/LogOutput.log` for `[opp] placing peer card`.

Verified-valid card names: Wolf, Adder, Bullfrog, Squirrel, Stoat, Grizzly.

## Gotcha found the hard way
BepInEx's log confirms patch resolution but NOT patch correctness — "Harmony patches
applied" only means the target methods exist with the expected signatures.

## Gotcha: no in-game feedback
First F9 test "did nothing" from the player's side — but the log showed hosting had
started fine, twice. There was simply no on-screen indication, so the key got pressed
again, and the second `Host()` called `Shutdown()` on the live listener.

Two fixes:
- `Hotkeys.OnGUI` draws a persistent status overlay (top-left). Green when connected.
- `Net.Host()`/`Net.Join()` are now idempotent — a second press is a no-op, not a teardown.

Lesson: for a mod with no UI, build the status readout before the first live test.
Verified working via `netstat`: the game process holds `0.0.0.0:27333 LISTENING`.

## Gotchas: launching and logging
1. **BepInEx's disk log buffers.** You cannot watch a live session through
   `LogOutput.log` from outside the game. `Trace.cs` mirrors everything to
   `BepInEx/mp-trace.log` with an explicit flush per line. Tag lines with the PID.
2. **Launching `Inscryption.exe` directly makes Steam spawn a second process.** The two
   race for port 27333 and the loser logs `Address already in use` — then Steam kills
   the winner, leaving a live game that is not hosting. Launch with
   `cmd /c start "" "steam://rungameid/1092790"` instead.
3. A failed `Host()`/`Join()` left `_running = true`, so the idempotency guard blocked
   any retry — the mod was wedged until restart. Failures now reset `_running`.
   Listener also sets `SO_REUSEADDR` for prompt rebinding.

## Verified working end to end
```
[boot] AutoHost enabled - hosting immediately.
[net] hosting on port 27333, waiting for peer...
[net] peer connected.
```
Transport, host lifecycle, and status overlay all confirmed against the real game.

## First working round trip (2026-09-02)
```
[net]  -> PLAY Bee 0                          local play captured and sent
[probe] bell rang - local player ended turn 1
[net]  -> END
[opp]  waiting for peer's turn...
[net]  <- PLAY Wolf 1
[net]  <- PLAY Adder 2
[opp]  placing peer card 'Wolf' in opponent slot 1
[opp]  placing peer card 'Adder' in opponent slot 2
[opp]  peer ended turn.
```
Both directions confirmed against the live game. The engine's own combat resolution
then runs untouched.

### The real blocker, for the record
The setup hang was never the opponent injection - that worked on the first try. It was
`DeckInfo.LoadCards()` raising a NullReferenceException on the player's *campaign* deck.
Serving a fixed versus deck fixed it and is the correct design anyway.

## Next: standalone versus mode
Hijacking an arbitrary campaign fight couples a match to the player's run - a mid-match
bug strands them on a map node with input locked. `TurnManager.StartGame(EncounterData)`
is public and `EncounterData` is a trivial plain class, so a match should build its own
encounter instead:

- menu entry -> construct `EncounterData` (empty turn plan, our opponent type)
- `DeckOverride` already supplies both decks
- no run consumed, no save touched, repeatable test loop with no map walking

## Standalone versus mode
`F8` from anywhere. If `TurnManager` is missing we load `Part1_Cabin`, wait for the board
singletons, then start. The match is built as a plain `EncounterData` handed to the public
`TurnManager.StartGame(EncounterData)` overload - no `CardBattleNodeData`, no
`EncounterBuilder`, no blueprint, no map node consumed.

Key scene facts:
- Act 1 gameplay scene is **`Part1_Cabin`**, loaded via `LoadingScreenManager.LoadScene`.
- A menu launch lands in **Map** state (not first person, as first assumed).
- `SaveManager.savingDisabled` is set for the whole match; `SaveToFile()` honours it, so a
  match cannot write to the campaign save whatever goes wrong.

### Pinning the player at the table
`GameFlowManager.UpdateForTransitionToFirstPerson()` polls input every frame, so no view
state prevents standing up - and standing up mid-match re-reveals the map and desyncs
`GameFlowManager`. `MatchLock` prefixes `TransitionToFirstPerson` and
`TransitionToGameState` to swallow them while a match is live.

## Full round trip in a standalone match (2026-09-02)
```
-> PLAY Squirrel 1 / END
<- PLAY Wolf 1 / PLAY Adder 2 / END
[opp] placing peer card 'Wolf' in opponent slot 1
```

## Known unfairness
**This entry is wrong and is kept for the record — see "Correction: peer cards are not
'free'" below.** It read: peer cards are placed with `BoardManager.CreateCardInSlot`, which
bypasses cost entirely, so the remote player pays no blood or bones.

## Full match verified (2026-09-02)
```
bell rang - turn 1  ->  peer plays Stoat
bell rang - turn 2  ->  peer plays Bullfrog
bell rang - turn 3
[versus] match over - you won (battle resolved)
[versus] returning to main menu
```

## Bug class worth remembering: "the game assumed this would be instant"
A vanilla opponent turn takes a second or two, so the engine locks the view for its
duration. Ours can block indefinitely on the network, which turned that assumption into
a frozen camera. Expect more of these wherever the engine brackets a short operation.

## Bug: CRLF mismatch (cost ~20 minutes)
C# `StreamWriter` defaults to CRLF, so lines arrived at the peer as `"END
"`. The peer
stripped only `
`, so every equality check failed and auto-play never fired - the player
waited forever on a turn that never came. **Both logs printed identically**, because a
carriage return is invisible.

`Net` now writes LF explicitly and trims on receive; the peer strips whitespace. When a
text protocol "obviously matches" but comparisons fail, check the invisible bytes first.

## Verified working (2026-09-02, two machines over Steam)
Full matches played end to end: lobby, join, start sync, alternating turns, board state
holding across sacrifices, and a clean finish on both clients.

### Correction: peer cards are not "free"
An earlier note claimed remote plays bypassed cost. They don't. Each player's own client
enforces blood and sacrifices normally, and the board snapshot reflects what remains
afterwards. Materialising the resulting card on the receiving side without charging that
player is correct - the cost was already paid where it was owed.

### Match results must bypass the inbox
Only the loser's client detects a loss locally. It reports the outcome to the peer, but
that message was originally read only inside NetworkOpponent's wait loop - which runs
only while a client waits on the opponent's turn. The winner is normally in their *own*
turn when it arrives, so the message sat unread and the winner was stranded at a
finished board.

Results now go through `Net.PendingResult`, written by both transports at the point of
receipt and applied from the menu's update every frame. Anything that must be handled
regardless of game phase needs the same treatment.
