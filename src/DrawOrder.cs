using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Deals off the top of the deck instead of at random, when the match is being played
    /// under that rule. Everything else about drawing is the game's own.
    /// </summary>
    [HarmonyPatch]
    internal static class DrawOrder
    {
        /// <summary>Whether the running match deals in deck order.</summary>
        private static bool InOrder =>
            VersusMode.InMatch && MatchRules.Live.Draw == CardDraw.InOrder;

        /// <summary>
        /// Both Draw() overloads land here - the no-argument one calls this with null - so
        /// one patch covers every draw. The parameter types are spelled out because
        /// patching an overload by name alone throws during PatchAll and takes every other
        /// patch in the mod down with it.
        /// </summary>
        [HarmonyPatch(typeof(Deck), nameof(Deck.Draw), new[] { typeof(CardInfo) })]
        [HarmonyPrefix]
        private static bool TopOfDeck(Deck __instance, CardInfo specificCard, ref CardInfo __result)
        {
            if (!InOrder) return true;

            List<CardInfo> cards = __instance.Cards;
            if (cards == null || cards.Count == 0) return true;

            // Someone asked for a named card - the opening hand, a tutor, a sigil that
            // fetches. That is not a random draw and the rule has nothing to say about it.
            if (specificCard != null && cards.Contains(specificCard)) return true;

            __result = cards[0];
            cards.RemoveAt(0);
            Trace.Info($"[draw] top of deck: {__result.name} ({cards.Count} left)");
            return false;
        }

        /// <summary>
        /// The opening hand. Vanilla picks it with an eye on playability; in order it is
        /// simply the top of the deck, which is the whole point of the rule.
        /// </summary>
        [HarmonyPatch(typeof(Deck), nameof(Deck.GetFairHand),
                      new[] { typeof(int), typeof(bool), typeof(List<CardInfo>) })]
        [HarmonyPrefix]
        private static bool OpeningHandInOrder(Deck __instance, int numCards,
                                               List<CardInfo> existingHand,
                                               ref List<CardInfo> __result)
        {
            if (!InOrder) return true;

            var hand = new List<CardInfo>();
            if (existingHand != null) hand.AddRange(existingHand);

            // The caller draws these by name afterwards, so this only decides which cards
            // and in what order - it must not take them out of the deck itself.
            foreach (CardInfo card in __instance.Cards)
            {
                if (hand.Count >= numCards) break;
                if (existingHand != null && existingHand.Contains(card)) continue;
                hand.Add(card);
            }

            __result = hand;
            Trace.Info("[draw] opening hand in deck order: " + Describe(hand));
            return false;
        }

        internal static string Describe(IList<CardInfo> cards)
        {
            if (cards == null || cards.Count == 0) return "(none)";

            var names = new string[cards.Count];
            for (int i = 0; i < cards.Count; i++)
                names[i] = cards[i] == null ? "?" : cards[i].name;
            return string.Join(", ", names);
        }
    }
}
