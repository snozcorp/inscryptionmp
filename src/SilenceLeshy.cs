using System.Collections;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Suppresses campaign dialogue during a versus match.
    ///
    /// The match runs inside the Act 1 scene, so Leshy narrates it as though it were part
    /// of a run - commenting on draws, taunting, reacting to the scales. None of that
    /// belongs in a match against another player, and some lines wait for input, which
    /// stalls a turn the opponent is waiting on.
    /// </summary>
    [HarmonyPatch]
    internal static class SilenceLeshy
    {
        [HarmonyPatch(typeof(TextDisplayer), nameof(TextDisplayer.PlayDialogueEvent))]
        [HarmonyPrefix]
        private static bool SkipDialogueEvent(ref IEnumerator __result, string eventId)
        {
            if (!VersusMode.InMatch) return true;
            Trace.Info($"[quiet] suppressed dialogue '{eventId}'");
            __result = Nothing();
            return false;
        }

        [HarmonyPatch(typeof(TextDisplayer), nameof(TextDisplayer.ShowUntilInput))]
        [HarmonyPrefix]
        private static bool SkipShowUntilInput(ref IEnumerator __result)
        {
            if (!VersusMode.InMatch) return true;
            __result = Nothing();
            return false;
        }

        [HarmonyPatch(typeof(TextDisplayer), nameof(TextDisplayer.ShowThenClear))]
        [HarmonyPrefix]
        private static bool SkipShowThenClear(ref IEnumerator __result)
        {
            if (!VersusMode.InMatch) return true;
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }
}
