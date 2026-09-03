using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Match tracing. Deliberately gated on an active match: these hooks sit on ordinary
    /// campaign code paths, so ungated they fill the player's log during single player.
    /// </summary>
    [HarmonyPatch]
    internal static class Probe
    {
        [HarmonyPatch(typeof(TurnManager), nameof(TurnManager.OnCombatBellRang))]
        [HarmonyPostfix]
        private static void BellRang()
        {
            if (!VersusMode.InMatch) return;
            Trace.Info($"[probe] bell rang - local player ended turn {Singleton<TurnManager>.Instance?.TurnNumber}");
        }
    }
}
