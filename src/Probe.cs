using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Milestone 1 probe: confirm we can see and hook the real battle state machine.
    /// Logs every turn transition and every card the opponent queues.
    /// </summary>
    [HarmonyPatch]
    internal static class Probe
    {
        [HarmonyPatch(typeof(TurnManager), nameof(TurnManager.OnCombatBellRang))]
        [HarmonyPostfix]
        private static void BellRang()
        {
            Trace.Info($"[probe] bell rang - local player ended turn {Singleton<TurnManager>.Instance?.TurnNumber}");
        }

        [HarmonyPatch(typeof(Opponent), nameof(Opponent.QueueCard))]
        [HarmonyPrefix]
        private static void QueueCard(CardInfo cardInfo, CardSlot slot)
        {
            Trace.Info($"[probe] opponent queued '{cardInfo?.DisplayedNameEnglish}' -> slot idx {slot?.Index}");
        }
    }
}
