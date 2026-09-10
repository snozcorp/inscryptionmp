using System.Collections;
using DiskCardGame;
using GBC;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Suppresses campaign dialogue during a versus match. Each act has its own way of
    /// talking to you: Acts 1 and 3 go through TextDisplayer, Act 2 through its own TextBox
    /// with a character who walks on to deliver it. In a match there is nobody there to be
    /// explaining sigils to, and the box sits over the board while it does it.
    /// </summary>
    [HarmonyPatch]
    internal static class SilenceDialogue
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

        [HarmonyPatch(typeof(TextBox), nameof(TextBox.ShowUntilInput))]
        [HarmonyPrefix]
        private static bool SkipGbcMessage(ref IEnumerator __result, string message)
        {
            if (!VersusMode.InMatch) return true;
            Trace.Info($"[quiet] suppressed GBC message: {message}");
            __result = Nothing();
            return false;
        }

        /// <summary>
        /// And the speaker who comes with it - blocking the message alone leaves whoever
        /// was about to say it standing on the board.
        /// </summary>
        [HarmonyPatch(typeof(TextBox), nameof(TextBox.EnterSpeaker))]
        [HarmonyPrefix]
        private static bool SkipGbcSpeaker()
        {
            if (!VersusMode.InMatch) return true;
            Trace.Info("[quiet] kept a GBC speaker off the board");
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }
}
