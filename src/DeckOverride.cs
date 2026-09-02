using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Supplies the versus deck instead of the player's campaign deck while a match is live.
    ///
    /// This is what makes a match independent of the save file. It also sidesteps the
    /// NullReferenceException in CardLoader.Clone that DeckInfo.LoadCards() raises when a
    /// saved deck contains an unresolvable card name.
    /// </summary>
    [HarmonyPatch]
    internal static class DeckOverride
    {
        [HarmonyPatch(typeof(CardDrawPiles3D), "DeckData", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool DeckData(ref List<CardInfo> __result)
        {
            if (!Match.Active) return true;   // campaign: leave the game alone

            __result = Match.Deck;
            Trace.Info($"[deck] serving versus deck ({__result.Count} cards) instead of save deck");
            return false;                     // skip original
        }
    }
}
