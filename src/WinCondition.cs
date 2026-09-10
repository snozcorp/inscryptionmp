using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Moves the post the scales have to pass. Inscryption has no hit points - you win when
    /// your damage lead reaches five - and that five is a constant compiled into three
    /// private TurnManager checks and one LifeManager property, so the only way to move it
    /// is to answer those four questions ourselves. Each is two lines of arithmetic, and
    /// each is left alone outside a versus match.
    /// </summary>
    [HarmonyPatch]
    internal static class WinCondition
    {
        /// <summary>The lead a player needs, this match.</summary>
        private static int Goal => MatchRules.Live.DamageToWin;

        private static bool Ours => VersusMode.InMatch;

        private static int Balance
        {
            get
            {
                var life = Singleton<LifeManager>.Instance;
                return life == null ? 0 : life.Balance;
            }
        }

        [HarmonyPatch(typeof(LifeManager), nameof(LifeManager.DamageUntilPlayerWin), MethodType.Getter)]
        [HarmonyPrefix]
        private static bool HowMuchIsLeft(ref int __result)
        {
            if (!Ours) return true;
            __result = Goal - Balance;
            return false;
        }

        [HarmonyPatch(typeof(TurnManager), "ScalesTippedToOpponent")]
        [HarmonyPrefix]
        private static bool TippedTheirWay(ref bool __result)
        {
            if (!Ours) return true;
            __result = Balance >= Goal;
            return false;
        }

        [HarmonyPatch(typeof(TurnManager), "LifeLossConditionsMet")]
        [HarmonyPrefix]
        private static bool SomebodyIsOut(TurnManager __instance, ref bool __result)
        {
            if (!Ours) return true;

            Opponent opponent = __instance.Opponent;
            bool conceded = opponent != null && opponent.Surrendered;

            __result = (Mathf.Abs(Balance) >= Goal || conceded) || __instance.PlayerSurrendered;
            return false;
        }

        [HarmonyPatch(typeof(TurnManager), "GameIsOver")]
        [HarmonyPrefix]
        private static bool ThatIsThat(TurnManager __instance, ref bool __result)
        {
            if (!Ours) return true;

            Opponent opponent = __instance.Opponent;
            bool conceded = opponent != null && opponent.Surrendered;

            // PlayerIsWinner() is private and one line: the opponent is beaten when it has
            // no lives left, or has conceded.
            bool playerWon = opponent == null || opponent.NumLives <= 0 || conceded;

            __result = playerWon || Balance <= -Goal || conceded || __instance.PlayerSurrendered;
            return false;
        }
    }
}
