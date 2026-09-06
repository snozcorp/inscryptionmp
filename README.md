# Inscryption Online

Networked player-versus-player for Inscryption, running **inside the real game client**.

Everything that existed before this was either local hotseat, a Tabletop Simulator
table, or a browser/Godot reimplementation of the card game. This is the actual game,
over the network, against another person.

![A versus match in progress](https://raw.githubusercontent.com/snozcorp/inscryptionmp/main/docs/match.png)
*A match between two players - the opponent's board, turn indicator, and your hand.*

![Deck builder](https://raw.githubusercontent.com/snozcorp/inscryptionmp/main/docs/deck-builder.png)
*Deck building uses the game's own card table.*

## What works

- **All three acts.** Play on Leshy's table, the GBC pixel game, or P03's board. The
  host picks; both clients load the same act. Each act keeps its own deck.
- **Steam lobbies and P2P** — host, browse, join. No IPs, no port forwarding, no
  master server. Verified across two machines on two Steam accounts.
- **Direct IP / LAN** as a fallback for non-Steam copies.
- **Standalone versus mode** launched from the main menu. No campaign run required, no
  map node consumed, and saving is disabled for the duration so a match can never touch
  your save.
- **Real turn order** with a turn indicator; cards appear on the opponent's board as
  they are played.
- **Authoritative board sync** each turn, carrying each card's attack, health and any
  sigils it has picked up — so a buffed or damaged card reads the same on both screens.
- **Deck builder** using the game's own 3D card table, paged, persisted to disk — one
  deck per act, each restricted to cards that act can actually draw and pay for.
- **Sigils on your own cards.** Put up to two extra sigils on any card in your deck. The
  picker shows that card wearing each sigil, so you choose by looking at cards rather than
  reading a list, and only sigils the act can actually draw are offered.
- **A multiplayer card on the title screen** — drag it into the slot to open the menu,
  alongside New Game and the rest.
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

**Version compatibility.** The handshake carries a protocol version, and a newer client
speaks an older one's dialect rather than refusing it:

| Your build | Plays against |
|---|---|
| 1.3.x | 1.3.x, and 1.1.x–1.2.x without the newer extras |
| 1.1.x–1.2.x | each other, and 1.3.x |
| 1.0.x | nothing newer |

Against a pre-1.3 peer you lose stat corrections, synced sigils and Sniper aiming — the
match works, there is just information their client never sends. 1.0.x is refused outright
because it predates act negotiation, so the two clients would load different scenes.
Mismatches are reported at the handshake, not halfway into a match.

## Playing

1. One player clicks **Host Lobby**, the other clicks **Find Games** and picks the lobby.
2. Pick an **act**. The host's choice decides the table; the other client follows it
   and uses its deck for that act.
3. Either player clicks **START MATCH** — both clients enter a match together.
4. The host takes the first turn. Ring the bell to pass.

**F7** menu · **F8** start match · **F12** abort out of anything

## Deck building

**F7 → EDIT DECK.** Browse all cards and click to add. In **View My Deck**, clicking a
card selects it — the bar then offers **Add Sigils** or **Delete Card**. Decks are 6–20
cards, stored per act in `BepInEx/config/inscryptionmp-deck-act1.txt` (and `-act2`,
`-act3`), one card per line as `CardName` or `CardName:Sigil,Sigil`.

**Sigils.** Up to two per card, chosen from what the game itself considers graftable —
Act 1 and 2 offer Leshy's totem set, Act 3 offers P03's. Two is the engine's limit rather
than a balance decision: Act 2 keeps one icon layout per sigil count and a card carrying
more than the layouts cover draws none at all.

Each act offers the cards that belong to it: Leshy's creatures in Act 1, all four
scrybes in Act 2's GBC game, P03's machines in Act 3. Cards an act can't pay for are
left out, so an Act 1 deck can't smuggle in energy costs. Act 2 and Act 3 cards are
drawn on Act 1's table while you browse, so their pixel art appears a little blocky
there — in a match they render natively.

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
`QueueNewCards()`. It holds for every act, because each act's opponent derives from that
same base — `Part1Opponent`, `PixelOpponent` and `Part3Opponent` are all replaced the
same way.

What differs between acts is everything around the battle: Act 1 and Act 3 are explorable
scenes with a `GameFlowManager`, while Act 2's `GBC_CardBattle` has none and is driven
from an overworld we never load. Each act also has its own draw pile reading its own deck
store. Those differences live in `MatchAct.cs` rather than being special-cased at each
call site.

What differs between acts is everything *around* the battle, and that is where the work
went: Act 2's scene has no `GameFlowManager` at all, each act draws from its own deck
store, and each renders cards through a different displayer. Those differences live in
`MatchAct.cs` rather than being special-cased at every call site.

See `NOTES.md` for the engine details and the bugs that cost real time — including the
ones worth knowing before you write an Inscryption mod of your own.

## Not done

- Acts 2 and 3 have been played end to end, but against a scripted test client rather
  than two real ones. Act 1 is the one verified across two machines and two Steam accounts.
- Each client is authoritative over its own board, which is inherent to peer-to-peer with
  no referee. Play with people you trust.
- **No consumable items.** The Pliers, the Dagger and the rest deal damage straight to
  the scales that the other player never hears about, and the Dagger writes to the save
  file's story events. A match carries none until they can be synced properly.
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
