using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Deck building using the game's own 3D card view.
    ///
    /// Inscryption has no general UI toolkit, but it does have DeckReviewSequencer, which
    /// lays real cards out on the table and lets you pick one. Reusing that gives us the
    /// game's art, hover, zoom and inspection for free instead of an IMGUI list.
    ///
    /// Two modes: browsing the pool adds the card you pick, browsing your deck removes it.
    /// </summary>
    internal static class NativeDeckBuilder
    {
        public static bool IsOpen { get; private set; }
        public static bool PoolMode { get; private set; }
        public static string LastError { get; private set; }

        /// <summary>
        /// The array crushes everything it is given onto the table, so 57 cards becomes an
        /// unreadable wall. Show a page at a time instead.
        /// </summary>
        public const int PageSize = 10;

        public static int Page { get; private set; }
        private static bool _refresh;

        public static int PageCount
        {
            get
            {
                int n = SourceCount();
                return n <= 0 ? 1 : (n + PageSize - 1) / PageSize;
            }
        }

        public static void NextPage()
        {
            Page = (Page + 1) % PageCount;
            _refresh = true;
        }

        public static void PrevPage()
        {
            Page = (Page - 1 + PageCount) % PageCount;
            _refresh = true;
        }

        public static void ToggleMode()
        {
            PoolMode = !PoolMode;
            Page = 0;
            _refresh = true;
        }

        private static int SourceCount()
        {
            return PoolMode ? DeckStore.Pool.Count : DeckStore.Deck.Count;
        }

        /// <summary>Only usable inside the Act 1 scene, where the review table exists.</summary>
        public static bool Available => Singleton<DeckReviewSequencer>.Instance != null;

        private static SelectableCardArray Array
        {
            get
            {
                var seq = Singleton<DeckReviewSequencer>.Instance;
                if (seq == null) return null;
                var field = AccessTools.Field(typeof(DeckReviewSequencer), "cardArray");
                return field?.GetValue(seq) as SelectableCardArray;
            }
        }

        /// <summary>Set while the Act 1 scene loads so we can open as soon as it's ready.</summary>
        public static bool PendingOpen { get; set; }

        /// <summary>Opens the card view the moment the review table exists after a scene load.</summary>
        public static void TickPendingOpen(MonoBehaviour host)
        {
            if (!PendingOpen || IsOpen) return;
            if (!Available) return;

            var flow = Singleton<GameFlowManager>.Instance;
            if (flow == null || flow.Transitioning) return;

            PendingOpen = false;
            Trace.Info("[deckui] table ready - opening card view");
            Open(host, poolMode: true);
        }

        public static void Open(MonoBehaviour host, bool poolMode)
        {
            if (IsOpen) return;
            if (VersusMode.InMatch)
            {
                LastError = "finish the match before editing your deck";
                Trace.Warn("[deckui] " + LastError);
                return;
            }
            if (!Available)
            {
                LastError = "card view needs the Act 1 table - use Load Table first";
                Trace.Warn("[deckui] " + LastError);
                return;
            }
            if (Array == null)
            {
                LastError = "could not reach the card array";
                Trace.Error("[deckui] " + LastError);
                return;
            }

            LastError = null;
            PoolMode = poolMode;
            Page = 0;
            _refresh = false;
            host.StartCoroutine(Loop(host));
        }

        public static void Close() => IsOpen = false;

        private static IEnumerator Loop(MonoBehaviour host)
        {
            IsOpen = true;
            Trace.Info($"[deckui] opening card view ({(PoolMode ? "pool" : "deck")})");

            var views = Singleton<ViewManager>.Instance;
            var flow = Singleton<GameFlowManager>.Instance;
            ViewLockState prevLock = ViewLockState.Unlocked;

            var map = Singleton<GameMap>.Instance;
            if (map != null && flow != null && flow.CurrentGameState == GameState.Map)
            {
                Trace.Info("[deckui] hiding map");
                views.Controller.SwitchToControlMode(ViewController.ControlMode.MapNoDeckReview);
                yield return map.HideMapSequence();
                yield return new WaitForSeconds(0.2f);
            }

            TableProps.HidePlayerMarker();

            if (views != null)
            {
                prevLock = views.Controller.LockState;
                views.SwitchToView(View.MapDeckReview);
                views.Controller.LockState = ViewLockState.Locked;
            }

            yield return new WaitForSeconds(0.3f);

            while (IsOpen)
            {
                _refresh = false;
                if (Page >= PageCount) Page = 0;

                List<CardInfo> cards = BuildList();
                if (cards.Count == 0)
                {
                    // An empty deck is normal, not an error - wait for a mode switch.
                    yield return new WaitUntil(() => _refresh || !IsOpen);
                    continue;
                }

                SelectableCard picked = null;
                // SelectCardFrom mutates the list it is given, so hand it a copy.
                yield return Array.SelectCardFrom(
                    new List<CardInfo>(cards),
                    null,
                    c => picked = c,
                    () => !IsOpen || _refresh);

                if (!IsOpen) break;
                if (_refresh) continue;         // page or mode changed
                if (picked == null) break;      // cancelled

                string id = picked.Info != null ? picked.Info.name : null;
                if (string.IsNullOrEmpty(id)) continue;

                if (PoolMode)
                {
                    DeckStore.Add(id);
                    Trace.Info($"[deckui] added {id} ({DeckStore.Deck.Count} cards)");
                }
                else
                {
                    DeckStore.Remove(id);
                    Trace.Info($"[deckui] removed {id} ({DeckStore.Deck.Count} cards)");
                }

                DeckStore.Save();

                // SelectCardFrom drops the picked card from its cleanup list because the
                // campaign animates it into your deck. Nothing else destroys it, so every
                // click otherwise leaves a card stranded on the table.
                if (picked != null) Object.Destroy(picked.gameObject);

                yield return new WaitForSeconds(0.15f);
            }

            IsOpen = false;
            if (views != null)
            {
                views.SwitchToView(View.Default);
                views.Controller.LockState = prevLock;
            }
            Trace.Info("[deckui] closed card view");

            // If we loaded this scene purely to show the cards, don't strand the player at
            // an empty table with no map and no menu.
            if (VersusMode.LoadedTableForDeck && !VersusMode.InMatch)
            {
                yield return new WaitForSeconds(0.35f);
                VersusMode.LeaveDeckTable();
            }
            else
            {
                TableProps.RestorePlayerMarker();
            }
        }

        /// <summary>The current page of whichever list we're browsing.</summary>
        private static List<CardInfo> BuildList()
        {
            var all = new List<CardInfo>();

            if (PoolMode)
            {
                all.AddRange(DeckStore.Pool);
            }
            else
            {
                foreach (string name in DeckStore.Deck)
                {
                    CardInfo info = CardLoader.GetCardByName(name);
                    if (info != null) all.Add(info);
                }
            }

            int start = Page * PageSize;
            if (start >= all.Count) { Page = 0; start = 0; }
            int count = Mathf.Min(PageSize, all.Count - start);
            return all.GetRange(start, count);
        }
    }
}
