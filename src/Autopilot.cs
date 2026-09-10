using System;
using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Plays the mod by itself, so a change can be tested against two real game clients
    /// without a person at either keyboard. Off unless INSCRYPTIONMP_AUTOPILOT is set, and
    /// everything it does it writes to the trace - the log is the test report.
    ///
    ///   INSCRYPTIONMP_AUTOPILOT        host | join
    ///   INSCRYPTIONMP_AUTOPILOT_ADDR   who to dial (join only, default 127.0.0.1)
    ///   INSCRYPTIONMP_AUTOPILOT_PORT   default 27333
    ///   INSCRYPTIONMP_AUTOPILOT_ACT    1 | 2 | 3
    ///   INSCRYPTIONMP_AUTOPILOT_DRAW   random | order   (host decides)
    ///   INSCRYPTIONMP_AUTOPILOT_DECK   comma-separated card names to build the deck from
    ///   INSCRYPTIONMP_AUTOPILOT_TURNS  how many turns to take before giving up (default 8)
    ///   INSCRYPTIONMP_AUTOPILOT_QUIT   1 to close the game when the run finishes
    /// </summary>
    internal static class Autopilot
    {
        public static bool Active { get; private set; }

        public static string Mode { get; private set; }
        public static string Address { get; private set; } = "127.0.0.1";
        public static int Port { get; private set; } = Net.DefaultPort;
        public static MatchAct Act { get; private set; } = MatchAct.Act1;
        public static string Draw { get; private set; }


        /// <summary>A rules payload for the host to run the match under.</summary>
        public static string RulesPayload { get; private set; }

        /// <summary>What the locked rules must come out as, on both clients.</summary>
        public static string Expect { get; private set; }
        public static string[] Deck { get; private set; }
        public static int MaxTurns { get; private set; } = 8;
        public static bool QuitWhenDone { get; private set; }

        /// <summary>
        /// Stops before starting a match and parks the client somewhere worth looking at,
        /// so the interface can be photographed in a known state rather than described.
        /// "lobby" opens the panel; "deck" opens the card table with a card chosen.
        /// </summary>
        public static string Hold { get; private set; }

        private static string Env(string name)
        {
            try
            {
                string v = Environment.GetEnvironmentVariable(name);
                return string.IsNullOrEmpty(v) ? null : v.Trim();
            }
            catch { return null; }
        }

        public static void Boot(MonoBehaviour plugin)
        {
            Mode = Env("INSCRYPTIONMP_AUTOPILOT");
            if (Mode != "host" && Mode != "join")
            {
                if (Mode != null) Trace.Warn($"[auto] unknown mode '{Mode}' - autopilot stays off");
                return;
            }

            Active = true;
            Address = Env("INSCRYPTIONMP_AUTOPILOT_ADDR") ?? Address;

            int n;
            if (int.TryParse(Env("INSCRYPTIONMP_AUTOPILOT_PORT") ?? "", out n) && n > 0 && n < 65536) Port = n;
            if (int.TryParse(Env("INSCRYPTIONMP_AUTOPILOT_ACT") ?? "", out n) && n >= 1 && n <= 3) Act = (MatchAct)n;
            if (int.TryParse(Env("INSCRYPTIONMP_AUTOPILOT_TURNS") ?? "", out n) && n > 0) MaxTurns = n;

            Draw = Env("INSCRYPTIONMP_AUTOPILOT_DRAW");
            Hold = Env("INSCRYPTIONMP_AUTOPILOT_HOLD");
            RulesPayload = Env("INSCRYPTIONMP_AUTOPILOT_RULES");
            Expect = Env("INSCRYPTIONMP_AUTOPILOT_EXPECT");
            QuitWhenDone = Env("INSCRYPTIONMP_AUTOPILOT_QUIT") == "1";

            string deck = Env("INSCRYPTIONMP_AUTOPILOT_DECK");
            if (deck != null) Deck = deck.Split(',');

            Trace.Info($"[auto] autopilot on: {Mode} {Address}:{Port}, {ActInfo.Name(Act)}, " +
                       $"draw={Draw ?? "(unset)"}, turns={MaxTurns}, quit={QuitWhenDone}");

            // Two clients share one desktop and only one of them can be in front.
            Background.ForceOn();

            plugin.StartCoroutine(Run(plugin));
        }

        /// <summary>Marks the run as finished, one way or the other.</summary>
        private static void Report(bool ok, string what)
        {
            if (ok) Trace.Info("[auto] PASS " + what);
            else Trace.Error("[auto] FAIL " + what);
        }

        private static IEnumerator Run(MonoBehaviour plugin)
        {
            yield return WaitForCards();
            DumpCardNames();

            ActInfo.Selected = Act;
            if (Deck != null) InstallDeck();
            CheckPool();

            // Nothing to connect to: this one is only here to be looked at.
            if (Hold == "menu")
            {
                Trace.Info("[auto] holding on the panel with no opponent");
                MpMenu.OpenFromMenuCard();
                Trace.Info("[auto] ==== RUN OK: holding ====");
                while (true) yield return new WaitForSeconds(5f);
            }

            if (Mode == "host")
            {
                string wanted = RulesPayload;
                if (Draw != null)
                    wanted = (wanted == null ? "" : wanted + ";") + "custom=1;draw=" + Draw;
                if (wanted != null) MatchRules.Adopt(wanted);

                Net.Host(Port);
            }
            else
            {
                Net.Join(Address, Port);
            }

            yield return WaitUntil(() => Net.Connected, 90f, "connect to the peer");
            if (!Net.Connected) { Finish(false, "no connection"); yield break; }

            Report(true, "connected");
            Lobby.ChooseAct(Act);

            yield return WaitUntil(() => Lobby.CanReady, 60f, "the lobby to be startable");
            if (!Lobby.CanReady)
            {
                Trace.Error("[auto] lobby blocked: " + (Lobby.Blocker ?? "(no reason given)"));
                Finish(false, "lobby never became startable");
                yield break;
            }

            Trace.Info($"[auto] rules in effect: {MatchRules.Effective.Encode()}");

            if (Hold != null) { yield return HoldStill(plugin); yield break; }

            yield return Commit(plugin);

            yield return WaitUntil(() => VersusMode.InMatch, 180f, "the match to start");
            if (!VersusMode.InMatch) { Finish(false, "match never started"); yield break; }

            Report(true, $"match started on {ActInfo.Name(ActInfo.Current)} " +
                         $"with rules {MatchRules.Live.Encode()}");
            CheckRules();

            yield return PlayMatch(plugin);
            CheckTalking();
            Finish(true, "run complete");
        }

        /// <summary>
        /// Parks the client with something on screen and never starts a match. There is no
        /// way to read a layout out of a log, so the interface gets looked at instead.
        /// </summary>
        private static IEnumerator HoldStill(MonoBehaviour plugin)
        {
            if (Hold == "deck" || Hold == "reorder")
            {
                Trace.Info("[auto] holding on the deck table");
                MpMenu.OpenDeckTable();

                yield return WaitUntil(() => NativeDeckBuilder.IsOpen, 120f, "the card table");
                yield return new WaitForSeconds(1.5f);

                // The pool is what it opens on; the deck is where the new controls live.
                NativeDeckBuilder.ToggleMode();
                yield return new WaitForSeconds(1.5f);
                NativeDeckBuilder.SelectAt(2);
                Trace.Info("[auto] deck table: card 3 selected");

                if (Hold == "reorder") yield return CheckReordering();
            }
            else if (Hold == "rules")
            {
                Trace.Info("[auto] holding on the rules screen");
                MpMenu.OpenRules();
            }
            else if (Hold == "chat")
            {
                Trace.Info("[auto] holding with the chat box open and focused");
                MpMenu.OpenChat();
            }
            else
            {
                Trace.Info("[auto] holding on the lobby panel");
                MpMenu.OpenFromMenuCard();
            }

            Trace.Info("[auto] ==== RUN OK: holding ====");
            while (true) yield return new WaitForSeconds(5f);
        }

        /// <summary>
        /// Moves the chosen card around and checks the deck ends up in the order that
        /// implies - including that it stays chosen, and that it cannot be moved off
        /// either end. Under the in-order rule this list is the hand you will be dealt.
        /// </summary>
        private static IEnumerator CheckReordering()
        {
            string card = NativeDeckBuilder.SelectedCardName;
            Report(NativeDeckBuilder.SelectedEntry == 2, $"'{card}' starts third");

            NativeDeckBuilder.MoveSelected(-1);
            yield return new WaitForSeconds(0.4f);
            Report(NativeDeckBuilder.SelectedEntry == 1 && NativeDeckBuilder.SelectedCardName == card,
                   $"'{card}' moved earlier and stayed chosen");
            Trace.Info("[auto] deck now: " + string.Join(", ", DeckStore.Deck.ToArray()));

            NativeDeckBuilder.MoveSelected(-1);
            yield return new WaitForSeconds(0.4f);
            Report(NativeDeckBuilder.SelectedEntry == 0, $"'{card}' is now first");

            NativeDeckBuilder.MoveSelected(-1);
            yield return new WaitForSeconds(0.4f);
            Report(NativeDeckBuilder.SelectedEntry == 0, "the first card cannot move earlier");
            Report(!NativeDeckBuilder.CanMoveSelected(-1), "and the button says so");

            NativeDeckBuilder.MoveSelected(1);
            yield return new WaitForSeconds(0.4f);
            Report(NativeDeckBuilder.SelectedEntry == 1, $"'{card}' moved back to second");
            Trace.Info("[auto] deck now: " + string.Join(", ", DeckStore.Deck.ToArray()));

            // The last card is the other end of the same check.
            NativeDeckBuilder.SelectAt(DeckStore.Deck.Count - 1);
            yield return new WaitForSeconds(0.4f);
            Report(!NativeDeckBuilder.CanMoveSelected(1), "the last card cannot move later");

            // What was written is what a match would deal from.
            DeckStore.Load();
            Trace.Info("[auto] deck on disk: " + string.Join(", ", DeckStore.Deck.ToArray()));
            Report(DeckStore.Deck.Count > 0 && DeckStore.BaseName(DeckStore.Deck[1]) == card,
                   "the new order was saved");
        }

        /// <summary>
        /// Does what F8 does, which is not one thing: against a peer that can vote it
        /// commits us and waits for them, and against an older one it starts the match
        /// outright, because that is the only way an older peer ever enters one.
        /// </summary>
        private static IEnumerator Commit(MonoBehaviour plugin)
        {
            float deadline = Time.realtimeSinceStartup + 120f;

            while (!VersusMode.InMatch && !VersusMode.PendingStart
                   && Time.realtimeSinceStartup < deadline)
            {
                if (Lobby.Negotiating)
                {
                    if (!Lobby.MyReady && Lobby.CanReady) Lobby.ToggleReady();
                }
                else if (Lobby.CanReady)
                {
                    Trace.Info("[auto] peer can't vote - starting the old way");
                    VersusMode.StartAnywhere(plugin);
                }

                yield return new WaitForSeconds(1f);
            }
        }

        private static void Finish(bool ok, string why)
        {
            Trace.Info($"[auto] ==== RUN {(ok ? "OK" : "FAILED")}: {why} ====");
            if (!QuitWhenDone) return;

            Trace.Info("[auto] quitting");
            Application.Quit();
        }

        /// <summary>The card database is loaded lazily, and everything here needs it.</summary>
        private static IEnumerator WaitForCards()
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var all = ScriptableObjectLoader<CardInfo>.AllData;
                if (all != null && all.Count > 0)
                {
                    Trace.Info($"[auto] card database ready ({all.Count} cards)");
                    yield return new WaitForSeconds(2f);   // let the title screen settle
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
            Trace.Error("[auto] card database never loaded");
        }

        /// <summary>
        /// Every card name the game knows, so a test deck can be written from real names
        /// rather than guesses. Chunked: one line of 267 names is not readable.
        /// </summary>
        private static void DumpCardNames()
        {
            var all = ScriptableObjectLoader<CardInfo>.AllData;
            if (all == null) return;

            var names = new List<string>();
            foreach (CardInfo c in all)
                if (c != null) names.Add(c.name);
            names.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < names.Count; i += 25)
                Trace.Info($"[auto] cards {i}: " +
                           string.Join(", ", names.GetRange(i, Math.Min(25, names.Count - i)).ToArray()));
        }

        /// <summary>
        /// Both clients have to have locked the same rules, and the ones that were asked
        /// for. A rule that only takes on the host is a match the two of you are playing
        /// differently without either screen saying so.
        /// </summary>
        private static void CheckRules()
        {
            Rules live = MatchRules.Live;

            if (Expect != null)
            {
                Rules want = Rules.Decode(Expect);
                bool ok = live.SameAs(want);
                Report(ok, ok ? "the match locked the rules that were asked for"
                              : $"rules are {live.Encode()}, wanted {want.Encode()}");
            }

            // The ones with a visible consequence get checked against the board rather
            // than against the payload they came from.
            bool weShouldStart = Net.IsHost ? !live.GuestFirst : live.GuestFirst;
            Report(TurnOrder.IsMyTurn == weShouldStart,
                   $"the right player has the first turn (ours={TurnOrder.IsMyTurn}, rule says {weShouldStart})");
        }

        /// <summary>Both directions of the chat, which is only provable from both ends.</summary>
        private static void CheckTalking()
        {
            int heard = 0;
            foreach (Chat.Line line in Chat.All)
                if (!line.Mine && !line.System) heard++;

            Report(heard > 0, $"heard {heard} line(s) from the opponent");
        }

        /// <summary>
        /// The act's browsable pool has to contain the cards the act plainly deals in. This
        /// is the squirrel check: they were missing from it until 1.5, so it is worth a
        /// line in the report rather than a manual look through the card table.
        /// </summary>
        private static void CheckPool()
        {
            List<CardInfo> pool = DeckStore.Pool;
            Trace.Info($"[auto] {ActInfo.Name(Act)} pool holds {pool.Count} cards");

            foreach (string name in ActInfo.AlwaysOffered(Act))
            {
                bool present = pool.Exists(c => c != null && c.name == name);
                if (present) Report(true, $"'{name}' is offered in the {ActInfo.Name(Act)} pool");
                else Report(false, $"'{name}' is missing from the {ActInfo.Name(Act)} pool");
            }
        }

        /// <summary>Writes the deck the run was asked to play, so a test is repeatable.</summary>
        private static void InstallDeck()
        {
            DeckStore.BackUpOnce();

            var kept = new List<string>();
            foreach (string raw in Deck)
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;

                if (!Cards.Exists(DeckStore.BaseName(name)))
                {
                    Report(false, $"deck asks for '{name}', which is not a card");
                    continue;
                }
                kept.Add(name);
            }

            DeckStore.Deck.Clear();
            DeckStore.Deck.AddRange(kept);
            Match.Reset();
            DeckStore.Save();
            Trace.Info($"[auto] deck installed for {ActInfo.Name(Act)}: {string.Join(", ", kept.ToArray())}");
        }

        private static IEnumerator WaitUntil(Func<bool> done, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            float chirp = 0f;

            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                if (Time.realtimeSinceStartup > chirp)
                {
                    chirp = Time.realtimeSinceStartup + 5f;
                    Trace.Info($"[auto] waiting for {what}... " +
                               $"(connected={Net.Connected}, ready={Lobby.ReadyCount}/2, " +
                               $"match={VersusMode.InMatch}, pending={VersusMode.PendingStart})");
                }
                yield return new WaitForSeconds(0.25f);
            }

            if (!done()) Trace.Error($"[auto] timed out waiting for {what}");
        }

        /// <summary>Takes turns until the match ends or the turn budget runs out.</summary>
        private static IEnumerator PlayMatch(MonoBehaviour plugin)
        {
            int taken = 0;

            while (VersusMode.InMatch && taken < MaxTurns)
            {
                var tm = Singleton<TurnManager>.Instance;
                var hand = Singleton<PlayerHand>.Instance;

                if (tm == null || hand == null || !tm.IsPlayerMainPhase || hand.PlayingLocked
                    || !TurnOrder.IsMyTurn)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                yield return new WaitForSeconds(0.6f);
                if (!VersusMode.InMatch) break;

                taken++;
                Trace.Info($"[auto] turn {taken}: hand is {HandNames()}, {Scales()}");

                if (taken == 1)
                {
                    int held = Singleton<PlayerHand>.Instance.CardsInHand.Count;
                    int want = MatchRules.Live.StartingHand;

                    // Only the player who opens sees an untouched hand; the other one has
                    // already taken a draw-phase card by the time they act.
                    if (Net.IsHost ? !MatchRules.Live.GuestFirst : MatchRules.Live.GuestFirst)
                        Report(held == want, $"opening hand is {held}, rule says {want}");

                    Chat.Say("turn 1 from " + (Net.IsHost ? "the host" : "the guest"));
                    Chat.Emote(0);
                }

                yield return PlayWhatWeCan();

                if (!VersusMode.InMatch) break;
                Trace.Info($"[auto] turn {taken}: ringing the bell");
                Singleton<TurnManager>.Instance.OnCombatBellRang();

                // Don't ring twice for the same turn: wait until the phase has moved on.
                yield return WaitUntil(() => !TurnOrder.IsMyTurn || !VersusMode.InMatch, 120f,
                                       "the turn to pass to the peer");
            }

            Trace.Info($"[auto] played {taken} turn(s); match {(VersusMode.InMatch ? "still running" : "over")}" +
                       (VersusMode.LastResult == null ? "" : " - " + VersusMode.LastResult));
        }

        /// <summary>
        /// Where the scales stand, read through the same property the game asks. With the
        /// win post moved this is what proves the patch is live rather than the rule
        /// merely being agreed on.
        /// </summary>
        private static string Scales()
        {
            var life = Singleton<LifeManager>.Instance;
            if (life == null) return "no scales";
            return $"balance {life.Balance}, {life.DamageUntilPlayerWin} to win " +
                   $"(post at {MatchRules.Live.DamageToWin})";
        }

        private static string HandNames()
        {
            var hand = Singleton<PlayerHand>.Instance;
            if (hand == null || hand.CardsInHand == null || hand.CardsInHand.Count == 0) return "(empty)";

            var names = new List<string>();
            foreach (PlayableCard c in hand.CardsInHand)
                names.Add(c == null || c.Info == null ? "?" : c.Info.name);
            return string.Join(", ", names.ToArray());
        }

        /// <summary>
        /// Plays every card it can pay for without sacrificing. Blood is deliberately out of
        /// scope: paying it means driving the sacrifice picker, and a harness that has to
        /// fake its way through a UI stops testing the thing it was built to test.
        /// </summary>
        private static IEnumerator PlayWhatWeCan()
        {
            var board = Singleton<BoardManager>.Instance;
            var hand = Singleton<PlayerHand>.Instance;
            var res = Singleton<ResourcesManager>.Instance;
            if (board == null || hand == null) yield break;

            foreach (CardSlot slot in board.PlayerSlotsCopy)
            {
                if (slot == null || slot.Card != null) continue;

                PlayableCard pick = null;
                foreach (PlayableCard card in new List<PlayableCard>(hand.CardsInHand))
                {
                    if (card == null || card.Info == null) continue;
                    if (card.Info.BloodCost > 0) continue;
                    if (card.Info.GemsCost != null && card.Info.GemsCost.Count > 0) continue;
                    if (res != null && card.Info.BonesCost > res.PlayerBones) continue;
                    if (res != null && card.EnergyCost > res.PlayerEnergy) continue;
                    pick = card;
                    break;
                }

                if (pick == null) break;

                if (res != null && pick.Info.BonesCost > 0) yield return res.SpendBones(pick.Info.BonesCost);
                if (res != null && pick.EnergyCost > 0) yield return res.SpendEnergy(pick.EnergyCost);

                Trace.Info($"[auto] playing {pick.Info.name} into slot {slot.Index}");
                yield return hand.PlayCardOnSlot(pick, slot);
                yield return new WaitForSeconds(0.35f);
            }
        }
    }

    /// <summary>
    /// The 3D draw phase waits for a click on one of the two piles. Nobody is clicking, so
    /// under autopilot it takes from the deck - or the side pile once the deck is out.
    /// </summary>
    [HarmonyPatch]
    internal static class AutopilotDraw
    {
        [HarmonyPatch(typeof(CardDrawPiles3D), "ChooseDraw", new[] { typeof(int) })]
        [HarmonyPrefix]
        private static bool DrawWithoutClicking(CardDrawPiles3D __instance, ref IEnumerator __result)
        {
            if (!Autopilot.Active || !VersusMode.InMatch) return true;

            __result = Take(__instance);
            return false;
        }

        private static IEnumerator Take(CardDrawPiles3D piles)
        {
            if (piles.Deck != null && piles.Deck.CardsInDeck > 0)
            {
                Trace.Info("[auto] draw phase: taking from the deck");
                yield return piles.DrawCardFromDeck();
                yield break;
            }

            Trace.Info("[auto] draw phase: deck is out, taking from the side pile");
            yield return piles.DrawFromSidePile();
        }
    }
}
