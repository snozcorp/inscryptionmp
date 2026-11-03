using System;
using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Sigils whose payoff goes to "the player" - a card in hand, a draw, bones, energy. The
    /// game never asks whose card triggered them, so on your screen the opponent's Bees
    /// Within put a bee in YOUR hand, while their own screen gave them one too. The owner's
    /// client already does it for real; here the opponent's copy stays quiet.
    /// </summary>
    [HarmonyPatch]
    internal static class OwnerOnly
    {
        /// <summary>Behaviours whose whole trigger is the owner's business.</summary>
        private static readonly HashSet<Type> Whole = new HashSet<Type>
        {
            typeof(DrawVesselOnHit),   // side pile draw
            typeof(GemsDraw),          // draws per gem
            typeof(Loot),              // draws per damage
            typeof(Tutor),             // picks from your deck
            typeof(DrawNewHand),       // discards your hand
            typeof(QuadrupleBones),    // four bones on death
            typeof(GainBattery),       // energy
            typeof(OpponentBones),     // bones when the other side's cards die
            typeof(BoneDigger),        // a bone at its owner's turn end
            typeof(Morsel),            // feeds CurrentSacrificeDemandingCard, null here
            typeof(RandomConsumable),  // an item
        };

        private static bool Applies => VersusMode.InMatch;

        private static bool IsTheirs(TriggerReceiver receiver)
        {
            var card = receiver != null ? receiver.GetComponent<PlayableCard>() : null;
            return card != null && card.OpponentCard;
        }

        /// <summary>Every card trigger asks this one method first.</summary>
        [HarmonyPatch(typeof(GlobalTriggerHandler), nameof(GlobalTriggerHandler.ReceiverRespondsToTrigger))]
        [HarmonyPostfix]
        private static void QuietForTheirCards(Trigger trigger, TriggerReceiver receiver, ref bool __result)
        {
            if (!__result || !Applies || receiver == null) return;
            if (!Whole.Contains(receiver.GetType()) || !IsTheirs(receiver)) return;

            __result = false;
            Trace.Info($"[owner] {receiver.GetType().Name} on their card - their client handles it ({trigger})");
        }

        /// <summary>
        /// Bees, rabbits, ants, Fecundity, Unkillable, Steel Trap's pelt and the rest. Only the
        /// card-to-hand step is skipped: Steel Trap still kills what's opposite on both screens.
        /// </summary>
        [HarmonyPatch(typeof(DrawCreatedCard), "CreateDrawnCard")]
        [HarmonyPrefix]
        private static bool NoCardForUs(DrawCreatedCard __instance, ref IEnumerator __result)
        {
            if (!Applies || !IsTheirs(__instance)) return true;

            Trace.Info($"[owner] {__instance.GetType().Name} on their card - no card for our hand");
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }
    }
}
