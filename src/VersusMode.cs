using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Starts a self-contained versus match.
    ///
    /// Deliberately does NOT go through CardBattleNodeData / EncounterBuilder: that path
    /// needs a blueprint and ties the match to a map node, so a mid-match failure strands
    /// the player on the map with input locked. We build an EncounterData ourselves and
    /// call the public TurnManager.StartGame(EncounterData) overload, so a match consumes
    /// no run progress and writes nothing to the save.
    /// </summary>
    internal static class VersusMode
    {
        public static bool InMatch { get; private set; }

        /// <summary>Set when a match was requested from the menu and the scene is still loading.</summary>
        public static bool PendingStart { get; private set; }

        private const string Act1Scene = "Part1_Cabin";

        /// <summary>Human-readable reason a match can't start right now, or null if it can.</summary>
        public static string Blocker
        {
            get
            {
                if (!Net.Connected) return "no peer connected";
                if (InMatch)        return null;
                if (PendingStart)   return "loading...";
                return null;
            }
        }

        /// <summary>
        /// Entry point that works from anywhere, including the main menu. If we're not in
        /// the Act 1 scene yet, load it and start the match once its singletons exist.
        /// </summary>
        public static void StartAnywhere(MonoBehaviour host)
        {
            if (!Net.Connected) { Trace.Warn("[versus] no peer connected"); return; }
            if (InMatch || PendingStart) return;

            if (Singleton<TurnManager>.Instance != null)
            {
                Start(host);
                return;
            }

            Trace.Info($"[versus] not in gameplay scene - loading {Act1Scene}");

            // A versus match must never be able to write to the campaign save.
            SaveManager.savingDisabled = true;
            SaveManager.LoadFromFile();

            PendingStart = true;
            LoadingScreenManager.LoadScene(Act1Scene);
        }

        /// <summary>Polled once the scene has loaded; starts the match when the board is ready.</summary>
        public static void TickPendingStart(MonoBehaviour host)
        {
            if (!PendingStart || InMatch) return;

            if (Singleton<TurnManager>.Instance == null) return;
            if (Singleton<BoardManager>.Instance == null) return;
            if (Singleton<PlayerHand>.Instance == null) return;

            var flow = Singleton<GameFlowManager>.Instance;
            if (flow == null || flow.Transitioning) return;

            PendingStart = false;
            Trace.Info("[versus] scene ready - starting match");
            Start(host);
        }

        public static bool CanStart()
        {
            if (!Net.Connected) { Trace.Warn("[versus] no peer connected"); return false; }
            if (InMatch)        { Trace.Warn("[versus] already in a match"); return false; }
            if (Singleton<TurnManager>.Instance == null)
            {
                Trace.Warn("[versus] no TurnManager - must be in the Act 1 scene");
                return false;
            }
            return true;
        }

        public static void Start(MonoBehaviour host)
        {
            if (!CanStart()) return;
            host.StartCoroutine(StartSequence());
        }

        private static IEnumerator StartSequence()
        {
            InMatch = true;
            SaveManager.savingDisabled = true;   // belt and braces: no save writes during a match
            Trace.Info("[versus] starting match");

            var flow = Singleton<GameFlowManager>.Instance;
            var views = Singleton<ViewManager>.Instance;

            // A menu launch drops us into the cabin standing up, not on the map. Sit down
            // at the table first, otherwise the battle runs under a first-person view and
            // standing up reveals the map again.
            if (flow != null && flow.CurrentGameState == GameState.FirstPerson3D)
            {
                Trace.Info("[versus] sitting down at the table");
                flow.TransitionFromFirstPerson();
                yield return new WaitForSeconds(1f);
            }

            // Roll the map away if it's showing.
            var map = Singleton<GameMap>.Instance;
            if (map != null && flow != null && flow.CurrentGameState == GameState.Map)
            {
                Trace.Info("[versus] hiding map");
                views.Controller.SwitchToControlMode(ViewController.ControlMode.MapNoDeckReview);
                yield return map.HideMapSequence();
                yield return new WaitForSeconds(0.25f);
            }

            views.Controller.SwitchToControlMode(ViewController.ControlMode.CardGameDefault);
            if (flow != null) flow.CurrentGameState = GameState.CardBattle;

            // Force the camera onto the table rather than trusting whatever view the
            // scene load left us in.
            views.SwitchToView(View.Default, immediate: false, lockAfter: false);
            yield return new WaitForSeconds(0.35f);

            var encounter = new EncounterData
            {
                opponentType = Opponent.Type.Default,
                aiId = EncounterData.DEFAULT_AI_ID,
                opponentTurnPlan = new List<List<CardInfo>>(),
                startConditions = new List<EncounterData.StartCondition>(),
                Difficulty = 0,
            };

            Trace.Info("[versus] handing encounter to TurnManager");
            Singleton<TurnManager>.Instance.StartGame(encounter);
        }

        public static void End()
        {
            InMatch = false;
            Match.Reset();
            Trace.Info("[versus] match ended");
        }
    }
}
