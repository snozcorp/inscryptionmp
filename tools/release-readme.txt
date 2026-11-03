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
  Both players need 2.0 or newer. 2.0 changed how each client runs combat,
  not just what they say to each other, so it can't play against 1.x - an
  older copy is refused at the handshake, with a message naming both
  versions, before anything loads.

PLAYING
  One player clicks Host Lobby, the other clicks Find Games and picks it.
  Both players pick an act - the tally under the buttons shows who wants
  what, and the match can only be the one you both chose. Both players then
  press READY; the match starts when it reads 2/2.

  To end your turn, ring the bell, or press END TURN under the score at the
  top of the screen. The score reads like +3 / 10: three up, ten to win.

  F7  menu        F8  ready up        F12  abort out of anything

MATCH RULES
  The host sets nine of them on their own screen: card draw (random, or in
  the order you built your deck), damage to win, starting hand, draws a
  turn, deck size, sigils a card, side pile, who goes first and starting
  bones. Both players play under the same set, and the other player can
  read them before readying up.

DECK
  F7 -> EDIT DECK opens the game's own card table. Click a card to add it.
  In View My Deck, clicking a card selects it, and the bar then offers
  Add Sigils or Delete Card. A strip along the bottom shows where that card
  sits in the deck, with Move Earlier and Move Later - which is the order it
  is dealt in under the In order rule. 6-20 cards, each player brings their
  own.

  Copy deck code and Paste deck code, on the same screen, pass a deck around
  as one line of text.

  Act 1 also keeps ten squirrels in a second pile beside your deck, and
  Act 3 ten empty vessels; you can draw from either one on any turn, the
  same as the campaign. Both players get the same pile whatever their own
  single-player progress says.

SIGILS
  Put extra sigils on any card in your deck, chosen from what the game
  itself considers graftable - Leshy's totem set in Acts 1 and 2, P03's
  builder set in Act 3. A card shows two in total, its own included. The
  picker shows the card wearing each sigil, so you choose by looking.

  Each act has its own deck, holding the cards that act can draw and pay
  for. Act 2 and Act 3 cards are browsed on Act 1's table, so their pixel
  art looks blocky there; in a match they render natively.

ITEMS
  A match carries no consumable items. What they do - damage straight to
  the scales, a post-battle map node - is never sent to the other player,
  and the Dagger writes to your save file's story events. Both players get
  the same empty slots until they can be synced properly.

Your campaign save is never written to during a match.

Something wrong? F7 -> Open log folder, and send mp-trace.log and
LogOutput.log from both players.
