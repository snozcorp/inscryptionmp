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
        /// <summary>
        /// True only during an actual versus match. Keying this off "a peer is connected"
        /// meant that sitting in a lobby and then playing a normal campaign battle would
        /// swap the player's campaign deck for their versus deck.
        /// </summary>
        public static bool Active => VersusMode.InMatch;

        private static List<CardInfo> _cache;

        public static List<CardInfo> Deck
        {
            get
            {
                // Hand out a copy: the draw pile takes ownership of this list and removes
                // from it as cards are drawn, which would otherwise eat our cached deck.
                if (_cache != null) return new List<CardInfo>(_cache);

                var deck = new List<CardInfo>();
                foreach (string entry in DeckStore.Deck)
                {
                    string name = DeckStore.BaseName(entry);
                    CardInfo info = CardLoader.GetCardByName(name);
                    if (info == null)
                    {
                        Trace.Warn($"[match] card '{name}' did not resolve - skipping");
                        continue;
                    }

                    string[] sigils = DeckStore.SigilsOf(entry);
                    if (sigils.Length > 0) info = WithSigils(info, sigils);

                    deck.Add(info);
                }

                Trace.Info($"[match] built versus deck with {deck.Count} cards from the player's list");
                _cache = deck;
                return new List<CardInfo>(_cache);
            }
        }

        /// <summary>
        /// A copy of the card carrying the player's chosen sigils.
        ///
        /// Cloned rather than modified: CardLoader hands out the one shared CardInfo for a
        /// card, so adding sigils to it would put them on that card everywhere in the game,
        /// campaign included. CardInfo.Clone gives the copy its own Mods list, which is the
        /// part that matters here.
        /// </summary>
        private static CardInfo WithSigils(CardInfo info, string[] sigils)
        {
            var copy = info.Clone() as CardInfo;
            if (copy == null) return info;

            var mod = new CardModificationInfo { singletonId = "inscryptionmp-deck-sigils" };
            var added = new List<string>();

            foreach (string name in sigils)
            {
                if (!System.Enum.IsDefined(typeof(Ability), name))
                {
                    Trace.Warn($"[match] '{info.name}' asks for unknown sigil '{name}' - ignoring");
                    continue;
                }

                var ability = (Ability)System.Enum.Parse(typeof(Ability), name);
                if (copy.Abilities.Contains(ability)) continue;   // already has it natively

                mod.abilities.Add(ability);
                added.Add(name);
            }

            if (mod.abilities.Count == 0) return info;

            copy.Mods.Add(mod);
            Trace.Info($"[match] {info.name} carries added sigil(s): {string.Join(", ", added.ToArray())}");
            return copy;
        }

        public static void Reset() => _cache = null;
    }
}
