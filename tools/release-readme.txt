Inscryption Online v@VERSION@
=============================

Networked player-versus-player for Inscryption, inside the real game.
All three acts: Leshy's table, the GBC pixel game, and P03's board.

REQUIRES BepInEx 5.4.x (x86 / 32-bit, Mono build).

INSTALL
  1. Install BepInEx into your Inscryption folder if you haven't already.
     Steam -> right-click Inscryption -> Manage -> Browse local files.
  2. Copy BepInEx/plugins/InscryptionMP.dll from this archive into the same
     path in your game folder.
  3. Launch the game. There is a new card on the title screen - drag it into
     the slot - or press F7 at any time.

VERSIONS
  1.3.x plays against 1.1.x and 1.2.x, dropping back to what the older
  client understands: no stat corrections, no synced sigils, no Sniper
  aiming. 1.0.x is refused outright, and says so at the handshake rather
  than failing halfway into a match.

PLAYING
  One player clicks Host Lobby, the other clicks Find Games and picks it.
  Pick an act - the host's choice decides the table, and the other client
  follows it. Either player then clicks START MATCH.

  F7  menu        F8  start match        F12  abort out of anything

DECK
  F7 -> EDIT DECK opens the game's own card table. Click a card to add it.
  In View My Deck, clicking a card selects it, and the bar then offers
  Add Sigils or Delete Card. 6-20 cards, each player brings their own.

SIGILS
  Up to two extra sigils on any card in your deck, chosen from what the
  game itself considers graftable - Leshy's totem set in Acts 1 and 2,
  P03's builder set in Act 3. The picker shows the card wearing each
  sigil, so you choose by looking rather than by reading a list.

  Each act has its own deck, holding the cards that act can draw and pay
  for. Act 2 and Act 3 cards are browsed on Act 1's table, so their pixel
  art looks blocky there; in a match they render natively.

Your campaign save is never written to during a match.
