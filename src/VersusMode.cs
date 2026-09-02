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
            Trace.Info("[versus] starting match");

            var flow = Singleton<GameFlowManager>.Instance;
            var views = Singleton<ViewManager>.Instance;

            // Get off the map if we're on it, so the table is actually visible.
            var map = Singleton<GameMap>.Instance;
            if (map != null && flow != null && flow.CurrentGameState == GameState.Map)
            {
                views.Controller.SwitchToControlMode(ViewController.ControlMode.MapNoDeckReview);
                yield return map.HideMapSequence();
                yield return new WaitForSeconds(0.25f);
            }

            views.Controller.SwitchToControlMode(ViewController.ControlMode.CardGameDefault);
            if (flow != null) flow.CurrentGameState = GameState.CardBattle;

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
