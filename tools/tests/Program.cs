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
            Votes();
            RulesWire();
            RulesPayload();
            Slots();
            Plays();
            Aims();
            Latches();
            Results();

            Console.WriteLine($"\n{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void Handshake()
        {
            Eq(Protocol.Version, 5, "protocol version");
            Check(Protocol.Hello.StartsWith("HELLO 5 "), "greeting carries the version");

            Check(Protocol.TryParseHello("HELLO 5 1.5.0", out int p, out string v), "greeting parses");
            Eq(p, 5, "greeting protocol");
            Eq(v, "1.5.0", "greeting mod version");

            Check(Protocol.TryParseHello("HELLO 2", out int old, out _), "a greeting without a version parses");
            Eq(old, 2, "old greeting protocol");
            Check(!Protocol.TryParseHello("HELO 5 1.5.0", out _, out _), "a misspelt greeting is refused");
            Check(!Protocol.TryParseHello("HELLO x 1.5.0", out _, out _), "a non-numeric protocol is refused");

            // 1.5 must still be able to sit down with everything back to 1.1.
            Check(Protocol.MinCompatible <= 2, "protocol 2 peers are still allowed");
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
            Eq(Rules.Default.Draw, CardDraw.Random, "the default deal is random");
            Eq(Rules.Default.Encode(), "draw=random", "default encodes");

            var order = new Rules { Draw = CardDraw.InOrder };
            Eq(order.Encode(), "draw=order", "in-order encodes");
            Eq(Rules.Decode(order.Encode()).Draw, CardDraw.InOrder, "in-order round-trips");
            Eq(Rules.Decode(Rules.Default.Encode()).Draw, CardDraw.Random, "random round-trips");

            Eq(order.DrawName, "In order", "in-order reads as a phrase");
            Eq(Rules.Default.DrawName, "Random", "random reads as a phrase");

            Check(order.SameAs(new Rules { Draw = CardDraw.InOrder }), "same rules compare equal");
            Check(!order.SameAs(Rules.Default), "different rules compare unequal");

            // A later build's extra keys must not take the rules it does understand with
            // them, and an unknown value must fall back rather than throw.
            Eq(Rules.Decode("draw=order;handsize=5").Draw, CardDraw.InOrder,
               "an unknown key is skipped, not fatal");
            Eq(Rules.Decode("handsize=5;draw=order").Draw, CardDraw.InOrder,
               "an unknown key first is also skipped");
            Eq(Rules.Decode("draw=sideways").Draw, CardDraw.Random, "an unknown value falls back");
            Eq(Rules.Decode("").Draw, CardDraw.Random, "an empty payload is the default");
            Eq(Rules.Decode(null).Draw, CardDraw.Random, "no payload is the default");
            Eq(Rules.Decode("garbage").Draw, CardDraw.Random, "junk is the default");
            Eq(Rules.Decode(" draw = order ").Draw, CardDraw.InOrder, "spaces around a pair are ignored");
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
