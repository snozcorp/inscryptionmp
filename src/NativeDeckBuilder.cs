using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>Deck building using the game's own 3D card view.</summary>
    internal static class NativeDeckBuilder
    {
        public static bool IsOpen { get; private set; }
        public static bool PoolMode { get; private set; }

        /// <summary>
        /// Which deck entry is having its sigils chosen, or -1 when browsing normally.
        /// </summary>
        public static int SigilTarget { get; private set; } = -1;

        public static bool SigilMode => SigilTarget >= 0;

        /// <summary>The deck card you have clicked, or -1 for none.</summary>
        public static int SelectedEntry { get; private set; } = -1;

        public static bool HasSelection => SelectedEntry >= 0 && SelectedEntry < DeckStore.Deck.Count;

        public static string SelectedCardName =>
            HasSelection ? DeckStore.BaseName(DeckStore.Deck[SelectedEntry]) : "";

        public static void ClearSelection()
        {
            SelectedEntry = -1;
            _refresh = true;
        }

        /// <summary>Opens the sigil pages for the selected card.</summary>
        public static void EditSelectedSigils()
        {
            if (!HasSelection) return;
            EnterSigilMode(SelectedEntry);
        }

        /// <summary>Takes the selected card out of the deck.</summary>
        public static void DeleteSelected()
        {
            if (!HasSelection) return;

            string card = SelectedCardName;
            DeckStore.RemoveAt(SelectedEntry);
            Notice.Say($"Removed {card} ({DeckStore.Deck.Count} cards).");
            Trace.Info($"[deckui] removed {card} ({DeckStore.Deck.Count} cards)");
            ClearSelection();
        }

        /// <summary>The sigil each card on the current page stands for.</summary>
        private static readonly List<Ability> PageSigils = new List<Ability>();

        /// <summary>The card whose sigils are being chosen, for the control bar.</summary>
        public static string SigilCardName =>
            SigilMode && SigilTarget < DeckStore.Deck.Count
                ? DeckStore.BaseName(DeckStore.Deck[SigilTarget])
                : "";

        public static void EnterSigilMode(int deckIndex)
        {
            SigilTarget = deckIndex;
            Page = 0;
            _refresh = true;
        }

        public static void ExitSigilMode()
        {
            SigilTarget = -1;
            SelectedEntry = -1;
            Page = 0;
            _refresh = true;
        }
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
            SelectedEntry = -1;
            Page = 0;
            _refresh = true;
        }

        private static int SourceCount()
        {
            if (SigilMode) return SigilPool.For(ActInfo.Selected).Count;
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
            Notice.ClearIfBusy();
            Trace.Info("[deckui] table ready - opening card view");
            Open(host, poolMode: true);
        }

        public static void Open(MonoBehaviour host, bool poolMode)
        {
            if (IsOpen) return;
            if (VersusMode.InMatch)
            {
                LastError = "finish the match before editing your deck";
                Notice.Bad("Finish the match before editing your deck.");
                return;
            }
            if (!Available)
            {
                LastError = "card view needs the Act 1 table - use Load Table first";
                Notice.Bad("The card table isn't loaded yet.");
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

                if (SigilMode)
                {
                    // Which sigil the chosen card stood for. Matched by reference: every
                    // card on this page is the same card, so the name tells us nothing.
                    int slot = cards.FindIndex(c => ReferenceEquals(c, picked.Info));
                    if (slot >= 0 && slot < PageSigils.Count) ToggleSigil(PageSigils[slot]);

                    yield return CleanUpPicked(picked);
                    continue;
                }

                string id = picked.Info != null ? picked.Info.name : null;
                if (string.IsNullOrEmpty(id)) continue;

                if (PoolMode)
                {
                    DeckStore.Add(id);
                    Trace.Info($"[deckui] added {id} ({DeckStore.Deck.Count} cards)");
                }
                else
                {
                    // A click selects; the bar says what can be done with it.
                    int index = DeckStore.Deck.FindIndex(e => DeckStore.BaseName(e) == id);
                    if (index >= 0)
                    {
                        SelectedEntry = index;
                        Notice.Say($"{id} selected - add sigils or delete it.");
                        yield return CleanUpPicked(picked);
                        continue;
                    }
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
        /// <summary>Destroys the card that was clicked.</summary>
        private static IEnumerator CleanUpPicked(SelectableCard picked)
        {
            if (picked != null) Object.Destroy(picked.gameObject);
            yield return new WaitForSeconds(0.15f);
        }

        /// <summary>Takes the card being edited out of the deck entirely.</summary>
        public static void RemoveTargetCard()
        {
            if (SigilTarget < 0 || SigilTarget >= DeckStore.Deck.Count) return;

            string card = DeckStore.BaseName(DeckStore.Deck[SigilTarget]);
            DeckStore.RemoveAt(SigilTarget);
            Notice.Say($"Removed {card} ({DeckStore.Deck.Count} cards).");
            Trace.Info($"[deckui] removed {card} ({DeckStore.Deck.Count} cards)");

            ExitSigilMode();
        }

        /// <summary>Adds or removes one sigil on the card being edited.</summary>
        private static void ToggleSigil(Ability ability)
        {
            if (SigilTarget < 0 || SigilTarget >= DeckStore.Deck.Count) return;

            string entry = DeckStore.Deck[SigilTarget];
            string card = DeckStore.BaseName(entry);
            var chosen = new List<string>(DeckStore.SigilsOf(entry));
            string id = ability.ToString();

            if (chosen.Contains(id))
            {
                chosen.Remove(id);
                Notice.Say($"{card}: removed {SigilPool.DisplayName(ability)}");
            }
            else if (chosen.Count >= DeckStore.MaxAddedSigils)
            {
                // Two is what the game can draw; a third would be invisible in Act 2.
                Notice.Bad($"{card} already has {DeckStore.MaxAddedSigils} sigils. Remove one first.");
                return;
            }
            else
            {
                chosen.Add(id);
                Notice.Good($"{card}: added {SigilPool.DisplayName(ability)}");
            }

            DeckStore.SetSigils(SigilTarget, chosen);
            _refresh = true;   // redraw the page so the change shows on the cards
        }

        private static List<CardInfo> BuildList()
        {
            PageSigils.Clear();

            if (SigilMode) return BuildSigilPage();

            var all = new List<CardInfo>();

            if (PoolMode)
            {
                all.AddRange(DeckStore.Pool);
            }
            else
            {
                foreach (string entry in DeckStore.Deck)
                {
                    // Entries can carry sigils now, so the card is the part before the
                    // colon - resolving the whole entry finds nothing and the card would
                    // quietly disappear from your own deck view.
                    CardInfo info = CardLoader.GetCardByName(DeckStore.BaseName(entry));
                    if (info == null) continue;

                    string[] sigils = DeckStore.SigilsOf(entry);
                    all.Add(sigils.Length > 0 ? Preview(info, sigils) : info);
                }
            }

            int start = Page * PageSize;
            if (start >= all.Count) { Page = 0; start = 0; }
            int count = Mathf.Min(PageSize, all.Count - start);
            return all.GetRange(start, count);
        }

        /// <summary>The card being edited, once per sigil it could take.</summary>
        private static List<CardInfo> BuildSigilPage()
        {
            var cards = new List<CardInfo>();
            if (SigilTarget < 0 || SigilTarget >= DeckStore.Deck.Count) return cards;

            string entry = DeckStore.Deck[SigilTarget];
            CardInfo baseInfo = CardLoader.GetCardByName(DeckStore.BaseName(entry));
            if (baseInfo == null) return cards;

            var chosen = new List<string>(DeckStore.SigilsOf(entry));
            List<Ability> pool = SigilPool.For(ActInfo.Selected);

            int start = Page * PageSize;
            if (start >= pool.Count) { Page = 0; start = 0; }
            int count = Mathf.Min(PageSize, pool.Count - start);

            for (int i = start; i < start + count; i++)
            {
                Ability ability = pool[i];

                // Already-chosen sigils are shown as they are on the card, so the page
                // reads as "this is what it looks like now" rather than a preview of
                // adding it a second time.
                var withAll = new List<string>(chosen);
                if (!withAll.Contains(ability.ToString())) withAll.Add(ability.ToString());

                cards.Add(Preview(baseInfo, withAll.ToArray()));
                PageSigils.Add(ability);
            }

            return cards;
        }

        /// <summary>
        /// A throwaway copy of a card carrying some sigils, purely to look at.
        /// </summary>
        private static CardInfo Preview(CardInfo info, string[] sigils)
        {
            var copy = info.Clone() as CardInfo;
            if (copy == null) return info;

            var mod = new CardModificationInfo { singletonId = "inscryptionmp-preview" };
            foreach (string name in sigils)
            {
                if (!System.Enum.IsDefined(typeof(Ability), name)) continue;
                var ability = (Ability)System.Enum.Parse(typeof(Ability), name);
                if (!copy.Abilities.Contains(ability)) mod.abilities.Add(ability);
            }

            if (mod.abilities.Count > 0) copy.Mods.Add(mod);
            return copy;
        }
    }
}
