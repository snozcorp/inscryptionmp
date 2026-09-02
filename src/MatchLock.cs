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
