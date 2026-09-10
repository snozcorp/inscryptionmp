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
- `CardLoader.GetCardByName(string)` — card name is a sufficient wire identifier. It
  throws rather than returning null on a name it doesn't know; see the correction below.

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

- **`Latch.OnPreDeathAnimation` picks its target two different ways.** `if
  (base.Card.OpponentCard)` runs `AISelectTarget`, otherwise the player chooses. Exactly
  the shape of the Sniper problem, and worse in effect: `AIEvaluateTarget` adds 1000 to a
  card whose side matches the sigil's sign, so for a negative sigil it reaches for the
  opposite half of the board on purpose. Two clients therefore latch onto different cards.
  The mod applies its sigil through `AddTemporaryMod` with `fromLatch` set, which is a
  usable marker for catching every latcher without naming them.
- **A fresh Part 1 run is not empty.** `RunState.InitializeStarterDeckAndItems` seeds
  `consumables` with a Squirrel Bottle, then Pliers or the Special Dagger, then a Fish
  Hook. `SpecialDaggerItem` writes `StoryEventsData.SetEventCompleted(SpecialDaggerUsed)`,
  and that lands in `SaveManager.SaveFile.storyEvents` - the profile, not the run - so a
  synthetic run does not contain it. It also sets `TurnManager.PostBattleSpecialNode`.
- **`PauseMenu` owns a `MenuController` of its own.** Patching `MenuController` catches
  the title screen and the in-game pause menu both; `PauseMenu.instance.menuController`
  tells them apart, and `GBCPauseMenu` inherits it. `TweenInCards` also removes the
  ascension card when Kaycee's Mod is locked, so a prefix that lays out the row sees a
  card that is about to disappear.

### Three more, found bringing up match rules and the self-test
- **`CardLoader.GetCardByName` throws on a name that isn't a card.** It is
  `Clone(AllData.Find(x => x.name == name))` with nothing in between, and `Clone` dereferences
  what `Find` returned - so an unknown name is a `NullReferenceException`, not a null. Every
  caller in this mod had been written to check for the null that never comes. It matters
  because two of the places a card name arrives from are outside the mod's control: a
  hand-edited deck file, and the peer's own `PLAY` line. `Cards.ByName` is the guarded lookup
  everything goes through now.
- **`Application.runInBackground` is false.** Inscryption is single-player, so Unity's
  default stands and the player stops updating when its window isn't in front. For a
  networked match that is a freeze: the reader thread keeps filling the inbox, because
  background threads are unaffected, but nothing on the main thread ever drains it. The
  symptom is confusing precisely because half the client stays alive - the log keeps
  printing `<- VOTE`, and nothing is ever sent back.
- **The side pile is campaign progression in disguise.** `Part1CardDrawPiles.SideDeckData`
  reads `StoryEventsData.EventCompleted(StoryEvent.BeeFigurineFound)` and hands out ten
  Bees instead of ten Squirrels; `Part3CardDrawPiles` does the same with
  `GemsModuleFetched` and `Part3SaveData.deckGemsDistribution`. In single player that is a
  reward. In a versus match it is two people playing with different piles for reasons
  neither of them can see.

### The numbers a match is made of are constants, not settings
Everything a house rule would want to change is compiled in rather than stored:

- **The win post is `LifeManager.GOAL_BALANCE = 5`**, and it is inlined into
  `LifeManager.DamageUntilPlayerWin` and three *private* TurnManager checks -
  `GameIsOver`, `ScalesTippedToOpponent`, `LifeLossConditionsMet`. Moving it means
  answering all four, which is two lines of arithmetic each; `PlayerIsWinner` needs no
  patch because it reads the opponent's lives, which the patched `ScalesTippedToOpponent`
  already decides. Act 1's scales are a physics simulation, so a higher post just leans
  the balance further rather than saturating.
- **The opening hand is the literal `4`, twice.** `CardDrawPiles3D.DrawOpeningHand` and
  `GBC.PixelCardDrawPiles.DrawOpeningHand` each write it into their own body, and the GBC
  one also loops `for (int i = 0; i < 4; i++)` - so changing what `GetFairHand` returns
  moves which cards you get and not how many.
- **`NumDrawPhaseDraws` is overridden.** Patching `CardDrawPiles`'s getter alone leaves
  Acts 1 and 3 on the base number, because `CardDrawPiles3D` has its own.
- `ResourcesManager` is per-act, so anything handed out at the start hangs off
  `TurnManager.SetupPhase` instead, which runs for all three.

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
Protocol 5. Newline-delimited text, human-readable so the log is the debugger.

```
HELLO <proto> <modVersion>    greeting; both sides send it on connect
VOTE <act>                    which act the sender wants to play
READY 0 | READY 1             whether the sender has committed to starting it
RULES <k=v;k=v>               the host's house rules for the match
NAME <text>                   what the sender would like to be called
CHAT <text>                   something the sender typed
EMOTE <n>                     one of the fixed phrases, by position
START <act>                   begin a match on act 1, 2 or 3
PLAY <cardName> <slotIndex>   peer played a card into their player slot N
SAC <slotIndex>               peer sacrificed the card in that slot
BOARD <slot>|<slot>|...       authoritative snapshot of the sender's player slots
AIM <slot> <t1,t2,...>        where the sender aimed a Sniper card
LATCH P|O <slotIndex>         which card a latcher latched onto
END                           peer ended their turn
OVER WON | OVER LOST          how the match ended, FOR THE RECEIVER
```

`OVER` states the *receiver's* result, not the sender's: the side that knows the match is
over is reporting it to the side that doesn't. Every sender in the mod always meant that -
conceding by walking out sends `OVER WON` - but until 1.5 the reading end took it as the
sender's own result and printed the opposite, so quitting told your opponent they had lost.
The strings are unchanged, which is why a 1.4 peer's report is still read correctly.

`EMOTE` carries a position rather than the words, so each build shows the phrase in its own
wording rather than the sender's.

`RULES` is a key=value list rather than a fixed set of fields, so the next rule is a key an
older build skips instead of protocol 6. Nine keys so far; every numeric one is clamped on
the way in, because a peer that sends `win=0` would otherwise produce a match that is over
before it starts. Only the host sends it, because both clients run
their own battle against their own deck and a rule that changes how a deck behaves is only
fair if one side decides it for both. A peer that speaks 4 or less cannot be told, so the
match falls back to the defaults on both sides rather than one of them quietly playing a
different game.

A `BOARD` slot is `-` for empty, otherwise `Name:attack/health` with `:sigil,sigil`
appended when the card has gained any. Names alone were not enough: both clients simulate
combat independently and usually agree, but nothing corrected them when they didn't, so a
buffed or damaged card kept different numbers on each screen for the rest of the match.

`VOTE` and `READY` are the lobby negotiation, and they are the only messages that matter
while no battle is running. Both are captured out of band, like results: they arrive
between matches, and the opponent turn loop would only discard them. Both sides announce
their vote and their (un)readiness once per connection and again whenever either changes;
the host is the one that sends `START` when the two agree and both are ready, because if
both sent it the two messages would cross and each client would try to start a match it
was already starting.

**Older peers are not shut out.** The handshake carries the protocol version, so a client
that speaks 4 writes protocol 2 messages to a peer that speaks 2 and goes without the
extras. A peer that cannot vote falls back to the pre-4 rule — either player starts and
both are pulled in — but *only* when their own greeting says so. An earlier version gave up
on a silent peer after eight seconds, which meant one dropped packet quietly turned the
start button back into one that drags them into a match they never agreed to; waiting
forever is the safe failure, and the two-second re-announce is what makes waiting rare.
Only protocol 1 is refused, and for a real reason: it predates `START <act>`, so one side
would load Act 1's cabin while the other loaded Act 3's board.

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
- [x] Both players vote on the act and both commit before a match loads
- [x] Match rules on the wire: nine of them, host-set, both clients bound by them
- [x] The five hardcoded numbers that make a match: win post, hand, draws, side pile, first
      turn - all of them constants compiled into the game rather than fields to set
- [x] Deck order the player sets, and the deck dealt in it
- [x] Two real clients playing a whole match with nobody at either keyboard

## Known gaps / next
- Acts 2 and 3 have now been played between two real clients, but with far fewer hours on
  them than Act 1.
- **A modified client is trusted.** Each client is authoritative over its own board, which
  is inherent to a peer-to-peer design without a referee. Play with people you trust.
- Sigils a card gains beyond what the act can lay out are clamped for display; Act 2 keeps
  one icon layout per sigil count and draws none at all past the end of that list.
- No spectating, no reconnect beyond the two-minute window, no matchmaking beyond Steam's
  public lobby list.

## Testing with two clients and nobody watching
`src/Autopilot.cs` is a second driver for the mod: it hosts or joins, votes, presses start,
takes its turns and quits, and writes what it did to the trace. It is inert unless
`INSCRYPTIONMP_AUTOPILOT` is set in the environment, so it ships in the release and does
nothing there.

```bash
tools/test-all.sh          # wire-format tests, then a match on each act
tools/test-all.sh --quick  # just the build and the wire-format tests
tools/match-test.sh --act 2 --draw order
```

`match-test.sh` launches both installs listed at the top of it, waits for both trace logs to
say `[auto] ==== RUN`, and fails on any `[auto] FAIL`. Two things it taught, both of which
are now fixed rather than worked around:

- Only the focused window runs, so the first attempt looked like a transport bug: the host
  was reading the joiner's messages and answering none of them. `Application.runInBackground`.
- Both game windows open at the same position, so screenshotting one by its window rectangle
  photographs whichever is in front. Foreground it first.

`INSCRYPTIONMP_AUTOPILOT_HOLD` stops before the match and parks the client on the lobby
panel or the deck table instead, which is how the interface gets looked at rather than
described - there is no reading a layout out of a log.

## Testing without a second copy of the game
Steam won't happily run two instances, so `tools/peer.py` speaks our protocol directly:

1. Launch the game (BepInEx is installed; it loads automatically).
2. **F7 → Direct connect → Host**, before anything else. `OpponentInjector` only swaps in
   `NetworkOpponent` if `Net.Connected` is true at `SpawnOpponent` time.
3. `python tools/peer.py` — it greets as a protocol 5 client, mirrors whichever act you
   vote for, and presses start once you have, so the whole negotiation runs from one
   machine. `--act 2` pins it to Act 2 to see a 1/2 disagreement, `--wait` makes it never
   press start, `--proto 4` makes it a build that cannot be told the match rules, and
   `--legacy` makes it greet as protocol 3 to exercise the older fallback. It
   can be started before the game and left running across restarts; it waits for the
   listener and goes back to waiting when the game closes.
4. Press START MATCH. The panel should hold at 1/2 for half a second, then load.
5. Watch `BepInEx/mp-trace.log` for `[lobby]` lines and `[opp] placing peer card`.

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
