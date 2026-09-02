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
- [ ] One card crossing the wire (use `tools/peer.py`)
- [ ] Two real game clients

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
