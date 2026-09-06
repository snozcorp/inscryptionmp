using System.Collections.Generic;
using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>State for a versus match.</summary>
    public static class Match
    {
        /// <summary>True only during an actual versus match.</summary>
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

        /// <summary>A copy of the card carrying the player's chosen sigils.</summary>
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
