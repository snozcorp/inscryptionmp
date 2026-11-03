# Changelog

Versions that were actually published. Dates are release dates.

## 2.0.0 — 2026-11-03

The update that makes both screens agree. **Both players need 2.0** (see the end).

**The scales no longer drift apart.** In a longer match, and most of all with "damage to
win" above 5, the two players could end up looking at different scores. The cause was
one more hard-coded 5 that 1.5 missed. When you attack, the game cuts your damage off at
the point where it would tip the scales past 5. It does that only on the attacker's own
screen, so with the post at 10, sitting on +3 and hitting for 4 moved your scale 2 and your
opponent's 4. The cut now follows the match's real post. As a backstop, each player sends
their scale with their turn, and if the other screen disagrees it leans its scale to match.

**Your opponent's sigils stop paying you.** On your screen, their Bees Within put a bee in
*your* hand. Rabbit Hole, Fecundity, Unkillable, Steel Trap's pelt, Bone Digger, the gem and
energy sigils and every other "gain a card / bone / item" sigil did the same: the game never
asks whose card triggered them. The owner's screen already does it for real, so now the
other screen leaves it alone.

**Cards that count things count the right player's.** Hand Tentacle, Lammergeier and the
sacrifice counter on your opponent's cards read *your* hand, bones and sacrifices, so the
numbers on their side of the board were yours. Each player now sends theirs with their
turn. Bell Tentacle counts the chimes beside it on its own side of the table,
not the ones in the same spot on yours.

**Many Lives survives being sacrificed on your opponent's screen**, instead of dying there
while it lived on yours.

**Daus answers only its own bells.** It struck back at any bell anyone hit, so your Daus
punished your own cards for hitting the opponent's chimes.

**The Ijiraq is the Ijiraq.** It disguises itself whenever it's shown off the board, and
the deck builder saved whatever it was shown, so it ended up in your deck as "Mole Man?" or
a copy of another card. It now stays itself in the deck builder, and still disguises itself
in your hand during a match.

**A new opening hand every match.** The game seeds its shuffle from your campaign run, and a
match's run is the same every time, so the same deck dealt the same opening hand match after
match. Each match now rolls its own.

**An END TURN button.** The bell is still there. The button sits under the score at the
top of the screen whenever the bell would answer. It's for anyone who couldn't find the
bell, which was the most common question on Nexus.

**The score shows the post.** Each side reads like `+3 / 10`: three up, ten to win.

**Deck codes.** **Copy deck code** puts your deck on the clipboard as one line, sigils and
order included. **Paste deck code** loads a friend's into the matching act and keeps your
old deck in a backup file.

**A match summary.** The lobby shows rounds, time, the final scales and how many cards each
of you played under the result.

**Open log folder**, on the home screen, for bug reports.

**Act 2 and Act 3 cards show their costs on Act 1's table.** Act 1's card renderer has no
art for energy or gem costs, so most Tech and Mox cards had a blank corner in the deck
builder. It now draws a battery or the gems in that corner.

**Compatibility.** 2.0 refuses every 1.x build. The fixes above change what each client
*does* during combat, not just what it says, so a 1.5 client would bring every one of
those desyncs straight back. The handshake says so before anything loads, and names both
versions.

## 1.5.0 — 2026-09-12

The update that let you set the rules, and put a lobby around them.

**Nine match rules, set by the host.** Card draw, how far the scales have to tip to win,
starting hand, draws a turn, deck size, sigils a card, what's in the side pile, who takes
the first turn, and starting bones. They live on their own screen, they all travel to the
other player, and both of you play under the same set. Default is one click away and is
still the game's own rules.

The one that changes the most is **damage to win**: Inscryption has no hit points, you win
when your damage lead reaches five, and five was a constant compiled into four different
places. Set it to 3 for a knife fight or 10 for a long game.

**Card draw: Random, or In order.** In order deals off the top of your deck, in the order
you built it, so the deck is a plan rather than a bag. Suggested by Zucker_Schock.

**The panel is a menu now, not one long page.** It opens on four choices — Online play,
Local play, Edit deck, Match rules — and each of them is its own screen. Before this every
one of those was stacked onto a single panel that ran off the bottom of smaller windows.

**A lobby that looks like one.** Both players by name, each with their own ready state, and
one row each for the act, the deck and the rules. Press READY; it holds at 1/2 until the
other one does, and the match starts the moment it says 2/2. Your name comes from Steam, or
you can set one in the config for a direct connection.

**The score is across the top.** Your name and how far ahead you are, whose turn it is, and
the same for your opponent — green when you're up, red when you're not. It replaces the
status chip in the corner, which could only ever show one side of a thing that has two.

**Chat, in the corner.** A folded tab at the bottom right that opens into a log and a box —
type, press enter, done. It stays there through the lobby, the deck table and the match, and
tells you when there is something unread. During a match the number keys **1-6** say a fixed
phrase without typing, because Inscryption reads the keyboard straight out of its update loop
and a text field on the board would be fighting the game for every keystroke.

**Both players get the same second pile, and you can turn it off.** Act 1's squirrel pile
becomes ten *bees* once you've found the bee figurine in single player, and Act 3's vessels
come with gems in them once you've fetched the gems module. A bee is 1/1 and airborne where
a squirrel is 0/1, so two players in the same match were playing with materially different
piles, decided by how far each of them had got in their own campaign and mentioned nowhere.
Now it is the same for both, and it is a rule: standard, bees for everyone, or nothing at
all, in which case your deck is all you draw from.

**Your deck has an order, and you can set it.** In **View My Deck**, a selected card shows
its draw position along the bottom with **Move Earlier** and **Move Later**. It matters under
the In order rule and is harmless under Random, where it is just the order you browse in.

**Squirrels are in the card list.** They always belonged in a deck — the starter deck the mod
hands out is three of them — but nothing in the game marks a squirrel as a card it would ever
*offer* you, so the browser had been filtering them out since the first release. Act 3's
empty vessels were missing for the same reason. Also spotted by Zucker_Schock.

For anyone wondering what the three squirrels in the starter deck were for: they aren't the
squirrel supply. Act 1 has a second pile beside your deck holding ten of them and you can
draw from it any turn, exactly as in the campaign. The three in the deck are simply what the
campaign's own starting deck has.

**Alt-tabbing no longer stops your side of the match.** Unity does not update a window that
isn't in front unless it is asked to, so clicking away froze your client where it stood:
nothing read off the socket, no card your opponent played arrived, and the turn never came
back. From their side you had simply stopped playing without disconnecting.

**Leaving a match told the wrong person they had lost.** Every part of the mod that reports a
result means "here is your result" — conceding by walking out sends *you won* — but the
receiving end read the same message as the sender's own result and printed its opposite. Quit
mid-match and your opponent was told they had lost the match they had just been handed. The
wire format is unchanged, so a 1.4 opponent's report is still understood correctly; it is
only their own screen that keeps their build's answer.

**A deck naming a card that doesn't exist no longer takes the deck with it.** The game's
`CardLoader.GetCardByName` throws on an unknown name rather than returning nothing, and the
mod asked it about names from hand-edited deck files and straight off the wire. A typo in
`inscryptionmp-deck-act1.txt` broke loading; an unrecognised card name from a peer broke their
whole turn instead of skipping one card.

**The panel fits smaller windows.** It is drawn at its designed size wherever there is room
and comes down until it fits where there isn't — below about 800 pixels of height it used to
run off both ends, losing the title and the hotkey line. The version in the corner also stopped
cutting its last character off, having called itself "v1.4." for several releases.

**Act 2 stopped explaining itself mid-match.** The GBC game talks to you through its own
text box, with a character who walks on to deliver the line — so a sigil you had not seen
before would stop the match to describe itself. Acts 1 and 3 were already quiet; they use a
different one.

**Nothing blank on the GBC table.** A card with no pixel art draws as an empty rectangle with
stats on it, and there is now a check right before a card is dealt as well as when the deck
loads. Ant and Skink are the ones you would have noticed.

**Quitting a match through the pause menu left the mod holding it.** The game's own way out
goes straight to the start screen, around everything the mod hooks, so nothing told it the
match was over - the score bar stayed on the title screen and the opponent was left waiting
for a turn that was never coming. Now any route out of a battle ends the match and concedes
it, whether the mod was asked or not.

**Nobody is called nothing.** Steam hands back a name made of invisible characters when its
API never initialised, which drew as an empty gap where a player should be. A name with
nothing readable in it is refused, and the seat falls back to Host or Guest.

**Smaller things.** A twenty-first card now says the deck is full instead of doing nothing.
The GBC board took its colour from your deck's temple and got Nature every time once any card
in that deck carried a sigil. Hosting, cancelling and hosting again in quick succession could
leave the transport reading the peer happily while every send was dropped as "not connected".
A network opponent no longer offers you a surrender it has no way to honour.

**Under the hood.** The mod can now play itself: two real game clients drive a whole match
from `tools/test-all.sh` with nobody at either keyboard, which is how everything above was
checked on all three acts.

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
