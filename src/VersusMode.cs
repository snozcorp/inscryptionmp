using System;
using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using GBC;
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

        /// <summary>Scene for the act being played.</summary>
        private static string ActScene => ActInfo.SceneFor(ActInfo.Current);

        /// <summary>
        /// The scene the deck browser runs in - always Act 1's cabin, whichever act's deck
        /// is being edited. The browser is built on SelectableCardArray, which only exists
        /// on the 3D table; other acts' cards are shown there via CardRenderFallbacks.
        /// </summary>
        private static string DeckTableScene => ActInfo.SceneFor(MatchAct.Act1);

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
                Notice.Bad("Can't start: " + Net.HandshakeError);
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

            Notice.Busy($"Starting {ActInfo.Name(ActInfo.Current)} match...");
            Trace.Info($"[versus] not in gameplay scene - loading {ActScene} for {ActInfo.Name(ActInfo.Current)}");

            // A versus match must never be able to write to the campaign save.
            SaveManager.savingDisabled = true;

            PrepareIsolatedRun(ActInfo.Current);

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
        private static void PrepareIsolatedRun(MatchAct act)
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
                if (!_stashed)
                {
                    _stashedRun = save.currentRun;
                    _stashedScene = save.currentScene;
                    _stashedGbc = save.gbcData;
                    _stashedPart3 = save.part3Data;
                    _stashed = true;
                }
                else
                {
                    Trace.Info("[versus] campaign state already stashed - keeping it");
                }

                save.ResetPart1Run();          // fresh run + starter deck, in memory only
                save.currentScene = ActInfo.SceneFor(act);

                // A fresh run is handed the campaign's starting consumables, and none of
                // them belong in a match - see NoItems for what they do to one.
                NoItems.StripFrom(save.currentRun);

                // Act 3 keeps its own save data - map areas, world position, bounty. Its
                // scene is an explorable holo world, and without this it comes up holding
                // Act 1 state and tries to put the player back on the map.
                if (act == MatchAct.Act3)
                {
                    save.part3Data = new Part3SaveData();
                    save.part3Data.Initialize();
                    Trace.Info("[versus] gave the match its own Part 3 save data");
                }
                else if (act == MatchAct.Act2)
                {
                    save.gbcData = new GBC.SaveData();
                    save.gbcData.Initialize();
                    Trace.Info("[versus] gave the match its own GBC save data");
                }

                // A synthetic run starts with the intro unplayed, which triggers Leshy's
                // tutorial patter. There's no run here to introduce.
                if (save.currentRun != null) save.currentRun.runIntroCompleted = true;
                Opponent.debugSkipIntro = true;

                Trace.Info($"[versus] synthesised an isolated run for {ActInfo.Name(act)}");
            }
            catch (Exception e)
            {
                Trace.Error($"[versus] could not prepare run state: {e.Message}");
            }
        }

        /// <summary>
        /// The player's own save state, held while a match or the deck table borrows the
        /// save file. A flag rather than a null check on the run: a player who has never
        /// started a run legitimately has none, and testing for null left them with our
        /// synthetic one.
        /// </summary>
        private static bool _stashed;
        private static RunState _stashedRun;
        private static string _stashedScene;
        private static GBC.SaveData _stashedGbc;
        private static Part3SaveData _stashedPart3;

        /// <summary>Puts the player's own run back after a match.</summary>
        private static void RestoreCampaignRun()
        {
            try
            {
                if (!_stashed) return;
                SaveFile save = SaveManager.SaveFile;
                if (save != null)
                {
                    save.currentRun = _stashedRun;
                    if (_stashedScene != null) save.currentScene = _stashedScene;

                    // Acts 2 and 3 keep their progress in their own save data, which the
                    // match replaced wholesale. Without putting these back, a later save
                    // would write our throwaway state over the player's campaign.
                    save.gbcData = _stashedGbc;
                    save.part3Data = _stashedPart3;
                    Trace.Info("[versus] restored the campaign run and act save data");
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
                _stashedGbc = null;
                _stashedPart3 = null;
                _stashed = false;
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

        /// <summary>
        /// True whenever the mod is driving the scene rather than the campaign - a match,
        /// a match about to start, or the deck table. Used by patches that should only
        /// change the game's behaviour for versus play.
        /// </summary>
        public static bool VersusContext => InMatch || PendingStart || LoadedTableForDeck;

        public static void LoadTableOnly()
        {
            if (Singleton<TurnManager>.Instance != null)
            {
                Trace.Info("[versus] already in the gameplay scene");
                return;
            }

            LoadedTableForDeck = true;
            Notice.Busy("Opening the card table...");
            Trace.Info($"[versus] loading {DeckTableScene} to edit the {ActInfo.Name(ActInfo.Selected)} deck");
            SaveManager.savingDisabled = true;
            PrepareIsolatedRun(MatchAct.Act1);
            LoadingScreenManager.LoadScene(DeckTableScene);
        }

        /// <summary>Polled once the scene has loaded; starts the match when the board is ready.</summary>
        public static void TickPendingStart(MonoBehaviour host)
        {
            if (!PendingStart || InMatch) return;

            if (Singleton<TurnManager>.Instance == null) return;
            if (Singleton<BoardManager>.Instance == null) return;
            if (Singleton<PlayerHand>.Instance == null) return;

            // Only wait on the flow manager in acts that actually have one - GBC doesn't,
            // and gating on it there meant the match never started at all.
            if (ActInfo.HasFlowManager(ActInfo.Current))
            {
                var flow = Singleton<GameFlowManager>.Instance;
                if (flow == null || flow.Transitioning) return;
            }

            PendingStart = false;
            Notice.ClearIfBusy();
            Trace.Info("[versus] scene ready - starting match");
            Start(host);

            // Don't leave someone parked at an empty table if the match couldn't begin
            // after all - the peer may have dropped while the scene was loading.
            if (!InMatch)
            {
                Trace.Warn("[versus] match did not start after loading - returning to the menu");
                Notice.Bad("Couldn't start the match.");
                RestoreCampaignRun();
                TableProps.RestorePlayerMarker();
                MenuController.ReturnToStartScreen();
            }
        }

        public static bool CanStart()
        {
            if (!Net.Connected) { Notice.Bad("No opponent connected yet."); return false; }
            if (InMatch)        { Notice.Say("You're already in a match."); return false; }


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

            // Start from an empty queue. Anything still buffered belongs to the match that
            // just finished, and applying it here would materialise phantom cards.
            Net.FlushInbox();

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

            // GBC has no ViewManager - it's a fixed 2D camera - so there is no control
            // mode to switch and nothing to point at the table.
            if (views != null)
                views.Controller.SwitchToControlMode(ViewController.ControlMode.CardGameDefault);
            if (flow != null) flow.CurrentGameState = GameState.CardBattle;

            if (ActInfo.Current == MatchAct.Act2) DressGbcTable();

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

        /// <summary>
        /// Applies the parts of the GBC battle setup that normally come from the NPC you
        /// walked into. Without a theme the board keeps its unset placeholder sprites, and
        /// the cursor stays hidden because nothing ever unhid it.
        /// </summary>
        private static void DressGbcTable()
        {
            try
            {
                var setter = Singleton<PixelBoardSpriteSetter>.Instance;
                if (setter != null)
                {
                    PixelBoardSpriteSetter.BoardTheme theme = ThemeForDeck();
                    setter.SetSpritesForTheme(theme);
                    Trace.Info($"[versus] dressed the GBC board as {theme}");
                }
                else
                {
                    Trace.Warn("[versus] no PixelBoardSpriteSetter - board keeps placeholder art");
                }

                PauseMenu.pausingDisabled = false;

                var cursor = Singleton<InteractionCursor>.Instance;
                if (cursor != null) cursor.SetHidden(hidden: false);
            }
            catch (Exception e)
            {
                Trace.Error($"[versus] could not dress the GBC table: {e.Message}");
            }
        }

        /// <summary>The temple the player's deck leans on most.</summary>
        private static PixelBoardSpriteSetter.BoardTheme ThemeForDeck()
        {
            var counts = new Dictionary<CardTemple, int>();
            foreach (string name in DeckStore.Deck)
            {
                CardInfo info = CardLoader.GetCardByName(name);
                if (info == null) continue;
                counts.TryGetValue(info.temple, out int n);
                counts[info.temple] = n + 1;
            }

            CardTemple best = CardTemple.Nature;
            int bestCount = -1;
            foreach (var kv in counts)
                if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }

            return ActInfo.ThemeForTemple(best);
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
            if (playerWon) Notice.Good("You won.");
            else Notice.Say("You lost.");
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

            // Tell them before we go. The socket stays open when we leave, so nothing ever
            // reports us as disconnected - without this the peer waits for a turn that is
            // never coming, and not even the reconnect window rescues them because nothing
            // dropped. Walking out is conceding.
            if (InMatch) Net.Send(Protocol.Won);   // they won; we left

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
