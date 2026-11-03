using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using InscryptionMP;

namespace InscryptionMP.Tests
{
    /// <summary>
    /// Checks the parts of the mod a game client isn't needed for: the wire format, and the
    /// rules payload that rides on it. Everything else is tested by running two clients -
    /// see tools/match-test.sh.
    /// </summary>
    internal static class Program
    {
        private static int _failed;
        private static int _passed;

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL  " + what);
        }

        private static void Eq(object actual, object expected, string what)
        {
            bool ok = Equals(actual, expected);
            if (!ok) Console.WriteLine($"FAIL  {what}: got '{actual}', wanted '{expected}'");
            if (ok) _passed++; else _failed++;
        }

        /// <summary>Where this file sits, so the tests can read the sources they shadow.</summary>
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        private static string SourceDir => Path.GetFullPath(Path.Combine(Here(), "..", "..", "src"));

        private static string GrepOne(string file, string pattern, string what)
        {
            string path = Path.Combine(SourceDir, file);
            if (!File.Exists(path)) { Console.WriteLine($"FAIL  cannot read {path}"); _failed++; return null; }

            Match m = Regex.Match(File.ReadAllText(path), pattern);
            if (!m.Success) { Console.WriteLine($"FAIL  {what}: no match in {file}"); _failed++; return null; }
            return m.Groups[1].Value;
        }

        /// <summary>
        /// The shimmed version and the manifest are copies of what src/Plugin.cs says. A
        /// release where they disagree ships a build that greets peers as the wrong version.
        /// </summary>
        private static void Versions()
        {
            string real = GrepOne("Plugin.cs", "Version = \"([^\"]+)\"", "plugin version");
            Eq(Plugin.Version, real, "the test shim matches src/Plugin.cs");

            string csproj = GrepOne("InscryptionMP.csproj", @"<Version>([^<]+)</Version>", "csproj version");
            Eq(csproj, real, "the csproj matches src/Plugin.cs");
        }

        private static int Main()
        {
            Versions();
            Handshake();
            Scale();
            State();
            DeckCodes();
            Votes();
            RulesWire();
            RulesPayload();
            Slots();
            Plays();
            Aims();
            Latches();
            Results();
            Talking();
            Dials();

            Console.WriteLine($"\n{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void Handshake()
        {
            Eq(Protocol.Version, 6, "protocol version");
            Check(Protocol.Hello.StartsWith("HELLO 6 "), "greeting carries the version");

            Check(Protocol.TryParseHello("HELLO 6 2.0.0", out int p, out string v), "greeting parses");
            Eq(p, 6, "greeting protocol");
            Eq(v, "2.0.0", "greeting mod version");

            Check(Protocol.TryParseHello("HELLO 2", out int old, out _), "a greeting without a version parses");
            Eq(old, 2, "old greeting protocol");
            Check(!Protocol.TryParseHello("HELO 6 2.0.0", out _, out _), "a misspelt greeting is refused");
            Check(!Protocol.TryParseHello("HELLO x 2.0.0", out _, out _), "a non-numeric protocol is refused");

            // 2.0 changed what both clients simulate in combat; a 1.x peer would desync.
            Eq(Protocol.MinCompatible, 6, "1.x peers are refused");
            Check(5 < Protocol.MinCompatible, "protocol 5 (mod 1.5) is too old");
        }

        /// <summary>
        /// The sender's balance, taken as truth by the receiver. A negative balance is the
        /// common case - whoever is behind sends one every other turn.
        /// </summary>
        private static void Scale()
        {
            Eq(Protocol.Scale(3), "SCALE 3", "a lead encodes");
            Eq(Protocol.Scale(-4), "SCALE -4", "a deficit encodes");
            Eq(Protocol.Scale(0), "SCALE 0", "level encodes");

            Check(Protocol.TryParseScale("SCALE -4", out int b), "a deficit parses");
            Eq(b, -4, "parsed deficit");
            Check(Protocol.TryParseScale(Protocol.Scale(7), out int round), "a scale round-trips");
            Eq(round, 7, "round-tripped balance");

            Check(!Protocol.TryParseScale("SCALE", out _), "a scale with no number is refused");
            Check(!Protocol.TryParseScale("SCALE x", out _), "a non-numeric scale is refused");
            Check(!Protocol.TryParseScale("STATE 1 2 3", out _), "STATE is not a scale");
            Check(!Protocol.TryParseScale(null, out _), "null is not a scale");
        }

        private static void State()
        {
            Eq(Protocol.State(5, 3, 1), "STATE 5 3 1", "a state encodes");
            Check(Protocol.TryParseState("STATE 5 3 1", out int hand, out int bones, out int sacs), "a state parses");
            Eq(hand, 5, "parsed hand");
            Eq(bones, 3, "parsed bones");
            Eq(sacs, 1, "parsed sacrifices");

            Check(Protocol.TryParseState(Protocol.State(0, 0, 0), out _, out _, out _), "an empty state round-trips");
            Check(!Protocol.TryParseState("STATE 5 3", out _, out _, out _), "a short state is refused");
            Check(!Protocol.TryParseState("STATE 5 3 1 9", out _, out _, out _), "a long state is refused");
            Check(!Protocol.TryParseState("STATE 5 -3 1", out _, out _, out _), "negative bones are refused");
            Check(!Protocol.TryParseState("STATE a b c", out _, out _, out _), "a non-numeric state is refused");
            Check(!Protocol.TryParseState("SCALE 3", out _, out _, out _), "SCALE is not a state");
            Check(!Protocol.TryParseState(null, out _, out _, out _), "null is not a state");

            // Neither prefix may swallow the other's lines, or one would eat the other's turn.
            Check(!Protocol.StatePrefix.StartsWith(Protocol.ScalePrefix)
                  && !Protocol.ScalePrefix.StartsWith(Protocol.StatePrefix), "STATE and SCALE prefixes are distinct");
        }

        private static void DeckCodes()
        {
            var deck = new[] { "Stoat", "Wolf:Sniper,Flying", "Squirrel", "Squirrel" };
            string code = DeckCode.Encode(MatchAct.Act3, deck);
            Check(code.StartsWith("IODECK1-3-"), "a deck code names its act");
            Check(code.IndexOf('=') < 0 && code.IndexOf('+') < 0 && code.IndexOf('/') < 0,
                  "a deck code is URL- and chat-safe");

            Check(DeckCode.TryDecode(code, out MatchAct act, out var back), "a deck code decodes");
            Eq(act, MatchAct.Act3, "decoded act");
            Eq(string.Join("|", back), string.Join("|", deck), "decoded entries, sigils and order kept");
            Check(DeckCode.TryDecode("  " + code + "\n", out _, out _), "surrounding whitespace is ignored");

            Check(!DeckCode.TryDecode("", out _, out _), "an empty paste is refused");
            Check(!DeckCode.TryDecode("hello there", out _, out _), "ordinary text is refused");
            Check(!DeckCode.TryDecode("IODECK1-9-" + code.Substring(10), out _, out _), "an unknown act is refused");
            Check(!DeckCode.TryDecode("IODECK1-1-!!!!", out _, out _), "a broken payload is refused");
            Check(!DeckCode.TryDecode(DeckCode.Encode(MatchAct.Act1, new string[0]), out _, out _),
                  "an empty deck is refused");
            Check(!DeckCode.TryDecode(DeckCode.Encode(MatchAct.Act1, new[] { "Two Words" }), out _, out _),
                  "a name with a space is refused");
        }

        private static void Votes()
        {
            Eq(Protocol.Vote(MatchAct.Act3), "VOTE 3", "vote encodes");
            Check(Protocol.TryParseVote("VOTE 2", out MatchAct act), "vote parses");
            Eq(act, MatchAct.Act2, "vote act");
            Check(!Protocol.TryParseVote("VOTE 9", out _), "an act that doesn't exist is refused");
            Check(!Protocol.TryParseVote("VOTE", out _), "a vote with no act is refused");

            Eq(Protocol.Ready(true), "READY 1", "ready encodes");
            Check(Protocol.TryParseReady("READY 0", out bool ready), "ready parses");
            Eq(ready, false, "ready value");
            Check(!Protocol.TryParseReady("READY 2", out _), "a nonsense ready is refused");

            Eq(Protocol.StartMatch(MatchAct.Act1), "START 1", "start encodes");
            Check(Protocol.TryParseStart("START 3", out MatchAct s), "start parses");
            Eq(s, MatchAct.Act3, "start act");
        }

        /// <summary>Names, typed lines and the fixed phrases.</summary>
        private static void Talking()
        {
            Eq(Protocol.NameLine("snozcorp"), "NAME snozcorp", "a name encodes");
            Check(Protocol.TryParseName("NAME snozcorp", out string who), "a name parses");
            Eq(who, "snozcorp", "the name itself");
            Check(!Protocol.TryParseName("NAME ", out _), "an empty name is refused");

            // Chat carries the rest of the line, spaces and all - splitting on spaces
            // would deliver "gg" out of "gg well played".
            Eq(Protocol.ChatLine("gg well played"), "CHAT gg well played", "a line encodes");
            Check(Protocol.TryParseChat("CHAT gg well played", out string said), "a line parses");
            Eq(said, "gg well played", "the whole line survives");
            Check(!Protocol.TryParseChat("CHAT", out _), "a chat with nothing in it is refused");
            Check(Protocol.TryParseChat("CHAT :", out string colon), "punctuation is not special");
            Eq(colon, ":", "a one-character line survives");

            // Sent as a number so each build shows its own wording rather than the sender's.
            Eq(Protocol.EmoteLine(2), "EMOTE 2", "an emote encodes");
            Check(Protocol.TryParseEmote("EMOTE 2", out int emote), "an emote parses");
            Eq(emote, 2, "the emote index");
            Check(!Protocol.TryParseEmote("EMOTE x", out _), "a non-numeric emote is refused");
        }

        /// <summary>
        /// Every dial has to be able to find the value it just set. A dial whose IndexOf
        /// and WithIndex disagree looks like a button that does nothing.
        /// </summary>
        private static void Dials()
        {
            Eq(RuleBook.Dials.Length, 9, "the rules screen has nine dials");

            foreach (RuleBook.Dial dial in RuleBook.Dials)
            {
                Check(dial.Choices.Length >= 2, dial.Key + " offers a choice");
                Check(!string.IsNullOrEmpty(dial.Note), dial.Key + " says what it does");

                for (int i = 0; i < dial.Choices.Length; i++)
                {
                    Rules turned = dial.WithIndex(Rules.Default, i);
                    Eq(dial.IndexOf(turned), i, dial.Key + " finds position " + i + " again");

                    // And it has to survive the wire, or the other player plays something else.
                    Eq(dial.IndexOf(Rules.Decode(turned.Encode())), i,
                       dial.Key + " position " + i + " round-trips");
                }

                Check(RuleBook.Find(dial.Key) == dial, dial.Key + " is findable by key");
            }

            Check(RuleBook.Find("nonsense") == null, "an unknown key finds nothing");
        }

        private static void RulesWire()
        {
            Eq(Protocol.RulesLine("draw=order"), "RULES draw=order", "rules encode");
            Check(Protocol.TryParseRules("RULES draw=order", out string payload), "rules parse");
            Eq(payload, "draw=order", "rules payload");
            Check(!Protocol.TryParseRules("READY 1", out _), "a ready is not a rules line");
            Check(Protocol.TryParseRules("RULES ", out string empty), "an empty rules line parses");
            Eq(empty, "", "empty rules payload");
        }

        private static void RulesPayload()
        {
            Rules d = Rules.Default;
            Eq(d.Draw, CardDraw.Random, "the default deal is random");
            Eq(d.DamageToWin, 5, "the default post is the game's own five");
            Eq(d.StartingHand, 4, "the default hand is four");
            Eq(d.DrawsPerTurn, 1, "the default is one draw a turn");
            Eq(d.MaxDeck, 20, "the default deck cap is twenty");
            Eq(d.SigilsPerCard, 2, "the default is two sigils a card");
            Eq(d.Side, SidePile.Standard, "the default side pile is the act's own");
            Eq(d.GuestFirst, false, "the host plays first by default");
            Eq(d.StartingBones, 0, "you start with no bones");
            Check(!d.Custom, "custom rules are off by default");
            Check(d.IsVanilla, "the defaults are vanilla");

            // Everything at once, there and back, which is the case that matters: a rule
            // that survives alone but not beside the others still loses a match.
            var loud = new Rules
            {
                Custom = true,
                Draw = CardDraw.InOrder,
                DamageToWin = 10,
                StartingHand = 6,
                DrawsPerTurn = 2,
                MaxDeck = 40,
                SigilsPerCard = 4,
                Side = SidePile.Bees,
                GuestFirst = true,
                StartingBones = 3,
            };
            Rules back = Rules.Decode(loud.Encode());
            Check(back.SameAs(loud), "every rule at once round-trips");
            Check(!back.IsVanilla, "a changed rule is not vanilla");
            Check(!loud.SameAs(Rules.Default), "different rules compare unequal");

            Eq(Rules.Decode(Rules.Default.Encode()).SameAs(Rules.Default), true,
               "the defaults round-trip");

            Eq(loud.DrawName, "In order", "in-order reads as a phrase");
            Eq(Rules.Default.DrawName, "Random", "random reads as a phrase");
            Eq(loud.SideName, "Bees", "the side pile reads as a phrase");

            // A later build's extra keys must not take the rules it does understand with
            // them, and an unknown value must fall back rather than throw.
            Eq(Rules.Decode("draw=order;turntimer=30").Draw, CardDraw.InOrder,
               "an unknown key is skipped, not fatal");
            Eq(Rules.Decode("turntimer=30;draw=order").Draw, CardDraw.InOrder,
               "an unknown key first is also skipped");
            Eq(Rules.Decode("draw=sideways").Draw, CardDraw.Random, "an unknown value falls back");
            Eq(Rules.Decode("side=purple").Side, SidePile.Standard, "an unknown side pile falls back");
            Eq(Rules.Decode("").Draw, CardDraw.Random, "an empty payload is the default");
            Eq(Rules.Decode(null).Draw, CardDraw.Random, "no payload is the default");
            Eq(Rules.Decode("garbage").Draw, CardDraw.Random, "junk is the default");
            Eq(Rules.Decode(" draw = order ").Draw, CardDraw.InOrder, "spaces around a pair are ignored");

            // A number nobody should be able to send: zero damage to win is a match that
            // is over before it starts, and a hand of 500 is one that never deals.
            Eq(Rules.Decode("win=0").DamageToWin, 1, "a win post of zero is clamped up");
            Eq(Rules.Decode("win=9999").DamageToWin, 20, "an absurd win post is clamped down");
            Eq(Rules.Decode("hand=500").StartingHand, 10, "an absurd hand is clamped down");
            Eq(Rules.Decode("deck=1").MaxDeck, 6, "a deck cap below the minimum is clamped up");
            Eq(Rules.Decode("bones=-4").StartingBones, 0, "negative bones are clamped up");
            Eq(Rules.Decode("win=notanumber").DamageToWin, 5, "a non-numeric value keeps the default");

            // The summary is read by the player who did not set any of this.
            Eq(Rules.Default.Summary(), "The game's own rules.", "vanilla says so plainly");
            Check(loud.Summary().Contains("10 to win"), "the summary names the win post");
            Check(loud.Summary().Contains("draw in order"), "the summary names the draw");
        }

        private static void Slots()
        {
            Eq(Protocol.EncodeSlot("Wolf", 3, 2), "Wolf:3/2", "a slot encodes");
            Eq(Protocol.EncodeSlot("-", 0, 0), "-", "an empty slot encodes");
            Eq(Protocol.EncodeSlot(null, 0, 0), "-", "a nameless slot is empty");
            Eq(Protocol.EncodeSlot("Wolf", 3, 2, new[] { "Sniper", "Flying" }),
               "Wolf:3/2:Sniper,Flying", "sigils ride along");

            Protocol.SlotState s = Protocol.DecodeSlot("Wolf:3/2:Sniper,Flying");
            Eq(s.Name, "Wolf", "decoded name");
            Eq(s.Attack, 3, "decoded attack");
            Eq(s.Health, 2, "decoded health");
            Eq(s.HasStats, true, "decoded stats present");
            Eq(string.Join(",", s.Sigils), "Sniper,Flying", "decoded sigils");
            Eq(s.IsEmpty, false, "a card slot is not empty");

            Protocol.SlotState empty = Protocol.DecodeSlot("-");
            Eq(empty.IsEmpty, true, "an empty slot decodes as empty");
            Eq(empty.Sigils.Length, 0, "an empty slot has no sigils");

            // Protocol 2 sends bare names. Reading the absent numbers as 0/0 would wipe out
            // the peer's whole board, so "no stats" has to survive the decode.
            Protocol.SlotState bare = Protocol.DecodeSlot("Wolf");
            Eq(bare.Name, "Wolf", "a bare name decodes");
            Eq(bare.HasStats, false, "a bare name reports no stats");

            var line = Protocol.Board(new[] { "-", "Wolf:3/2", "-", "Adder:1/1" });
            Eq(line, "BOARD -|Wolf:3/2|-|Adder:1/1", "a board encodes");
            Check(Protocol.TryParseBoard(line, out string[] slots), "a board parses");
            Eq(slots.Length, 4, "board slot count");
            Eq(slots[1], "Wolf:3/2", "board slot content");
        }

        private static void Plays()
        {
            Eq(Protocol.Play("Squirrel", 2), "PLAY Squirrel 2", "a play encodes");
            Check(Protocol.TryParsePlay("PLAY Squirrel 2", out string name, out int slot), "a play parses");
            Eq(name, "Squirrel", "played card");
            Eq(slot, 2, "played slot");
            Check(!Protocol.TryParsePlay("PLAY Squirrel", out _, out _), "a play with no slot is refused");
            Check(!Protocol.TryParsePlay("PLAY Squirrel x", out _, out _), "a non-numeric slot is refused");

            Eq(Protocol.Sacrifice(1), "SAC 1", "a sacrifice encodes");
            Check(Protocol.TryParseSacrifice("SAC 1", out int sac), "a sacrifice parses");
            Eq(sac, 1, "sacrificed slot");
        }

        private static void Aims()
        {
            Eq(Protocol.Aim(2, new[] { 0, 1 }), "AIM 2 0,1", "an aim encodes");
            Check(Protocol.TryParseAim("AIM 2 0,1", out int from, out int[] targets), "an aim parses");
            Eq(from, 2, "aiming slot");
            Eq(string.Join(",", targets), "0,1", "aimed targets");
            Check(!Protocol.TryParseAim("AIM 2", out _, out _), "an aim with no targets is refused");
        }

        /// <summary>
        /// Both ends have to read these the same way round, and for four releases they did
        /// not: everything that sends one means "here is your result", and the reader took
        /// it as the sender's. Conceding therefore told the other player they had lost.
        /// </summary>
        private static void Results()
        {
            Eq(Protocol.YouWon, "OVER WON", "a win report is on the wire as OVER WON");
            Eq(Protocol.YouLost, "OVER LOST", "a loss report is on the wire as OVER LOST");
            Check(Protocol.YouWon != Protocol.YouLost, "the two results are different lines");
        }

        private static void Latches()
        {
            Eq(Protocol.LatchTarget(true, 3), "LATCH P 3", "a latch on our side encodes");
            Eq(Protocol.LatchTarget(false, 0), "LATCH O 0", "a latch on their side encodes");
            Check(Protocol.TryParseLatch("LATCH P 3", out bool own, out int slot), "a latch parses");
            Eq(own, true, "latch side");
            Eq(slot, 3, "latch slot");
            Check(!Protocol.TryParseLatch("LATCH X 3", out _, out _), "an unknown side is refused");
        }
    }
}
