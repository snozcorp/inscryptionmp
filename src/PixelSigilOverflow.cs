using System.Collections.Generic;
using DiskCardGame;
using GBC;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Stops an Act 2 card losing all of its sigils when it has too many.
    ///
    /// PixelCardAbilityIcons keeps one prebuilt icon layout per sigil count - one group for
    /// a card with one sigil, another for two, and so on. It picks the group by index:
    ///
    ///     if (abilities.Count > 0 &amp;&amp; abilities.Count - 1 &lt; abilityIconGroups.Count)
    ///
    /// A card with more sigils than there are layouts fails that test, and the whole block
    /// is skipped - so instead of dropping the extra icons it draws none at all. The card
    /// silently loses every sigil it has.
    ///
    /// Vanilla never hits this because its own cards are authored within the limit. We can
    /// exceed it: a card that gains a sigil mid-match, or one synced from a peer, adds to
    /// whatever it already had. Showing as many as the layouts allow beats showing none.
    /// </summary>
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
