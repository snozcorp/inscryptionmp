using System.Collections.Generic;
using DiskCardGame;
using GBC;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>Stops an Act 2 card losing all of its sigils when it has too many.</summary>
    [HarmonyPatch]
    internal static class PixelSigilOverflow
    {
        private static bool _reported;

        // Named by parameter types: DisplayAbilities is overloaded, and matching on name
        // alone throws AmbiguousMatchException while patching - which aborts PatchAll and
        // silently leaves the entire mod unpatched.
        [HarmonyPatch(typeof(PixelCardAbilityIcons), nameof(PixelCardAbilityIcons.DisplayAbilities),
                      new[] { typeof(List<Ability>), typeof(PlayableCard) })]
        [HarmonyPrefix]
        private static void ClampToAvailableLayouts(PixelCardAbilityIcons __instance,
                                                    ref List<Ability> abilities)
        {
            if (abilities == null || abilities.Count == 0) return;

            var groups = AccessTools.Field(typeof(PixelCardAbilityIcons), "abilityIconGroups")
                                    ?.GetValue(__instance) as List<GameObject>;
            if (groups == null || groups.Count == 0) return;

            if (!_reported)
            {
                _reported = true;
                Trace.Info($"[act2] the pixel card can lay out at most {groups.Count} sigil(s)");
            }

            if (abilities.Count <= groups.Count) return;

            // A copy: this list belongs to the card, and trimming it in place would take
            // the abilities away from the card itself, not just from its icons.
            var shown = abilities.GetRange(0, groups.Count);

            Trace.Warn($"[act2] card has {abilities.Count} sigils but only {groups.Count} fit - " +
                       $"showing {string.Join(", ", shown)}");

            abilities = shown;
        }
    }
}
