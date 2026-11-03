using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// The player's versus deck: a list of card names persisted beside the BepInEx config.
    /// </summary>
    public static class DeckStore
    {
        public const int MinCards = DeckLimits.Min;

        /// <summary>
        /// The most cards a deck may hold, and the most sigils one card may wear. Both are
        /// match rules, so the deck builder shows what the host has set rather than a
        /// number that turns out to be wrong the moment you sit down.
        /// </summary>
        public static int MaxCards => MatchRules.Effective.MaxDeck;

        public static int MaxAddedSigils => MatchRules.Effective.SigilsPerCard;

        private static readonly string[] NoSigils = new string[0];

        /// <summary>
        /// A deck line is "CardName", or "CardName:Sigil,Sigil" once sigils are added.
        /// Plain names still load, so decks written before this stay valid.
        /// </summary>
        internal static string BaseName(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return entry;
            int colon = entry.IndexOf(':');
            return colon < 0 ? entry : entry.Substring(0, colon);
        }

        internal static string[] SigilsOf(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return NoSigils;
            int colon = entry.IndexOf(':');
            if (colon < 0 || colon == entry.Length - 1) return NoSigils;
            return entry.Substring(colon + 1).Split(',');
        }

        internal static string MakeEntry(string name, IList<string> sigils)
        {
            if (sigils == null || sigils.Count == 0) return name;
            return name + ":" + string.Join(",", sigils.ToArray());
        }

        /// <summary>Replaces the sigils on one deck entry, keeping its card.</summary>
        public static void SetSigils(int index, IList<string> sigils)
        {
            if (index < 0 || index >= Deck.Count) return;

            Deck[index] = MakeEntry(BaseName(Deck[index]), sigils);
            Match.Reset();   // the built deck is stale now
            Save();
        }

        /// <summary>Act 1's starter deck, matching what the campaign hands a new player.</summary>
        private static readonly string[] Act1Starter =
        {
            "Stoat", "Stoat", "Bullfrog", "Bullfrog",
            "Wolf", "Wolf", "Adder",
            "Squirrel", "Squirrel", "Squirrel",
        };

        private const int StarterSize = 10;

        /// <summary>A starting deck for an act the player hasn't built one for.</summary>
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

        /// <summary>
        /// Card counts for acts other than the selected one, so the act buttons can show all
        /// three at once. Loading each act's deck in turn would blow the cache every frame.
        /// </summary>
        private static readonly Dictionary<MatchAct, int> Counts = new Dictionary<MatchAct, int>();

        /// <summary>
        /// How many cards this act's saved deck holds, or -1 if it has never been saved.
        /// The selected act answers from the live deck; the others are read off disk once.
        /// </summary>
        public static int CountFor(MatchAct act)
        {
            if (act == ActInfo.Selected) return Deck.Count;

            int n;
            if (Counts.TryGetValue(act, out n)) return n;
            n = CountOnDisk(act);
            Counts[act] = n;
            return n;
        }

        private static int CountOnDisk(MatchAct act)
        {
            try
            {
                string path = PathFor(act);
                if (!File.Exists(path)) return -1;

                int n = 0;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length > 0 && !line.StartsWith("#")) n++;
                }
                return n;
            }
            catch (Exception e)
            {
                Trace.Warn($"[deck] could not count the {ActInfo.Name(act)} deck: {e.Message}");
                return -1;
            }
        }

        private static string PathFor(MatchAct act)
        {
            string dir = BepInEx.Paths.ConfigPath ?? ".";
            return System.IO.Path.Combine(dir, "inscryptionmp-deck" + ActInfo.DeckSuffix(act) + ".txt");
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
                CardInfo info = Cards.ByName(BaseName(name));
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

        /// <summary>
        /// Keeps a copy of this act's deck file the first time something replaces it
        /// wholesale, which only the autopilot does. A test run does not get to lose
        /// somebody's deck.
        /// </summary>
        internal static void BackUpOnce()
        {
            try
            {
                string path = Path;
                string backup = path + ".pre-autopilot";
                if (!File.Exists(path) || File.Exists(backup)) return;

                File.Copy(path, backup);
                Trace.Info($"[deck] kept the existing {ActInfo.Name(ActInfo.Selected)} deck at {backup}");
            }
            catch (Exception e)
            {
                Trace.Warn($"[deck] could not keep a copy of the deck: {e.Message}");
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(Path, Deck.ToArray());
                Counts.Clear();   // the numbers on the act buttons just moved
                Trace.Info($"[deck] saved {Deck.Count} cards");
            }
            catch (Exception e)
            {
                Notice.Bad($"Couldn't save your deck: {e.Message}");
                Trace.Error($"[deck] save failed: {e.Message}");
            }
        }

        /// <summary>Returns false when the deck is already as big as it may be.</summary>
        public static bool Add(string cardName)
        {
            if (Deck.Count >= MaxCards) return false;
            Deck.Add(cardName);
            Match.Reset();
            return true;
        }

        /// <summary>
        /// Moves one entry along by a place. Order is the order cards are drawn in when the
        /// match is played that way, so it is worth being able to set.
        /// </summary>
        public static bool Move(int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || index >= Deck.Count) return false;
            if (target < 0 || target >= Deck.Count) return false;

            string entry = Deck[index];
            Deck.RemoveAt(index);
            Deck.Insert(target, entry);
            Match.Reset();
            Save();
            return true;
        }

        /// <summary>
        /// Removes one entry by position. Removing by name can't tell two copies of a card
        /// apart once they carry different sigils.
        /// </summary>
        public static void RemoveAt(int index)
        {
            if (index < 0 || index >= Deck.Count) return;
            Deck.RemoveAt(index);
            Match.Reset();
            Save();
        }

        public static void Remove(string cardName)
        {
            Deck.Remove(cardName);
            Match.Reset();
        }

        public static int CountOf(string cardName)
        {
            return Deck.Count(c => BaseName(c) == cardName);
        }

        /// <summary>
        /// Replaces the selected act's deck with a pasted one. Cards this act can't use are
        /// left out, and the list is cut at the deck limit. Returns how many were dropped.
        /// </summary>
        public static int ReplaceWith(IList<string> entries)
        {
            var kept = new List<string>();
            foreach (string entry in entries)
            {
                CardInfo info = Cards.ByName(BaseName(entry));
                if (info != null && CanUseInDeck(info) && kept.Count < MaxCards) kept.Add(entry);
            }

            try
            {
                if (File.Exists(Path)) File.Copy(Path, Path + ".before-import", overwrite: true);
            }
            catch (Exception e)
            {
                Trace.Warn($"[deck] could not back up the deck before importing: {e.Message}");
            }

            EnsureAct();   // otherwise the next read drops this as another act's deck
            _deck = kept;
            Match.Reset();
            Save();

            int dropped = entries.Count - kept.Count;
            Trace.Info($"[deck] imported {kept.Count} card(s) into {ActInfo.Name(ActInfo.Selected)}, left out {dropped}");
            return dropped;
        }

        public static void ResetToStarter()
        {
            _deck = StarterFor(ActInfo.Selected);
            Match.Reset();
        }

        /// <summary>
        /// Every Act 1 card a player could normally be offered, rares included, and regardless
        /// of campaign progression - a versus deck shouldn't be gated behind someone's single-
        /// player unlocks.
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
                    LogPlayableButUnoffered(all);
                }
                catch (Exception e)
                {
                    Trace.Error($"[deck] pool build failed: {e.Message}");
                    _pool = new List<CardInfo>();
                }
                return _pool;
            }
        }

        /// <summary>Whether a card can be drawn and paid for on the Act 1 table.</summary>
        /// <summary>
        /// Whether a card is legal in a deck for the selected act - it belongs to that act's
        /// table and can be drawn and paid for there.
        /// </summary>
        internal static bool CanUseInDeck(CardInfo c) => CanUseInDeck(c, ActInfo.Selected);

        internal static bool CanUseInDeck(CardInfo c, MatchAct act)
        {
            if (c == null) return false;
            if (!ActInfo.AcceptsTemple(act, c.temple)) return false;

            // Needs whichever portrait this act's renderer actually draws. Act 2 draws
            // pixelPortrait and nothing else, so a card without one is a blank rectangle
            // with stats on it - Ant and Skink are the ones you notice.
            if (ActInfo.UsesPixelArt(act))
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
            if (c.EnergyCost > 0 && !ActInfo.GrantsEnergy(act)) return false;
            if (c.GemsCost != null && c.GemsCost.Count > 0 && !ActInfo.GrantsGems(act)) return false;

            return true;
        }

        /// <summary>
        /// Whether a card should appear in the browsable pool. Usable, and something the
        /// game would normally offer a player rather than an internal or token card.
        /// </summary>
        private static bool IsOfferedInPool(CardInfo c)
        {
            if (!CanUseInDeck(c)) return false;

            // A card the act plainly deals in but never *offers* - the squirrel pile is
            // the whole example. Checked before the categories because it is exactly the
            // case they get wrong.
            foreach (string name in ActInfo.AlwaysOffered(ActInfo.Selected))
                if (c.name == name) return true;

            if (c.metaCategories == null) return false;

            foreach (CardMetaCategory cat in ActInfo.OfferedCategories(ActInfo.Selected))
                if (c.metaCategories.Contains(cat)) return true;

            return false;
        }

        /// <summary>
        /// Cards this act could draw and pay for, that nothing marks as offerable. Squirrels
        /// sat in here for four releases without anyone being able to see why, so the pool
        /// now says what it left out rather than only how much.
        /// </summary>
        private static void LogPlayableButUnoffered(List<CardInfo> all)
        {
            var names = all.Where(c => c != null && CanUseInDeck(c) && !IsOfferedInPool(c))
                           .Select(c => c.name)
                           .OrderBy(n => n)
                           .ToList();

            if (names.Count == 0) return;

            const int cap = 60;
            string list = string.Join(", ", names.Take(cap).ToArray());
            if (names.Count > cap) list += $", ... (+{names.Count - cap} more)";
            Trace.Info($"[deck] playable here but not offered ({names.Count}): {list}");
        }
    }
}
