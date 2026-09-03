using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Pins the player at the table for the duration of a match.
    ///
    /// GameFlowManager polls input every frame in UpdateForTransitionToFirstPerson(), so
    /// no view state alone can stop the player standing up - and standing up mid-match
    /// re-reveals the map and leaves GameFlowManager in the wrong state. Blocking the
    /// transition itself is the only reliable fix.
    /// </summary>
    [HarmonyPatch]
    internal static class MatchLock
    {
        [HarmonyPatch(typeof(GameFlowManager), nameof(GameFlowManager.TransitionToFirstPerson))]
        [HarmonyPrefix]
        private static bool BlockStandingUp()
        {
            if (!VersusMode.InMatch) return true;
            return false;   // swallow it; you're playing a match
        }

        /// <summary>
        /// Part 3's scene initialisation puts the player on the holo map, or plays the P03
        /// intro on a fresh save. Either one hijacks a match that is starting - and it
        /// reaches the map through the protected TransitionTo, so blocking
        /// TransitionToGameState never caught it.
        /// </summary>
        [HarmonyPatch(typeof(Part3GameFlowManager), "SceneSpecificInitialization")]
        [HarmonyPrefix]
        private static bool SkipPart3SceneIntro()
        {
            if (!VersusMode.InMatch && !VersusMode.PendingStart) return true;
            Trace.Info("[lock] skipping Part 3 scene intro - a match is starting");
            return false;
        }

        /// <summary>
        /// Backstop for anything else inside the flow manager that tries to leave the
        /// battle by the protected route.
        /// </summary>
        [HarmonyPatch(typeof(GameFlowManager), "TransitionTo")]
        [HarmonyPrefix]
        private static bool BlockInternalTransition(GameState gameState)
        {
            if (!VersusMode.InMatch && !VersusMode.PendingStart) return true;
            if (gameState == GameState.CardBattle) return true;

            Trace.Warn($"[lock] blocked internal transition to {gameState} during a match");
            return false;
        }

        [HarmonyPatch(typeof(GameFlowManager), nameof(GameFlowManager.TransitionToGameState))]
        [HarmonyPrefix]
        private static bool BlockStateChange(GameState gameState)
        {
            if (!VersusMode.InMatch) return true;
            if (gameState == GameState.CardBattle) return true;

            Trace.Warn($"[lock] blocked transition to {gameState} during match");
            return false;
        }
    }
}
