using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>Makes the rulebook answer for cards from every act.</summary>
    [HarmonyPatch]
    internal static class RuleBookFallbacks
    {
        private static readonly AbilityMetaCategory[] RulebookCategories =
        {
            AbilityMetaCategory.Part1Rulebook,
            AbilityMetaCategory.Part3Rulebook,
            AbilityMetaCategory.GrimoraRulebook,
            AbilityMetaCategory.MagnificusRulebook,
        };

        [HarmonyPatch(typeof(RuleBookInfo), "AbilityShouldBeAdded")]
        [HarmonyPostfix]
        private static void WidenAbilities(int abilityIndex, ref bool __result)
        {
            if (__result || !VersusMode.VersusContext) return;

            AbilityInfo info = AbilitiesUtil.GetInfo((Ability)abilityIndex);
            if (info == null || info.metaCategories == null) return;

            foreach (AbilityMetaCategory cat in RulebookCategories)
                if (info.metaCategories.Contains(cat)) { __result = true; return; }

            // Some abilities sit in no rulebook category at all yet still carry an entry -
            // ActivatedEnergyToBones is one. If the game can describe it, it gets a page.
            if (!string.IsNullOrEmpty(info.rulebookName)) __result = true;
        }

        [HarmonyPatch(typeof(RuleBookInfo), "StatIconShouldBeAdded")]
        [HarmonyPostfix]
        private static void WidenStatIcons(int iconIndex, ref bool __result)
        {
            if (__result || !VersusMode.VersusContext) return;

            StatIconInfo info = StatIconInfo.GetIconInfo((SpecialStatIcon)iconIndex);
            if (info == null || info.metaCategories == null) return;

            foreach (AbilityMetaCategory cat in RulebookCategories)
                if (info.metaCategories.Contains(cat)) { __result = true; return; }
        }

        /// <summary>
        /// Insurance for anything still missing a page: stay put rather than flipping to
        /// index -1 and showing an unrelated entry as though it were the answer.
        /// </summary>
        [HarmonyPatch(typeof(RuleBookController), nameof(RuleBookController.OpenToAbilityPage))]
        [HarmonyPrefix]
        private static bool GuardMissingPage(RuleBookController __instance, string abilityName)
        {
            if (!VersusMode.VersusContext) return true;

            var pages = AccessTools.Property(typeof(RuleBookController), "PageData")
                                   ?.GetValue(__instance) as List<RuleBookPageInfo>;
            if (pages == null) return true;

            if (pages.Exists(x => x.abilityPage && x.pageId == abilityName)) return true;

            Trace.Warn($"[rulebook] no page for '{abilityName}' - not flipping to a wrong one");
            return false;
        }
    }
}
