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
  Both players should be on the same version. 1.5.x still plays against
  1.1.x-1.4.x, dropping back to what the older client understands: no match
  rules against anything before 1.5, no lobby vote before 1.4, and no stat
  corrections, synced sigils or Sniper aiming before 1.3. 1.0.x is refused
  outright, and says so at the handshake rather than failing halfway into a
  match.

PLAYING
  One player clicks Host Lobby, the other clicks Find Games and picks it.
  Both players pick an act - the tally under the buttons shows who wants
  what, and the match can only be the one you both chose. Both players then
  click START MATCH; the first press holds at 1/2 until the second lands.

  F7  menu        F8  ready up        F12  abort out of anything

MATCH RULES
  Under the act buttons, the host sets how cards are dealt:

    Random    the way the game normally deals - a card at random.
    In order  off the top of your deck, in the order you built it.

  Both players play under the same rule, and the one who did not choose it
  can see which it is before pressing start.

DECK
  F7 -> EDIT DECK opens the game's own card table. Click a card to add it.
  In View My Deck, clicking a card selects it, and the bar then offers
  Add Sigils or Delete Card. A strip along the bottom shows where that card
  sits in the deck, with Move Earlier and Move Later - which is the order it
  is dealt in under the In order rule. 6-20 cards, each player brings their
  own.

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
