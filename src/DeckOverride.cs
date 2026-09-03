using System.Collections.Generic;
using DiskCardGame;
using GBC;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Supplies the versus deck instead of the player's campaign deck while a match is live.
    ///
    /// This is what makes a match independent of the save file. It also sidesteps the
    /// NullReferenceException in CardLoader.Clone that DeckInfo.LoadCards() raises when a
    /// saved deck contains an unresolvable card name.
    ///
    /// Every act builds its draw pile from an abstract DeckData getter, but each concrete
    /// subclass reads a different source, so each has to be patched. Act 1 and Act 3 share
    /// CardDrawPiles3D; GBC has its own, reading a deck that our synthetic run leaves empty.
    /// </summary>
    [HarmonyPatch]
    internal static class DeckOverride
    {
        [HarmonyPatch(typeof(CardDrawPiles3D), "DeckData", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool DeckData3D(ref List<CardInfo> __result) => Serve(ref __result);

        /// <summary>
        /// GBC draws from GBC.SaveData.Data.deck, which Initialize() sets to an empty
        /// DeckInfo. Left alone, Deck.GetFairHand indexes that empty list and throws before
        /// a single card is dealt.
        /// </summary>
        [HarmonyPatch(typeof(PixelCardDrawPiles), "DeckData", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool DeckDataPixel(ref List<CardInfo> __result) => Serve(ref __result);

        private static bool Serve(ref List<CardInfo> __result)
        {
            if (!Match.Active) return true;   // campaign: leave the game alone

            __result = Match.Deck;
            Trace.Info($"[deck] serving versus deck ({__result.Count} cards) instead of save deck");
            return false;                     // skip original
        }
    }
}
