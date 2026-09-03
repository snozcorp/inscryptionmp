using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Crash insurance for the card renderer.
    ///
    /// <c>GetCostSpriteForCard</c> indexes <c>boneCostTextures</c> and <c>costTextures</c>
    /// with no bounds check, so a card costing more bones or blood than the current act's
    /// sprite array covers throws IndexOutOfRangeException mid-render. The gem and energy
    /// branches are already guarded and fall through safely; only these two throw.
    ///
    /// This does not try to make one act's cards look right on another act's table - that
    /// approach needs portraits, cost sprites and sigil icons the table simply doesn't
    /// have, and the answer there is to run the match in the act's own scene. This is only
    /// here so an unexpected card degrades to an approximate icon instead of an exception.
    /// </summary>
    [HarmonyPatch]
    internal static class CardRenderFallbacks
    {
        [HarmonyPatch(typeof(CardDisplayer), nameof(CardDisplayer.GetCostSpriteForCard))]
        [HarmonyPrefix]
        private static bool ClampCostSprite(CardDisplayer __instance, CardInfo card, ref Sprite __result)
        {
            if (card == null) return true;

            if (card.BonesCost > 0)
            {
                var bones = AccessTools.Field(typeof(CardDisplayer), "boneCostTextures")
                                       ?.GetValue(__instance) as Sprite[];
                if (bones == null || bones.Length == 0) return true;
                if (card.BonesCost - 1 < bones.Length) return true;   // in range, let it run

                __result = bones[bones.Length - 1];
                return false;
            }

            if (card.GemsCost != null && card.GemsCost.Count > 0) return true;   // guarded upstream
            if (card.EnergyCost > 0) return true;                                 // guarded upstream

            var blood = AccessTools.Field(typeof(CardDisplayer), "costTextures")
                                   ?.GetValue(__instance) as Sprite[];
            if (blood == null || blood.Length == 0) return true;
            if (card.BloodCost < blood.Length) return true;

            __result = blood[blood.Length - 1];
            return false;
        }
    }
}
