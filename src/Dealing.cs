using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using GBC;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// How many cards you start with, how many you take each turn, and what you start with
    /// in front of you. The opening hand is dealt by two different methods with the size
    /// written into each as a literal 4, so both are replaced rather than nudged.
    /// </summary>
    [HarmonyPatch]
    internal static class Dealing
    {
        private static bool Ours => VersusMode.InMatch;

        private static Rules Now => MatchRules.Live;

        // ------------------------------------------------------------- how many a turn

        [HarmonyPatch(typeof(CardDrawPiles), "NumDrawPhaseDraws", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool DrawsPerTurnFlat(ref int __result)
        {
            if (!Ours) return true;
            __result = Now.DrawsPerTurn;
            return false;
        }

        /// <summary>
        /// The 3D piles override the base getter, so patching the base alone would leave
        /// Acts 1 and 3 on the game's own number.
        /// </summary>
        [HarmonyPatch(typeof(CardDrawPiles3D), "NumDrawPhaseDraws", MethodType.Getter)]
        [HarmonyPrefix]
        private static bool DrawsPerTurn3D(ref int __result)
        {
            if (!Ours) return true;
            __result = Now.DrawsPerTurn;
            return false;
        }

        // -------------------------------------------------------------- the opening hand

        [HarmonyPatch(typeof(CardDrawPiles3D), nameof(CardDrawPiles3D.DrawOpeningHand))]
        [HarmonyPrefix]
        private static bool OpeningHand3D(CardDrawPiles3D __instance, List<CardInfo> fixedHand,
                                          ref IEnumerator __result)
        {
            if (!Ours) return true;
            __result = Deal3D(__instance, fixedHand);
            return false;
        }

        private static IEnumerator Deal3D(CardDrawPiles3D piles, List<CardInfo> fixedHand)
        {
            var dealt = new List<CardInfo>();
            var pile = AccessTools.Field(typeof(CardDrawPiles3D), "pile")?.GetValue(piles) as CardPile;
            var sidePile = AccessTools.Field(typeof(CardDrawPiles3D), "sidePile")?.GetValue(piles) as CardPile;

            // The side pile's card is the first one you are given, and it counts against
            // the hand size - which is why "no side pile" is one more card off the deck
            // rather than one card fewer in hand.
            if (piles.SideDeck != null && piles.SideDeck.CardsInDeck > 0)
            {
                if (sidePile != null) sidePile.Draw();
                yield return piles.DrawFromSidePile();

                var hand = Singleton<PlayerHand>.Instance;
                if (hand != null && hand.CardsInHand.Count > 0)
                    dealt.Add(hand.CardsInHand[0].Info);
            }

            if (fixedHand != null) dealt.AddRange(fixedHand.FindAll(x => x != null));

            int want = Now.StartingHand;
            Trace.Info($"[deal] opening hand of {want}, {dealt.Count} of it from the side pile");

            List<CardInfo> fair = piles.Deck.GetFairHand(want, false, dealt);
            foreach (CardInfo card in fair)
            {
                if (!piles.Deck.HasCard(card)) continue;
                if (pile != null) pile.Draw();
                yield return piles.DrawCardFromDeck(card);
            }
        }

        [HarmonyPatch(typeof(PixelCardDrawPiles), nameof(PixelCardDrawPiles.DrawOpeningHand))]
        [HarmonyPrefix]
        private static bool OpeningHandPixel(PixelCardDrawPiles __instance, List<CardInfo> fixedHand,
                                             ref IEnumerator __result)
        {
            if (!Ours) return true;
            __result = DealPixel(__instance, fixedHand);
            return false;
        }

        private static IEnumerator DealPixel(PixelCardDrawPiles piles, List<CardInfo> fixedHand)
        {
            int want = Now.StartingHand;
            List<CardInfo> hand = (fixedHand == null || fixedHand.Count == 0)
                ? piles.Deck.GetFairHand(want)
                : fixedHand;

            Trace.Info($"[deal] GBC opening hand of {want}");

            for (int i = 0; i < want; i++)
            {
                if (i < hand.Count) yield return piles.DrawCardFromDeck(hand[i]);
                else yield return piles.DrawCardFromDeck();
            }
        }

        // ------------------------------------------------------------ what you start with

        /// <summary>
        /// Bones on the table before the first card. Hung off the setup phase rather than
        /// the resources manager because each act has its own of those, and this one place
        /// runs for all three.
        /// </summary>
        [HarmonyPatch(typeof(TurnManager), "SetupPhase")]
        [HarmonyPostfix]
        private static IEnumerator StartingResources(IEnumerator __result)
        {
            while (__result.MoveNext()) yield return __result.Current;

            if (!Ours) yield break;

            int bones = Now.StartingBones;
            if (bones <= 0) yield break;

            var res = Singleton<ResourcesManager>.Instance;
            if (res == null)
            {
                Trace.Warn("[deal] no resources manager - starting bones not given");
                yield break;
            }

            Trace.Info($"[deal] starting the match with {bones} bone(s)");
            yield return res.AddBones(bones);
        }
    }
}
