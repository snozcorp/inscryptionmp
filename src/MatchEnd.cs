using System.Collections;
using DiskCardGame;
using HarmonyLib;

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

        /// <summary>
        /// Ends the match in acts that never reach the transition above.
        ///
        /// CleanupPhase only calls TransitionToNextGameState when a GameFlowManager exists,
        /// and GBC has none - so in Act 2 the prefix never fires. By then cleanup has
        /// already hidden the hand and torn the board down, leaving the player looking at a
        /// dead screen with nothing left to drive it.
        /// </summary>
        [HarmonyPatch(typeof(TurnManager), "CleanupPhase")]
        [HarmonyPostfix]
        private static IEnumerator EndWithoutFlowManager(IEnumerator __result, TurnManager __instance)
        {
            while (__result.MoveNext()) yield return __result.Current;

            if (!VersusMode.InMatch) yield break;
            if (ActInfo.HasFlowManager(ActInfo.Current)) yield break;   // the prefix handled it

            Trace.Info("[end] no flow manager in this act - finishing the match here");
            VersusMode.Finish(__instance, __instance.PlayerWon, "battle resolved");
        }
    }
}
