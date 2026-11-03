using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// The peer's hand size, bones and sacrifices this turn. Cards whose power counts those
    /// (Hand Tentacle, Lammergeier, the sacrifice counter) asked the game, which only knows
    /// ours - so the opponent's copy showed, and mirrored, the wrong number.
    /// </summary>
    [HarmonyPatch]
    internal static class PeerState
    {
        public static bool Known { get; private set; }
        public static int Hand { get; private set; }
        public static int Bones { get; private set; }
        public static int Sacrifices { get; private set; }

        public static void Reset()
        {
            Known = false;
            Hand = Bones = Sacrifices = 0;
        }

        public static void Note(int hand, int bones, int sacrifices)
        {
            Known = true;
            Hand = hand;
            Bones = bones;
            Sacrifices = sacrifices;
            Trace.Info($"[peer] hand {hand}, bones {bones}, sacrifices this turn {sacrifices}");
        }

        /// <summary>Their upkeep has just run on our screen, which resets their counter.</summary>
        public static void TheirTurnBegins() => Sacrifices = 0;

        public static void NoteSacrifice() => Sacrifices++;

        /// <summary>Ours, for the STATE line we send with our board.</summary>
        public static string OurLine()
        {
            var hand = Singleton<PlayerHand>.Instance;
            var res = Singleton<ResourcesManager>.Instance;
            var board = Singleton<BoardManager>.Instance;
            return Protocol.State(hand != null && hand.CardsInHand != null ? hand.CardsInHand.Count : 0,
                                  res != null ? res.PlayerBones : 0,
                                  board != null ? board.SacrificesMadeThisTurn : 0);
        }

        private static bool Theirs(Component behaviour)
        {
            if (!VersusMode.InMatch || !Known || behaviour == null) return false;
            var card = behaviour.GetComponent<PlayableCard>();
            return card != null && card.OpponentCard;
        }

        /// <summary>
        /// A card died on our screen; their screen paid them bones for it. Their own card
        /// pays 1 (4 with QuadrupleBones), ours pays 1 per OpponentBones card they have out.
        /// </summary>
        [HarmonyPatch(typeof(PlayableCard), nameof(PlayableCard.Die))]
        [HarmonyPrefix]
        private static void TheirBonesForADeath(PlayableCard __instance)
        {
            if (!VersusMode.InMatch || __instance == null || __instance.Dead || __instance.Slot == null) return;

            if (__instance.OpponentCard)
            {
                if (__instance.InOpponentQueue) return;
                Bones += __instance.HasAbility(Ability.QuadrupleBones) ? 4 : 1;
                return;
            }

            var board = Singleton<BoardManager>.Instance;
            if (board == null) return;
            foreach (CardSlot slot in board.OpponentSlotsCopy)
                if (slot.Card != null && !slot.Card.Dead && slot.Card.HasAbility(Ability.OpponentBones))
                    Bones++;
        }

        [HarmonyPatch(typeof(CardsInHand), "GetStatValues")]
        [HarmonyPostfix]
        private static void TheirHand(CardsInHand __instance, ref int[] __result)
        {
            if (Theirs(__instance)) __result = new[] { Hand, 0 };
        }

        [HarmonyPatch(typeof(Lammergeier), "GetStatValues")]
        [HarmonyPostfix]
        private static void TheirBones(Lammergeier __instance, ref int[] __result)
        {
            if (Theirs(__instance)) __result = new[] { Mathf.FloorToInt(Bones / 2f), 0 };
        }

        [HarmonyPatch(typeof(SacrificesThisTurn), "GetStatValues")]
        [HarmonyPostfix]
        private static void TheirSacrifices(SacrificesThisTurn __instance, ref int[] __result)
        {
            if (Theirs(__instance)) __result = new[] { Sacrifices, 0 };
        }

        /// <summary>The Bell counts chimes beside it, on whichever side it actually sits.</summary>
        [HarmonyPatch(typeof(BellProximity), "GetStatValues")]
        [HarmonyPostfix]
        private static void BellOnItsOwnSide(BellProximity __instance, ref int[] __result)
        {
            if (!VersusMode.InMatch || __instance == null) return;
            var card = __instance.GetComponent<PlayableCard>();
            var board = Singleton<BoardManager>.Instance;
            if (card == null || !card.OpponentCard || card.Slot == null || board == null) return;

            List<CardSlot> slots = board.OpponentSlotsCopy;
            int index = card.Slot.Index;
            int chimes = 0;
            // Same bounds as the game's own, off-by-one on the right included, so both agree.
            if (index > 0 && IsChime(slots[index - 1].Card)) chimes++;
            if (index < slots.Count - 2 && IsChime(slots[index + 1].Card)) chimes++;
            __result = new[] { slots.Count - index + chimes, 0 };
        }

        private static bool IsChime(PlayableCard card) =>
            card != null && card.Info != null && card.Info.name.Equals("DausBell");
    }
}
