# Changelog

Versions that were actually published. Dates are release dates.

## 1.3.0 — unreleased

The update that stopped the two screens from quietly disagreeing.

**Both boards now show the same numbers.** The turn snapshot carries each card's
attack, health and any sigils it has picked up, not just which card is in which slot.
Before this, both clients simulated combat independently and usually agreed — but
nothing corrected them when they didn't, so a buffed or damaged card kept different
numbers on each screen for the rest of the match.

**Sigils on your own cards.** Up to two extra sigils on any card in your deck, chosen
from the set the game itself considers graftable — Leshy's totem set in Acts 1 and 2,
P03's builder set in Act 3. The picker shows the card wearing each sigil rather than
listing names, so you pick by looking at it.

**A multiplayer card on the title screen.** Drag it into the slot to open the menu,
alongside New Game and the rest. F7 still works everywhere.

**Sniper is aimed by whoever owns the card.** The game's own aiming prompt never checks
whose card it is — fine in single player, where only you have one. Over a network it
asked you to aim your opponent's card, at the wrong side of the board.

**The menu was rebuilt.** The game's own font, bigger text and boxes, three screens
instead of one crowded panel, and a single line that tells you what just happened
instead of leaving you to guess whether a click did anything.

**A match carries no consumable items.** A synthesised run was being seeded with the
campaign's starting items, and none of them survive a match: the Pliers and the Dagger
deal damage straight to the scales that the peer never hears about, so from that moment
the two clients disagree about the score. The Dagger also writes `SpecialDaggerUsed` to
the save file's story events — the player's real profile, which a match has no business
touching — and queues a post-battle map node onto a run with no map. Both players now get
the same empty slots. Syncing them properly is a feature for later.

Also:
- The title-screen card was appearing in the in-game pause menu too, on top of whatever
  card was already there; `PauseMenu` owns a `MenuController` of its own, so the patch
  caught both.
- Without Kaycee's Mod the card landed on Exit Game, because `TweenInCards` removes the
  ascension card after our hook has already laid the row out around it.
- Act 2 no longer blanks a card's sigils when it carries more than the pixel layout
  covers; the display is clamped instead.
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
