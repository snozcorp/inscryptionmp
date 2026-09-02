using System.Collections.Generic;
using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// State for a versus match. A match deliberately does NOT use the player's saved
    /// deck: both sides get the same fixed list, which makes the game fair and — just as
    /// importantly — keeps us off DeckInfo.LoadCards(), the campaign save path that
    /// throws when a saved deck references a card name the loader can't resolve.
    /// </summary>
    public static class Match
    {
        /// <summary>A match is live whenever a peer is connected.</summary>
        public static bool Active => Net.Connected;

        private static List<CardInfo> _cache;

        public static List<CardInfo> Deck
        {
            get
            {
                if (_cache != null) return _cache;

                var deck = new List<CardInfo>();
                foreach (string name in DeckStore.Deck)
                {
                    CardInfo info = CardLoader.GetCardByName(name);
                    if (info == null)
                    {
                        Trace.Warn($"[match] card '{name}' did not resolve - skipping");
                        continue;
                    }
                    deck.Add(info);
                }

                Trace.Info($"[match] built versus deck with {deck.Count} cards from the player's list");
                _cache = deck;
                return _cache;
            }
        }

        public static void Reset() => _cache = null;
    }
}
