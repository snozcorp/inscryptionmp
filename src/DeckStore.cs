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

        /// <summary>Act 1's starter deck, matching what the campaign hands a new player.</summary>
        private static readonly string[] Act1Starter =
        {
            "Stoat", "Stoat", "Bullfrog", "Bullfrog",
            "Wolf", "Wolf", "Adder",
            "Squirrel", "Squirrel", "Squirrel",
        };

        private const int StarterSize = 10;

        /// <summary>
        /// A starting deck for an act the player hasn't built one for.
        ///
        /// Only Act 1 gets a hand-written list. The other acts draw theirs from their own
        /// pool instead of hardcoded names: it can't reference a card that doesn't exist,
        /// and every pick is legal on that act's table by construction. Act 1's names
        /// happen to be legal in Act 2 as well, so without this an Act 2 deck silently
        /// started out as squirrels and wolves.
        /// </summary>
        private static List<string> StarterFor(MatchAct act)
        {
            if (act == MatchAct.Act1) return new List<string>(Act1Starter);

            var cheapest = Pool.Take(5).ToList();
            if (cheapest.Count == 0)
            {
                Trace.Warn($"[deck] {ActInfo.Name(act)} pool is empty - no starter deck to build");
                return new List<string>();
            }

            var deck = new List<string>();
            while (deck.Count < StarterSize)
                foreach (CardInfo c in cheapest)
                {
                    if (deck.Count >= StarterSize) break;
                    deck.Add(c.name);
                }

            Trace.Info($"[deck] built a {deck.Count}-card starter for {ActInfo.Name(act)} from its pool");
            return deck;
        }

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
                    _deck = StarterFor(ActInfo.Selected);
                    Trace.Info($"[deck] no saved deck for {ActInfo.Name(ActInfo.Selected)} - using starter");

                    // An empty starter means the card database wasn't loaded yet. Drop the
                    // cache so the next access rebuilds it rather than sticking at zero.
                    if (_deck.Count == 0) _cachedFor = (MatchAct)0;
                }
            }
            catch (Exception e)
            {
                Trace.Error($"[deck] load failed: {e.Message}");
                _deck = StarterFor(ActInfo.Selected);
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
            _deck = StarterFor(ActInfo.Selected);
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
            if (!ActInfo.AcceptsTemple(ActInfo.Selected, c.temple)) return false;

            // Needs whichever portrait this act's renderer actually draws.
            if (ActInfo.UsesPixelArt(ActInfo.Selected))
            {
                if (c.pixelPortrait == null) return false;
            }
            else if (c.portraitTex == null)
            {
                return false;
            }

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
