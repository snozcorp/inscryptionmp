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
        /// Every Act 1 creature the game ships, regardless of campaign progression - a
        /// versus deck shouldn't be gated behind someone's single-player unlocks.
        /// </summary>
        public static List<CardInfo> Pool
        {
            get
            {
                if (_pool != null) return _pool;

                try
                {
                    _pool = ScriptableObjectLoader<CardInfo>.AllData
                        .Where(c => c != null
                                    && c.temple == CardTemple.Nature
                                    && c.metaCategories != null
                                    && c.metaCategories.Contains(CardMetaCategory.ChoiceNode))
                        .OrderBy(c => c.BloodCost)
                        .ThenBy(c => c.DisplayedNameEnglish)
                        .ToList();
                    Trace.Info($"[deck] card pool: {_pool.Count} cards");
                }
                catch (Exception e)
                {
                    Trace.Error($"[deck] pool build failed: {e.Message}");
                    _pool = new List<CardInfo>();
                }
                return _pool;
            }
        }
    }
}
