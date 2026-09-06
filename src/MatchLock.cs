using System.Collections;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>Pins the player at the table for the duration of a match.</summary>
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
        /// Part 1's scene initialisation ends by transitioning to the map and then reading
        /// RunState.Run.map.EndNode - and a versus run is synthesised with no map, so that read
        /// throws.
        /// </summary>
        [HarmonyPatch(typeof(Part1GameFlowManager), "SceneSpecificInitialization")]
        [HarmonyPrefix]
        private static bool SkipPart1SceneIntro()
        {
            if (!VersusMode.InMatch && !VersusMode.PendingStart) return true;

            var items = Singleton<ItemsManager>.Instance;
            if (items != null) items.SetSlotsAtEdge(atEdge: true, immediate: true);

            var area = Singleton<ExplorableAreaManager>.Instance;
            if (area != null) area.SetHangingLightShadowStrength(0.5f, 0f);

            Trace.Info("[lock] skipping Part 1 scene intro - a match is starting");
            return false;
        }

        /// <summary>
        /// Part 3's scene initialisation puts the player on the holo map, or plays the P03
        /// intro on a fresh save.
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
        private static bool BlockInternalTransition(GameState gameState, ref IEnumerator __result)
        {
            if (!VersusMode.InMatch && !VersusMode.PendingStart) return true;
            if (gameState == GameState.CardBattle) return true;

            Trace.Warn($"[lock] blocked internal transition to {gameState} during a match");

            // Hand back an empty coroutine, not null. The caller passes the result straight
            // to StartCoroutine, which throws "routine is null" on every blocked transition.
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
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
