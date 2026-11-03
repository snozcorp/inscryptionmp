using System.Collections;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// The Ijiraq hides as another card whenever it's shown off the board - in deck review
    /// as a random deck card, at a card choice as "Mole Man?". The deck builder stores
    /// whatever it is shown, so the deck ended up holding the disguise.
    /// </summary>
    [HarmonyPatch]
    internal static class IjiraqUndisguised
    {
        private static bool Applies => VersusMode.VersusContext || NativeDeckBuilder.IsOpen;

        [HarmonyPatch(typeof(Shapeshifter), nameof(Shapeshifter.OnShownInDeckReview))]
        [HarmonyPrefix]
        private static bool DeckReview() => !Applies;

        [HarmonyPatch(typeof(Shapeshifter), nameof(Shapeshifter.OnShownForCardChoiceNode))]
        [HarmonyPrefix]
        private static bool CardChoice() => !Applies;

        [HarmonyPatch(typeof(Shapeshifter), nameof(Shapeshifter.OnShownForCardSelect))]
        [HarmonyPrefix]
        private static bool CardSelect(ref IEnumerator __result)
        {
            if (!Applies) return true;
            __result = Nothing();
            return false;
        }

        [HarmonyPatch(typeof(Shapeshifter), nameof(Shapeshifter.OnSelectedForDeckTrial))]
        [HarmonyPrefix]
        private static bool DeckTrial(ref IEnumerator __result)
        {
            if (!Applies) return true;
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }

    /// <summary>
    /// Daus strikes back at whatever rings a bell - any bell, struck by anyone. Across a
    /// versus table that meant our Daus answered our own cards hitting their chimes.
    /// Only a bell on Daus's side, struck from the other side, counts.
    /// </summary>
    [HarmonyPatch]
    internal static class DausOwnBells
    {
        [HarmonyPatch(typeof(Daus), nameof(Daus.RespondsToOtherCardDealtDamage))]
        [HarmonyPostfix]
        private static void OwnSideOnly(Daus __instance, PlayableCard attacker, PlayableCard target,
                                        ref bool __result)
        {
            if (!__result || !VersusMode.InMatch || __instance == null) return;

            PlayableCard daus = __instance.GetComponent<PlayableCard>();
            if (daus == null || attacker == null || target == null) return;

            bool ownBell = target.OpponentCard == daus.OpponentCard;
            bool fromAcross = attacker.OpponentCard != daus.OpponentCard;
            if (ownBell && fromAcross) return;

            __result = false;
            Trace.Info($"[daus] ignoring {attacker.Info?.name} hitting {target.Info?.name} - not Daus's bell to answer");
        }
    }
}
