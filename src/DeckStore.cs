using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// The player's versus deck: a list of card names persisted beside the BepInEx config.
    ///
    /// Each player brings their own deck, exactly like a normal card game - decks are not
    /// synchronised. The peer only ever needs to resolve a card *name*, which
    /// CardLoader.GetCardByName already does, so no extra protocol is required.
    /// </summary>
    public static class DeckStore
    {
        public const int MinCards = 6;
        public const int MaxCards = 20;

        /// <summary>Used when the player hasn't built a deck yet.</summary>
        private static readonly string[] Starter =
        {
            "Stoat", "Stoat", "Bullfrog", "Bullfrog",
            "Wolf", "Wolf", "Adder",
            "Squirrel", "Squirrel", "Squirrel",
        };

        private static List<string> _deck;
        private static List<CardInfo> _pool;

        private static string Path
        {
            get
            {
                string dir = BepInEx.Paths.ConfigPath ?? ".";
                return System.IO.Path.Combine(dir, "inscryptionmp-deck.txt");
            }
        }

        public static List<string> Deck
        {
            get
            {
                if (_deck == null) Load();
                return _deck;
            }
        }

        public static bool IsValid => Deck.Count >= MinCards && Deck.Count <= MaxCards;

        public static void Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    _deck = File.ReadAllLines(Path)
                                .Select(l => l.Trim())
                                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                                .ToList();
                    Trace.Info($"[deck] loaded {_deck.Count} cards from disk");
                    PruneUnrenderable();
                }
                else
                {
                    _deck = new List<string>(Starter);
                    Trace.Info("[deck] no saved deck - using starter");
                }
            }
            catch (Exception e)
            {
                Trace.Error($"[deck] load failed: {e.Message}");
                _deck = new List<string>(Starter);
            }
        }

        /// <summary>
        /// Drops saved cards that can no longer be drawn here. A deck built while the pool
        /// was too permissive would otherwise put blank cards on the table mid-match.
        /// </summary>
        private static void PruneUnrenderable()
        {
            if (_deck == null) return;

            var kept = new List<string>();
            var dropped = new List<string>();

            foreach (string name in _deck)
            {
                CardInfo info = CardLoader.GetCardByName(name);
                if (info != null && CanRenderOnAct1Table(info)) kept.Add(name);
                else dropped.Add(name);
            }

            if (dropped.Count == 0) return;

            _deck = kept;
            Trace.Warn($"[deck] dropped {dropped.Count} card(s) that can't render here: {string.Join(", ", dropped)}");
            Save();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(Path, Deck.ToArray());
                Trace.Info($"[deck] saved {Deck.Count} cards");
            }
            catch (Exception e)
            {
                Trace.Error($"[deck] save failed: {e.Message}");
            }
        }

        public static void Add(string cardName)
        {
            if (Deck.Count >= MaxCards) return;
            Deck.Add(cardName);
            Match.Reset();
        }

        public static void Remove(string cardName)
        {
            Deck.Remove(cardName);
            Match.Reset();
        }

        public static int CountOf(string cardName)
        {
            return Deck.Count(c => c == cardName);
        }

        public static void ResetToStarter()
        {
            _deck = new List<string>(Starter);
            Match.Reset();
        }

        /// <summary>
        /// Every Act 1 card a player could normally be offered, rares included, and
        /// regardless of campaign progression - a versus deck shouldn't be gated behind
        /// someone's single-player unlocks.
        /// </summary>
        public static List<CardInfo> Pool
        {
            get
            {
                if (_pool != null) return _pool;

                try
                {
                    var all = ScriptableObjectLoader<CardInfo>.AllData ?? new List<CardInfo>();

                    _pool = all.Where(CanRenderOnAct1Table)
                               .OrderBy(c => c.BloodCost + c.BonesCost)
                               .ThenBy(c => c.DisplayedNameEnglish)
                               .ToList();

                    int rejected = all.Count(c => c != null && !CanRenderOnAct1Table(c));
                    Trace.Info($"[deck] card pool: {_pool.Count} cards ({rejected} excluded as unrenderable here)");
                }
                catch (Exception e)
                {
                    Trace.Error($"[deck] pool build failed: {e.Message}");
                    _pool = new List<CardInfo>();
                }
                return _pool;
            }
        }

        /// <summary>
        /// Whether a card can both be drawn and be paid for on the Act 1 table.
        ///
        /// Temple is no longer the gate: CardRenderFallbacks clamps the cost sprite lookup
        /// and substitutes a pixel portrait when there's no 3D one, so cards from other
        /// acts render. What still rules a card out is having no art at all, or a cost in
        /// a currency this table never grants.
        /// </summary>
        internal static bool CanRenderOnAct1Table(CardInfo c)
        {
            if (c == null || c.metaCategories == null) return false;

            bool offerable = c.metaCategories.Contains(CardMetaCategory.ChoiceNode)
                             || c.metaCategories.Contains(CardMetaCategory.Rare);
            if (!offerable) return false;

            // Needs art of some kind. CardRenderFallbacks substitutes the pixel portrait
            // when there's no 3D one, so a card only fails here if it has neither.
            if (c.portraitTex == null && c.alternatePortrait == null && c.pixelPortrait == null)
                return false;

            // Energy and gems are still out: no energy is granted on the Act 1 table, so
            // those cards would be unplayable even though they'd now draw.
            if (c.EnergyCost > 0) return false;
            if (c.GemsCost != null && c.GemsCost.Count > 0) return false;

            return true;
        }
    }
}
