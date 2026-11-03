using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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

        /// <summary>The post DoCombatPhase caps the attacker's damage at, in place of its 5.</summary>
        public static int CombatPost() => Ours ? Goal : LifeManager.GOAL_BALANCE;
    }

    /// <summary>
    /// The fifth hardcoded 5. DoCombatPhase cuts the attacker's damage at "Balance + dmg - 5"
    /// - only on the attacker's own screen - so with any other post the two scales drifted
    /// apart every time someone got within reach of 5. Swaps that literal for the real post.
    /// </summary>
    [HarmonyPatch]
    internal static class CombatExcessDamage
    {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CombatPhaseManager),
                                                              nameof(CombatPhaseManager.DoCombatPhase)));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo post = AccessTools.Method(typeof(WinCondition), nameof(WinCondition.CombatPost));

            // State-machine bookkeeping also loads 5, but never straight into a subtraction.
            var hits = new List<int>();
            for (int i = 0; i < code.Count - 1; i++)
                if (code[i].opcode == OpCodes.Ldc_I4_5 && code[i + 1].opcode == OpCodes.Sub) hits.Add(i);

            if (hits.Count != 1)
            {
                Plugin.Log.LogWarning($"[win] expected one '- 5' in DoCombatPhase, found {hits.Count} - left alone");
                return code;
            }

            code[hits[0]] = new CodeInstruction(OpCodes.Call, post).MoveLabelsFrom(code[hits[0]]);
            Plugin.Log.LogInfo("[win] combat damage cap now follows the match's post");
            return code;
        }
    }
}
