# Inscryption Online

Networked player-versus-player for Inscryption, running **inside the real game client**.

Everything that existed before this was either local hotseat, a Tabletop Simulator
table, or a browser/Godot reimplementation of the card game. This is the actual game,
over the network, against another person.

## What works

- **Steam lobbies and P2P** — host, browse, join. No IPs, no port forwarding, no
  master server. Verified across two machines on two Steam accounts.
- **Direct IP / LAN** as a fallback for non-Steam copies.
- **Standalone versus mode** launched from the main menu. No campaign run required, no
  map node consumed, and saving is disabled for the duration so a match can never touch
  your save.
- **Real turn order** with a turn indicator; cards appear on the opponent's board as
  they are played.
- **Authoritative board sync** each turn, so sacrifices and combat deaths stay correct.
- **Deck builder** using the game's own 3D card table, paged, persisted to disk.
- **Clean match end** on both clients, back to the main menu with the result.

## Install

1. Steam → Library → right-click **Inscryption** → Manage → **Browse local files**
2. Extract `InscryptionMP-install.zip` into that folder, next to `Inscryption.exe`:
   ```
   Inscryption.exe
   winhttp.dll
   doorstop_config.ini
   BepInEx/plugins/InscryptionMP.dll
   ```
3. Launch the game and press **F7**.

Both players need the same build.

## Playing

1. One player clicks **Host Lobby**, the other clicks **Find Games** and picks the lobby.
2. Either player clicks **START MATCH** — both clients enter a match together.
3. The host takes the first turn. Ring the bell to pass.

**F7** menu · **F8** start match · **F12** abort out of anything

## Deck building

**F7 → DECK → Open Card View.** Click a card to add it; **View My Deck** to remove.
Decks are 6–20 cards, stored in `BepInEx/config/inscryptionmp-deck.txt`.

Each player brings their own deck. Decks are never synchronised — the peer only ever
resolves a card *name*, which the game's own `CardLoader` already handles.

The card pool ignores campaign progression on purpose: a versus deck shouldn't be gated
behind someone's single-player unlocks.

## How it works

Each client runs an ordinary single-player battle in which **it** is the player and the
opponent is driven by the network instead of the AI. A card the peer plays into their
slot N materialises in our opponent slot N. The engine's own combat resolution runs
untouched.

That is the whole trick, and it falls out of `Opponent` being abstract with a virtual
`QueueNewCards()`.

See `NOTES.md` for the engine details and the bugs that cost real time.

## Not done

- Act 2 (`GBC.Pixel*`) is a parallel class hierarchy and is untouched.
- Some rarer card effects may not replicate perfectly on the opponent's screen.
  Please report anything that looks wrong.
- Play with people you trust — this is built for playing with friends, not for
  competitive or ranked play.

## Building

```bash
tools/deploy.sh          # build + install into every local copy of the game
tools/peer.py            # scripted opponent for testing without a second client
```

Requires the .NET SDK. BepInEx packages come from `https://nuget.bepinex.dev/v3/index.json`
(see `NuGet.config`) — they are not on nuget.org.

## AI disclosure

Most of this mod's code was written by **Claude Opus 5 (Anthropic)**, working with the
author across a single extended session: the author drove the design, tested every
build against a second machine, and found a good number of the bugs.

Disclosed per Thunderstore's policy on LLM and AI-generated files
(https://wiki.thunderstore.io/llms-and-ai-generated-files). The same disclosure is
present in the assembly metadata (`AI_Assisted_Creation`, `AI_Model_Vendor`,
`AI_Model`), and every commit in the repository carries a `Co-Authored-By` trailer.
