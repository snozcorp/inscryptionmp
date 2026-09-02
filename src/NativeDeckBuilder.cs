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

        public static void Open(MonoBehaviour host, bool poolMode)
        {
            if (IsOpen) return;
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
            host.StartCoroutine(Loop(host));
        }

        public static void Close() => IsOpen = false;

        private static IEnumerator Loop(MonoBehaviour host)
        {
            IsOpen = true;
            Trace.Info($"[deckui] opening card view ({(PoolMode ? "pool" : "deck")})");

            var views = Singleton<ViewManager>.Instance;
            ViewLockState prevLock = ViewLockState.Unlocked;
            if (views != null)
            {
                prevLock = views.Controller.LockState;
                views.SwitchToView(View.MapDeckReview);
                views.Controller.LockState = ViewLockState.Locked;
            }

            yield return new WaitForSeconds(0.3f);

            while (IsOpen)
            {
                List<CardInfo> cards = BuildList();
                if (cards.Count == 0)
                {
                    Trace.Warn("[deckui] nothing to show");
                    break;
                }

                SelectableCard picked = null;
                // SelectCardFrom mutates the list it is given, so hand it a copy.
                yield return Array.SelectCardFrom(
                    new List<CardInfo>(cards),
                    null,
                    c => picked = c,
                    () => !IsOpen);

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
                yield return new WaitForSeconds(0.15f);
            }

            IsOpen = false;
            if (views != null)
            {
                views.SwitchToView(View.Default);
                views.Controller.LockState = prevLock;
            }
            Trace.Info("[deckui] closed card view");
        }

        private static List<CardInfo> BuildList()
        {
            if (PoolMode) return new List<CardInfo>(DeckStore.Pool);

            var list = new List<CardInfo>();
            foreach (string name in DeckStore.Deck)
            {
                CardInfo info = CardLoader.GetCardByName(name);
                if (info != null) list.Add(info);
            }
            return list;
        }
    }
}
