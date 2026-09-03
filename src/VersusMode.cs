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

        /// <summary>Scene for the act being played. Set when a match or deck view starts.</summary>
        private static string ActScene => ActInfo.SceneFor(ActInfo.Current);

        /// <summary>Human-readable reason a match can't start right now, or null if it can.</summary>
        public static string Blocker
        {
            get
            {
                if (Net.HandshakeError != null) return Net.HandshakeError;
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

            if (Net.HandshakeError != null)
            {
                Trace.Warn("[versus] refusing to start: " + Net.HandshakeError);
                return;
            }

            if (Net.HandshakeError != null)
            {
                Trace.Warn("[versus] refusing to start: " + Net.HandshakeError);
                return;
            }

            if (!DeckStore.IsValid)
            {
                Trace.Warn($"[versus] deck has {DeckStore.Deck.Count} cards - needs {DeckStore.MinCards}-{DeckStore.MaxCards}");
                return;
            }

            // The act is fixed for the whole match: it decides the scene, and both clients
            // must be on the same one.
            if (tellPeer) ActInfo.Current = ActInfo.Selected;
            Trace.Info($"[versus] act for this match: {ActInfo.Name(ActInfo.Current)}");

            DeckStore.Save();   // don't lose a deck because they forgot to press Save

            if (tellPeer)
            {
                Trace.Info("[versus] telling peer to start");
                Net.Send(Protocol.StartMatch(ActInfo.Current));
            }

            if (Singleton<TurnManager>.Instance != null)
            {
                Start(host);
                return;
            }

            Trace.Info($"[versus] not in gameplay scene - loading {ActScene} for {ActInfo.Name(ActInfo.Current)}");

            // A versus match must never be able to write to the campaign save.
            SaveManager.savingDisabled = true;

            PrepareIsolatedRun();

            PendingStart = true;
            LoadingScreenManager.LoadScene(ActScene);
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
                save.currentScene = ActScene;

                // Act 3 keeps its own save data - map areas, world position, bounty. Its
                // scene is an explorable holo world, and without this it comes up holding
                // Act 1 state and tries to put the player back on the map.
                if (ActInfo.Current == MatchAct.Act3)
                {
                    if (save.part3Data == null) save.part3Data = new Part3SaveData();
                    save.part3Data.Initialize();
                    Trace.Info("[versus] initialised Part 3 save data for the match");
                }

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
        /// <summary>
        /// True when we loaded the Act 1 scene purely to host the deck card view, so
        /// closing that view should return to the title rather than stranding the player
        /// at an empty table. False if they were already in the scene themselves.
        /// </summary>
        public static bool LoadedTableForDeck { get; private set; }

        public static void LoadTableOnly()
        {
            if (Singleton<TurnManager>.Instance != null)
            {
                Trace.Info("[versus] already in the gameplay scene");
                return;
            }

            LoadedTableForDeck = true;
            Trace.Info("[versus] loading the table for deck building");
            SaveManager.savingDisabled = true;
            PrepareIsolatedRun();
            LoadingScreenManager.LoadScene(ActScene);
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
            LoadedTableForDeck = false;
            Suspended = false;
            Net.Reconnecting = true;
            SaveManager.savingDisabled = true;   // belt and braces: no save writes during a match

            // The host takes the first turn; the joiner waits. Without this both clients
            // play simultaneously and never see each other's cards until a bell rings.
            TurnOrder.BeginMatch(Net.IsHost);

            Trace.Info("[versus] starting match");

            var flow = Singleton<GameFlowManager>.Instance;
            var views = Singleton<ViewManager>.Instance;

            // Act 1's cabin needs coaxing: a menu launch drops you in standing up, and the
            // paper map has to be rolled away. Other acts don't - the base game's own
            // battle transition is just "switch control mode, start the game", and Act 3's
            // holographic map throws if you try to hide it like Act 1's.
            if (ActInfo.Current == MatchAct.Act1)
            {
                if (flow != null && flow.CurrentGameState == GameState.FirstPerson3D)
                {
                    Trace.Info("[versus] sitting down at the table");
                    flow.TransitionFromFirstPerson();
                    yield return new WaitForSeconds(1f);
                }

                var map = Singleton<GameMap>.Instance;
                if (map != null && flow != null && flow.CurrentGameState == GameState.Map)
                {
                    Trace.Info("[versus] hiding map");
                    views.Controller.SwitchToControlMode(ViewController.ControlMode.MapNoDeckReview);
                    yield return map.HideMapSequence();
                    yield return new WaitForSeconds(0.25f);
                }
            }
            else
            {
                Trace.Info($"[versus] {ActInfo.Name(ActInfo.Current)}: using the plain battle transition");

                // Part 3's scene init both hides its holo map and then transitions to the
                // map state. We block that method to stop the transition, which also skips
                // the hide - so the map stays sitting on top of the board. Do the hide
                // ourselves. HideMapImmediate is what Part 3 itself calls; the animated
                // HideMapSequence throws on a HoloGameMap.
                var holoMap = Singleton<GameMap>.Instance;
                if (holoMap != null)
                {
                    Trace.Info("[versus] hiding the holo map");
                    holoMap.HideMapImmediate();
                }
                if (views != null) views.SwitchToView(View.Default, immediate: true);

                yield return new WaitForSeconds(0.5f);
            }

            if (ActInfo.Current == MatchAct.Act1) TableProps.HidePlayerMarker();

            views.Controller.SwitchToControlMode(ViewController.ControlMode.CardGameDefault);
            if (flow != null) flow.CurrentGameState = GameState.CardBattle;

            // Forcing the camera was an Act 1 fix for a scene load leaving the wrong view.
            // Other acts position themselves via their control mode, so don't fight it.
            if (ActInfo.Current == MatchAct.Act1)
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

        /// <summary>True while a match is paused waiting for a dropped peer to return.</summary>
        public static bool Suspended { get; private set; }

        /// <summary>How long we wait before calling it: a crash-and-relaunch fits inside this.</summary>
        public const float ReconnectWindowSeconds = 120f;

        private static float _suspendedAt;

        public static float SuspendedSecondsLeft =>
            Mathf.Max(0f, ReconnectWindowSeconds - (Time.realtimeSinceStartup - _suspendedAt));

        /// <summary>
        /// Called when the peer vanishes mid-match. Turn ownership is local state that only
        /// changes on a bell or an END, neither of which can happen while disconnected - so
        /// it survives the drop untouched and only the boards need resyncing on return.
        /// </summary>
        public static void NoteDisconnected()
        {
            if (!InMatch || Suspended) return;
            Suspended = true;
            _suspendedAt = Time.realtimeSinceStartup;
            Trace.Warn("[versus] peer lost - match suspended, waiting for them to return");
        }

        public static void NoteReconnected()
        {
            if (!Suspended) return;
            Suspended = false;
            Trace.Info("[versus] peer returned - resuming match");

            // Each side re-asserts its own board so both views agree again.
            Net.Send(Protocol.Board(Sync.SnapshotPlayerSlotsPublic()));
        }

        /// <summary>Gives up on a peer that never came back.</summary>
        public static void TickSuspension(MonoBehaviour host)
        {
            if (!Suspended) return;
            if (SuspendedSecondsLeft > 0f) return;

            Trace.Warn("[versus] reconnect window expired");
            Suspended = false;
            Finish(host, playerWon: true, reason: "opponent did not return");
        }

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
            Suspended = false;
            Net.Reconnecting = false;
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

        /// <summary>
        /// Leaves the deck table and goes back to the title. Only meaningful when we
        /// brought the player here ourselves.
        /// </summary>
        public static void LeaveDeckTable()
        {
            if (!LoadedTableForDeck || InMatch) return;

            LoadedTableForDeck = false;
            Trace.Info("[versus] leaving the deck table - back to the title");
            RestoreCampaignRun();
            TableProps.RestorePlayerMarker();
            MenuController.ReturnToStartScreen();   // also clears savingDisabled
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
            Suspended = false;
            Net.Reconnecting = false;
            PendingStart = false;
            Match.Reset();
            RestoreCampaignRun();
            TableProps.RestorePlayerMarker();
            MenuController.ReturnToStartScreen();
        }
    }
}
