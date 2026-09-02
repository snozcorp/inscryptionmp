using System;
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
                if (!DeckStore.IsValid) return $"deck needs {DeckStore.MinCards}-{DeckStore.MaxCards} cards";
                return null;
            }
        }

        /// <summary>
        /// Entry point that works from anywhere, including the main menu. If we're not in
        /// the Act 1 scene yet, load it and start the match once its singletons exist.
        /// </summary>
        public static void StartAnywhere(MonoBehaviour host)
        {
            StartAnywhere(host, tellPeer: true);
        }

        /// <summary>
        /// Both clients run their own battle, so a match only works if both start one.
        /// A locally initiated start tells the peer to start too; a peer-initiated start
        /// must not echo back.
        /// </summary>
        public static void StartAnywhere(MonoBehaviour host, bool tellPeer)
        {
            if (!Net.Connected) { Trace.Warn("[versus] no peer connected"); return; }
            if (InMatch || PendingStart) return;

            if (!DeckStore.IsValid)
            {
                Trace.Warn($"[versus] deck has {DeckStore.Deck.Count} cards - needs {DeckStore.MinCards}-{DeckStore.MaxCards}");
                return;
            }

            DeckStore.Save();   // don't lose a deck because they forgot to press Save

            if (tellPeer)
            {
                Trace.Info("[versus] telling peer to start");
                Net.Send(Protocol.StartMatch);
            }

            if (Singleton<TurnManager>.Instance != null)
            {
                Start(host);
                return;
            }

            Trace.Info($"[versus] not in gameplay scene - loading {Act1Scene}");

            // A versus match must never be able to write to the campaign save.
            SaveManager.savingDisabled = true;

            PrepareIsolatedRun();

            PendingStart = true;
            LoadingScreenManager.LoadScene(Act1Scene);
        }

        /// <summary>
        /// Gives the match a valid, self-contained Act 1 run to sit inside.
        ///
        /// Part1_Cabin expects a run to exist - a player with no save, or one who has
        /// never started Act 1, previously got a broken scene. We synthesise a fresh run
        /// instead of borrowing theirs, and stash whatever was there so the campaign is
        /// untouched. Saving is disabled throughout, so none of this reaches disk.
        /// </summary>
        private static void PrepareIsolatedRun()
        {
            try
            {
                if (SaveManager.SaveFile == null)
                {
                    Trace.Info("[versus] no save file - creating one");
                    SaveManager.CreateNewSaveFile();
                }

                SaveFile save = SaveManager.SaveFile;

                // Only ever stash the *player's* run. This runs for both a match and the
                // deck card view, and the view doesn't restore - so stashing again would
                // capture the synthetic run we just installed and later "restore" that
                // over their real one.
                if (_stashedRun == null)
                {
                    _stashedRun = save.currentRun;
                    _stashedScene = save.currentScene;
                }
                else
                {
                    Trace.Info("[versus] campaign run already stashed - keeping it");
                }

                save.ResetPart1Run();          // fresh run + starter deck, in memory only
                save.currentScene = Act1Scene;

                // A synthetic run starts with the intro unplayed, which triggers Leshy's
                // tutorial patter. There's no run here to introduce.
                if (save.currentRun != null) save.currentRun.runIntroCompleted = true;
                Opponent.debugSkipIntro = true;

                Trace.Info("[versus] synthesised an isolated Act 1 run for the match");
            }
            catch (Exception e)
            {
                Trace.Error($"[versus] could not prepare run state: {e.Message}");
            }
        }

        private static RunState _stashedRun;
        private static string _stashedScene;

        /// <summary>Puts the player's own run back after a match.</summary>
        private static void RestoreCampaignRun()
        {
            try
            {
                if (_stashedRun == null) return;
                SaveFile save = SaveManager.SaveFile;
                if (save != null)
                {
                    save.currentRun = _stashedRun;
                    if (_stashedScene != null) save.currentScene = _stashedScene;
                    Trace.Info("[versus] restored the campaign run");
                }
            }
            catch (Exception e)
            {
                Trace.Error($"[versus] could not restore run state: {e.Message}");
            }
            finally
            {
                _stashedRun = null;
                _stashedScene = null;
            }
        }

        /// <summary>
        /// Loads the Act 1 table without starting a match, so the native card view has
        /// somewhere to lay cards out. Uses the same isolated run as a match, so the
        /// player's campaign is untouched.
        /// </summary>
        public static void LoadTableOnly()
        {
            if (Singleton<TurnManager>.Instance != null)
            {
                Trace.Info("[versus] already in the gameplay scene");
                return;
            }

            Trace.Info("[versus] loading the table for deck building");
            SaveManager.savingDisabled = true;
            PrepareIsolatedRun();
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

            // The host takes the first turn; the joiner waits. Without this both clients
            // play simultaneously and never see each other's cards until a bell rings.
            TurnOrder.BeginMatch(Net.IsHost);

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

            TableProps.HidePlayerMarker();

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

        /// <summary>Last match result, shown in the overlay until the next match.</summary>
        public static string LastResult { get; private set; }

        /// <summary>
        /// Ends the match and returns to the main menu. The campaign save was locked for
        /// the whole match, so there is nothing to roll back.
        /// </summary>
        public static void Finish(MonoBehaviour host, bool playerWon, string reason)
        {
            if (!InMatch) return;

            LastResult = playerWon ? "you won" : "you lost";
            Trace.Info($"[versus] match over - {LastResult} ({reason})");

            Net.Send(playerWon ? Protocol.Lost : Protocol.Won);   // their result is our inverse

            InMatch = false;
            PendingStart = false;
            Match.Reset();
            RestoreCampaignRun();
            TableProps.RestorePlayerMarker();

            var runner = Plugin.Runner;
            if (runner != null) runner.StartCoroutine(ReturnToMenu());
            else MenuController.ReturnToStartScreen();
        }

        private static IEnumerator ReturnToMenu()
        {
            yield return new WaitForSeconds(1.5f);
            Trace.Info("[versus] returning to main menu");
            MenuController.ReturnToStartScreen();   // this also clears savingDisabled
        }

        /// <summary>Escape hatch: always available, always gets the player out.</summary>
        public static void Abort(MonoBehaviour host)
        {
            if (!InMatch && !PendingStart)
            {
                Trace.Info("[versus] abort requested but no match is running");
                return;
            }
            Trace.Warn("[versus] match aborted by player");
            InMatch = false;
            PendingStart = false;
            Match.Reset();
            RestoreCampaignRun();
            TableProps.RestorePlayerMarker();
            MenuController.ReturnToStartScreen();
        }
    }
}
