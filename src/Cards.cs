using System;
using System.Collections.Generic;
using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>Card lookups that survive a name the game has never heard of.</summary>
    internal static class Cards
    {
        /// <summary>
        /// CardLoader.GetCardByName is Clone(AllData.Find(...)) with nothing between the two,
        /// so an unknown name throws instead of returning null - and card names reach us from
        /// hand-edited deck files and from the peer's own wire.
        /// </summary>
        public static CardInfo ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            try
            {
                List<CardInfo> all = ScriptableObjectLoader<CardInfo>.AllData;
                if (all == null) return null;

                CardInfo found = all.Find(x => x != null && x.name == name);

                // Cloned, like the game's own lookup: callers add mods to what they get
                // back, and the loader hands out one shared instance per card.
                return found == null ? null : found.Clone() as CardInfo;
            }
            catch (Exception e)
            {
                Trace.Warn($"[cards] lookup of '{name}' failed: {e.Message}");
                return null;
            }
        }

        public static bool Exists(string name) => ByName(name) != null;
    }
}
