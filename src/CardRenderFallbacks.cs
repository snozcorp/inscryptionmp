using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Lets cards from other acts draw on the Act 1 table.
    ///
    /// Two things break when a non-Act-1 card is rendered by CardDisplayer3D, and neither
    /// is fundamental - both are just missing art the renderer assumes is present:
    ///
    ///  - <c>GetCostSpriteForCard</c> indexes <c>boneCostTextures</c> and
    ///    <c>costTextures</c> without bounds checks. A Grimora card costing more bones
    ///    than Act 1's array has entries throws IndexOutOfRangeException. The gem and
    ///    energy branches are already guarded and fall through, so only these two throw.
    ///  - Cards that ship only a pixel portrait have a null <c>portraitTex</c>, so the
    ///    3D card draws with a blank face.
    ///
    /// Clamping the first and falling back for the second is enough to make those cards
    /// legible. The cost icon may be approximate on very expensive cards, which is a much
    /// better failure than a crash or an empty card.
    /// </summary>
    [HarmonyPatch]
    internal static class CardRenderFallbacks
    {
        [HarmonyPatch(typeof(CardDisplayer), nameof(CardDisplayer.GetCostSpriteForCard))]
        [HarmonyPrefix]
        private static bool ClampCostSprite(CardDisplayer __instance, CardInfo card, ref Sprite __result)
        {
            if (card == null) return true;

            Sprite[] bones = AccessTools.Field(typeof(CardDisplayer), "boneCostTextures")
                                        ?.GetValue(__instance) as Sprite[];
            Sprite[] blood = AccessTools.Field(typeof(CardDisplayer), "costTextures")
                                        ?.GetValue(__instance) as Sprite[];

            // Only step in when the original would go out of bounds.
            if (card.BonesCost > 0)
            {
                if (bones == null || bones.Length == 0) return true;
                if (card.BonesCost - 1 < bones.Length) return true;

                __result = bones[bones.Length - 1];
                return false;
            }

            if (card.GemsCost != null && card.GemsCost.Count > 0) return true;   // guarded upstream
            if (card.EnergyCost > 0) return true;                                 // guarded upstream

            if (blood == null || blood.Length == 0) return true;
            if (card.BloodCost < blood.Length) return true;

            __result = blood[blood.Length - 1];
            return false;
        }

        /// <summary>
        /// Gives pixel-only cards something to show. Low resolution on a 3D card, but
        /// legible, and Act 2's art is pixel art by design.
        /// </summary>
        [HarmonyPatch(typeof(Card), nameof(Card.SetInfo))]
        [HarmonyPrefix]
        private static void SubstitutePortrait(CardInfo info)
        {
            if (info == null) return;
            if (info.portraitTex != null) return;

            if (info.alternatePortrait != null) info.portraitTex = info.alternatePortrait;
            else if (info.pixelPortrait != null) info.portraitTex = info.pixelPortrait;
        }
    }
}
