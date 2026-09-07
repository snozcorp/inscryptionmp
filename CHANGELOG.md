# Changelog

Versions that were actually published. Dates are release dates.

## 1.4.0 — 2026-09-08

The update that made starting a match something both players do.

**Both players vote on the act, and both press start.** The act buttons now show who wants
what, and the match can only be the one you both chose. START MATCH stays dark until you
agree, and the first press commits you rather than dragging the other player in — the panel
holds at 1/2 until the second press lands, and pressing again takes yours back. Before this
either player could start whatever act they had selected, and the other client was told to
follow; if they were still deciding, or halfway through editing a deck, it went anyway.

**A lobby with two people in it stops being advertised.** The search now asks Steam for
lobbies with a free seat, skips any that filled up between the request and the reply, and
the host closes their own lobby the moment an opponent arrives — and reopens it if that
opponent leaves. A third player was previously offered games that were already under way.

**The panel says each thing once.** The Steam status line and the notice underneath it were
written by different parts of the mod and kept arriving at the same sentence, so "2 games
found" appeared twice, and the chip said "WAITING" over "Waiting for someone to join". The
status line now carries its own colour instead of raising a notice that repeats it, the
chip drops a detail row that only restates its state, and the panel refuses to draw a line
it has already drawn in different words.

**Finding a game got less like running a query.** The lobby list now re-searches itself
every few seconds while it is on screen, so a game hosted after you looked still turns up
without you pressing anything, and a background search that fails leaves the list you had
rather than blanking it. Clicking a lobby that says Act 3 also opens your vote on Act 3,
because that is why you clicked it — you no longer arrive at an instant disagreement with
the person you just decided to play.

**The act buttons carry their deck's size.** `Act 2   12`, so the thing you are choosing
between is visible without clicking through all three. An act you have never saved a deck
for shows no number at all rather than a misleading zero.

**A result belongs to one opponent.** "You won" used to survive a disconnect and sit on the
panel while you set up a match against somebody else entirely.

**Hosting twice no longer strands a lobby.** Pressing Host Lobby while already hosting
created a second lobby and abandoned the first, which stayed advertised with nobody
watching it. While hosting or connecting, the panel now shows what it is waiting for and
a Cancel button, rather than the buttons that got you there.

## 1.3.0 — 2026-09-06

The update that stopped the two screens quietly disagreeing with each other.

**Both boards now show the same numbers.** The turn snapshot carries each card's attack,
health and any sigils it has picked up, not just which card is in which slot. Before this,
both clients simulated combat independently and usually agreed — but nothing corrected
them when they didn't, so a buffed or damaged card kept different numbers on each screen
for the rest of the match.

**Sigils on your own cards.** Put extra sigils on any card in your deck, chosen from the
set the game itself considers graftable — Leshy's totem set in Acts 1 and 2, P03's builder
set in Act 3. The picker shows the card wearing each sigil rather than listing names, so
you choose by looking at it. A card can show two sigils in total, its own included,
because Act 2 lays out two and draws a card carrying more with none at all.

**A multiplayer card on the title screen.** Drag it into the slot to open the menu,
alongside New Game and the rest. F7 still works everywhere.

**Sniper is aimed by whoever owns the card.** The game's own aiming prompt never checks
whose card it is — fine in single player, where only you have one. Over a network it asked
you to aim your opponent's card, at the wrong side of the board.

**Latchers latch onto the same card on both screens.** `Latch.OnPreDeathAnimation` had the
same flaw: the player who owns the latcher picks a target by hand, while on the other
screen that card is an opponent card, so that client runs an AI instead — one that
deliberately favours the opposite half of the board for a negative sigil. The two clients
ended up with the bomb on different cards, and once it went off, disagreeing about which
cards were still alive. The owner's choice now travels, and the AI only runs if the peer
never says.

**The menu was rebuilt.** The game's own font, bigger text and boxes, three screens
instead of one crowded panel, and a line that tells you what just happened rather than
leaving you to guess whether a click did anything.

**A match carries no consumable items.** A synthesised run was being seeded with the
campaign's starting items, and none of them survive a match: the Pliers and the Dagger
deal damage straight to the scales that the peer never hears about, so from that moment
the two clients disagree about the score. The Dagger also writes `SpecialDaggerUsed` to
the save file's story events — the player's real profile, which a match has no business
touching — and queues a post-battle map node onto a run with no map. Both players now get
the same empty slots. The hammer is unaffected: it only ever smashes your own cards, and
the empty slot is in the next snapshot like any other death. Syncing the rest is a
feature for later.

### Deck builder

- Available before you connect to anyone. Picking an act and building its deck needed a
  peer, though nothing about either does, so you couldn't build a deck until someone was
  already waiting on you.
- Two copies of the same card can each take their own sigils. The clicked card was matched
  back to a deck entry by name, so with duplicates you always edited the first one — and it
  opened already full, which looked like the sigils had landed on every copy.
- The card count reads `deck 8/20` again. It was rendering as `deck`: these labels wrap by
  default, and a box a shade too narrow keeps the first word and drops the rest.
- Cards no longer survive into the next page and sit underneath it.
- Separate **Add Sigils** and **Delete Card** buttons once a card is selected.

### Also

- The title-screen card was appearing in the in-game pause menu too, on top of whatever
  card was already there; `PauseMenu` owns a `MenuController` of its own, so the patch
  caught both.
- Without Kaycee's Mod the card landed on Exit Game, because `TweenInCards` removes the
  ascension card after our hook has already laid the row out around it.
- Reconciling a peer's card stopped re-adding a sigil it was printed with, which gave it
  two of the same icon — and Act 2 draws none at all past what its layout covers.
- Act 2 clamps a card's sigil display instead of blanking it.
- Deck entries can carry sigils (`CardName:Sigil,Sigil`); older deck files still load.
- Six issues found in a review pass over the sync and UI work.

**Compatibility.** Protocol 3, negotiated rather than enforced: a 1.3 client speaks
protocol 2 to a 1.1.x or 1.2.x peer and goes without the extras above. 1.0.x is still
refused — it predates act negotiation, so the two clients would load different scenes.

There was no 1.2.0 release; that work shipped here.

## 1.1.1 — 2026-09-03

- The Steam lobby search reported nothing while it ran, so Find Games looked broken
  when it was simply finding no lobbies. It now says what it is doing.

## 1.1.0 — 2026-09-03

- **All three acts.** Act 2's GBC board and Act 3's P03 table join Act 1's cabin. The
  host picks; both clients follow.
- One deck per act, each holding the cards that act can draw and pay for.
- Act 2 and Act 3 cards render properly in the deck builder.
- **Does not play against 1.0.x** — the act is negotiated at match start.

## 1.0.2 — 2026-09-03

- Fixes to the deck pool, the card table and the menu.

## 1.0.1 — 2026-09-03

- Screenshots, an accurate limitations list, and AI-assistance disclosure.

## 1.0.0 — 2026-09-02

First release. Steam lobbies, direct IP as a fallback, a versus mode off the main menu
that leaves the campaign save alone, a deck builder on the game's own card table, turn
order, board sync, sacrifices, combat, win and loss, and a two-minute reconnect window.
