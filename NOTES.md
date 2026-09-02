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
- [ ] **BepInEx runtime installed into game dir** (blocked: awaiting go-ahead)
- [ ] Verify plugin loads (F9 host / F10 join localhost / F11 status)
- [ ] Two clients, one card crossing the wire

## Known gaps / next
- No handshake or version check yet (`Protocol.Hello` defined, unused).
- Sacrifices/blood cost are resolved locally only — peer sees the resulting card, not the
  cost payment. Fine for v1.
- Deck sync: both sides currently use whatever encounter they launched. Needs a real
  "agree on decks" step before this is a fair PvP game.
- Act 2 (`GBC.Pixel*` classes) is a parallel hierarchy — same approach, different types.
