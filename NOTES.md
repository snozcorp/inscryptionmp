# Inscryption Online PvP — working notes

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
```
PLAY <cardName> <slotIndex>   peer played a card into their player slot N
END                            peer ended their turn
```

## Status
- [x] Environment recon
- [x] Decompile + map battle state machine
- [x] Plugin scaffold builds clean against game assemblies
- [x] `Net` TCP transport (background thread, ConcurrentQueue drain)
- [x] `NetworkOpponent : Opponent` — network-driven `QueueNewCards`
- [x] `Sync` — capture local plays + turn end
- [x] `OpponentInjector` — swap in NetworkOpponent when a session is live
- [x] BepInEx 5.4.23.2 (x86) installed into game dir
- [x] **Plugin loads in the real game; all Harmony patches resolve** (Unity 2019.4.24f1)
- [x] **One card crossing the wire, both directions** (2026-09-02)
- [x] **Standalone versus mode, launchable from the main menu** (F8)
- [x] Save protected + player pinned to the table for the duration
- [ ] Enforce blood/bone cost on peer cards (they are currently free)
- [ ] Two real game clients instead of a scripted peer
- [x] **Full match played to a win, clean return to the main menu**
- [ ] Proper in-game menu (host / join / IP entry) instead of F-keys
- [ ] Dedicated PvP deck / deck builder

## Known gaps / next
- No handshake or version check yet (`Protocol.Hello` defined, unused).
- Sacrifices/blood cost are resolved locally only — peer sees the resulting card, not the
  cost payment. Fine for v1.
- Deck sync: both sides currently use whatever encounter they launched. Needs a real
  "agree on decks" step before this is a fair PvP game.
- Act 2 (`GBC.Pixel*` classes) is a parallel hierarchy — same approach, different types.

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
BepInEx's log confirms patch resolution but NOT patch correctness � "Harmony patches
applied" only means the target methods exist with the expected signatures.

## Gotcha: no in-game feedback
First F9 test "did nothing" from the player's side � but the log showed hosting had
started fine, twice. There was simply no on-screen indication, so the key got pressed
again, and the second `Host()` called `Shutdown()` on the live listener.

Two fixes:
- `Hotkeys.OnGUI` draws a persistent status overlay (top-left). Green when connected.
- `Net.Host()`/`Net.Join()` are now idempotent � a second press is a no-op, not a teardown.

Lesson: for a mod with no UI, build the status readout before the first live test.
Verified working via `netstat`: the game process holds `0.0.0.0:27333 LISTENING`.

## Gotchas: launching and logging
1. **BepInEx's disk log buffers.** You cannot watch a live session through
   `LogOutput.log` from outside the game. `Trace.cs` mirrors everything to
   `BepInEx/mp-trace.log` with an explicit flush per line. Tag lines with the PID.
2. **Launching `Inscryption.exe` directly makes Steam spawn a second process.** The two
   race for port 27333 and the loser logs `Address already in use` � then Steam kills
   the winner, leaving a live game that is not hosting. Launch with
   `cmd /c start "" "steam://rungameid/1092790"` instead.
3. A failed `Host()`/`Join()` left `_running = true`, so the idempotency guard blocked
   any retry � the mod was wedged until restart. Failures now reset `_running`.
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
Peer cards are placed with `BoardManager.CreateCardInSlot`, which bypasses cost entirely -
the remote player pays no blood or bones. Fixing this properly means syncing sacrifices,
not just the resulting card.

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
C# `StreamWriter` defaults to CRLF, so lines arrived at the peer as `"END"`. The peer
stripped only `
`, so every equality check failed and auto-play never fired - the player
waited forever on a turn that never came. **Both logs printed identically**, because a
carriage return is invisible.

`Net` now writes LF explicitly and trims on receive; the peer strips whitespace. When a
text protocol "obviously matches" but comparisons fail, check the invisible bytes first.
