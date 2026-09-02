using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// A vanilla battle ends by transitioning back to the map. A versus match has no map
    /// to go back to, and MatchLock blocks that transition anyway - so without this the
    /// player is stranded at an empty table the moment someone wins.
    /// </summary>
    [HarmonyPatch]
    internal static class MatchEnd
    {
        [HarmonyPatch(typeof(TurnManager), "TransitionToNextGameState")]
        [HarmonyPrefix]
        private static bool InterceptEnd(TurnManager __instance)
        {
            if (!VersusMode.InMatch) return true;

            VersusMode.Finish(__instance, __instance.PlayerWon, "battle resolved");
            return false;   // don't go looking for a map
        }
    }
}
