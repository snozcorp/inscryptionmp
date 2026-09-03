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

        /// <summary>Which act the cached deck and pool belong to.</summary>
        private static MatchAct _cachedFor = MatchAct.Act1;

        /// <summary>Drops cached state when the selected act changes.</summary>
        private static void EnsureAct()
        {
            if (_cachedFor == ActInfo.Selected) return;
            _cachedFor = ActInfo.Selected;
            _deck = null;
            _pool = null;
            Match.Reset();
            Trace.Info($"[deck] switched to {ActInfo.Name(_cachedFor)} deck and pool");
        }

        private static string Path
        {
            get
            {
                string dir = BepInEx.Paths.ConfigPath ?? ".";
                return System.IO.Path.Combine(dir, "inscryptionmp-deck" + ActInfo.DeckSuffix(ActInfo.Selected) + ".txt");
            }
        }

        public static List<string> Deck
        {
            get
            {
                EnsureAct();
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
                if (info != null && CanUseInDeck(info)) kept.Add(name);
                else dropped.Add(name);
            }

            if (dropped.Count == 0) return;

            // In memory only. Overwriting the file here once destroyed a perfectly good
            // deck when the filter was wrong, and there's no reason to make that
            // unrecoverable - the file is re-pruned on every load anyway.
            _deck = kept;
            Trace.Warn($"[deck] ignoring {dropped.Count} card(s) not usable in {ActInfo.Name(ActInfo.Selected)}: {string.Join(", ", dropped)}");
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
                EnsureAct();
                if (_pool != null) return _pool;

                try
                {
                    var all = ScriptableObjectLoader<CardInfo>.AllData ?? new List<CardInfo>();

                    _pool = all.Where(IsOfferedInPool)
                               .OrderBy(c => c.BloodCost + c.BonesCost)
                               .ThenBy(c => c.DisplayedNameEnglish)
                               .ToList();

                    int rejected = all.Count(c => c != null && !IsOfferedInPool(c));
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
        /// Whether a card can be drawn and paid for on the Act 1 table.
        ///
        /// Other acts' cards are excluded on purpose. Making them look right here needs
        /// portraits, cost sprites and sigil icons this table doesn't have - each one
        /// patched reveals the next. The answer is to run the match in the act's own
        /// scene, which is what per-act matches will do.
        /// </summary>
        /// <summary>
        /// Whether a card is legal in a deck for the selected act - it belongs to that
        /// act's table and can be drawn and paid for there.
        ///
        /// Deliberately looser than <see cref="IsOfferedInPool"/>. Squirrels and other
        /// starter cards are never offered at choice nodes but are perfectly playable, and
        /// conflating the two once pruned them out of a saved deck.
        /// </summary>
        internal static bool CanUseInDeck(CardInfo c)
        {
            if (c == null) return false;
            if (c.temple != ActInfo.TempleFor(ActInfo.Selected)) return false;

            // Needs real 3D portrait art; a pixel-only portrait renders wrong at card scale.
            if (c.portraitTex == null) return false;

            // Only exclude costs this act's table can't actually pay. Banning energy
            // outright was an Act 1 rule; Act 3 grants energy and every Tech card uses it,
            // so applying it everywhere emptied that act's pool entirely.
            if (c.EnergyCost > 0 && !ActInfo.GrantsEnergy(ActInfo.Selected)) return false;
            if (c.GemsCost != null && c.GemsCost.Count > 0 && !ActInfo.GrantsGems(ActInfo.Selected)) return false;

            return true;
        }

        /// <summary>
        /// Whether a card should appear in the browsable pool. Usable, and something the
        /// game would normally offer a player rather than an internal or token card.
        /// </summary>
        private static bool IsOfferedInPool(CardInfo c)
        {
            if (!CanUseInDeck(c)) return false;
            if (c.metaCategories == null) return false;

            foreach (CardMetaCategory cat in ActInfo.OfferedCategories(ActInfo.Selected))
                if (c.metaCategories.Contains(cat)) return true;

            return false;
        }
    }
}
