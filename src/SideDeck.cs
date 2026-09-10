using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Gives both players the same second pile. The campaign decides it otherwise: the bee
    /// figurine turns Act 1's ten squirrels into ten bees, which are 1/1 and airborne rather
    /// than 0/1 and not, and Act 3's gems module puts gems in its vessels. Neither is
    /// announced anywhere, so two people would be playing with different piles based on how
    /// far each got in single player. A versus deck already ignores that; so does this.
    /// </summary>
    [HarmonyPatch]
    internal static class SideDeck
    {
        private const int Size = 10;

        private static List<CardInfo> TenOf(string cardName)
        {
            var pile = new List<CardInfo>();
            for (int i = 0; i < Size; i++)
            {
                CardInfo card = Cards.ByName(cardName);
                if (card != null) pile.Add(card);
            }

            if (pile.Count == 0) Trace.Error($"[side] could not build a side pile of '{cardName}'");
            return pile;
        }

        [HarmonyPatch(typeof(Part1CardDrawPiles), "SideDeckData", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool Squirrels(ref List<CardInfo> __result)
        {
            if (!VersusMode.VersusContext) return true;

            __result = TenOf("Squirrel");
            Trace.Info($"[side] versus: {__result.Count} squirrels, whatever the save says");
            return false;
        }

        [HarmonyPatch(typeof(Part3CardDrawPiles), "SideDeckData", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool Vessels(ref List<CardInfo> __result)
        {
            if (!VersusMode.VersusContext) return true;

            __result = TenOf("EmptyVessel");
            Trace.Info($"[side] versus: {__result.Count} empty vessels, no gems and no mods");
            return false;
        }

        /// <summary>
        /// The pile's figurine and card backs are set from the same story events. Left alone
        /// they would advertise bees over a pile of squirrels.
        /// </summary>
        [HarmonyPatch(typeof(Part1CardDrawPiles), nameof(Part1CardDrawPiles.UpdateSidePileFigurine))]
        [HarmonyPrefix]
        private static bool KeepTheSquirrelFigurine()
        {
            if (!VersusMode.VersusContext) return true;

            Trace.Info("[side] versus: leaving the squirrel figurine in place");
            return false;
        }
    }
}
