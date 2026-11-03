using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>A few numbers about the match just played, for the lobby after it.</summary>
    [HarmonyPatch]
    internal static class MatchSummary
    {
        private static int _ourPlays, _theirPlays;
        private static int? _finalBalance;
        private static float _startedAt;

        /// <summary>The finished match in one or two lines, or null before the first.</summary>
        public static string Text { get; private set; }

        public static void Begin()
        {
            _ourPlays = _theirPlays = 0;
            _finalBalance = null;
            _startedAt = Time.realtimeSinceStartup;
        }

        public static void NotePlay(bool mine)
        {
            if (mine) _ourPlays++;
            else _theirPlays++;
        }

        public static void End(bool weWon, string reason)
        {
            var life = Singleton<LifeManager>.Instance;
            var turns = Singleton<TurnManager>.Instance;
            int minutes = Mathf.Max(1, Mathf.RoundToInt((Time.realtimeSinceStartup - _startedAt) / 60f));

            var parts = new List<string>();
            if (turns != null && turns.TurnNumber > 0) parts.Add("round " + turns.TurnNumber);
            parts.Add(minutes + " min");
            if (_finalBalance.HasValue || life != null)
            {
                int b = _finalBalance ?? life.Balance;
                parts.Add("scales " + (b > 0 ? "+" + b : b.ToString()) + " / " + MatchRules.Live.DamageToWin);
            }
            parts.Add("cards played " + _ourPlays + " - " + _theirPlays);

            Text = string.Join("   ", parts.ToArray());
            if (ScaleSync.Corrections > 0)
                Text += "\nScales resynced " + ScaleSync.Corrections + "x - the log has the details";

            Trace.Info($"[summary] {(weWon ? "won" : "lost")} ({reason}): {Text.Replace("\n", " | ")}");
        }

        public static void Forget() => Text = null;

        /// <summary>The battle's cleanup zeroes the scales before the match is finished.</summary>
        [HarmonyPatch(typeof(LifeManager), nameof(LifeManager.CleanUp))]
        [HarmonyPrefix]
        private static void KeepFinalScale(LifeManager __instance)
        {
            if (VersusMode.InMatch && __instance != null) _finalBalance = __instance.Balance;
        }
    }
}
