using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Captures LOCAL player actions and puts them on the wire.
    /// Peer cards land in opponent slots, so gating on IsPlayerSlot also prevents
    /// echoing back a play we just received.
    /// </summary>
    [HarmonyPatch]
    internal static class Sync
    {
        [HarmonyPatch(typeof(BoardManager), nameof(BoardManager.ResolveCardOnBoard))]
        [HarmonyPrefix]
        private static void OnLocalCardPlayed(PlayableCard card, CardSlot slot)
        {
            if (!Net.Connected) return;
            if (slot == null || card == null) return;
            if (!slot.IsPlayerSlot) return;

            var tm = Singleton<TurnManager>.Instance;
            if (tm == null || !tm.IsPlayerTurn) return;

            string cardName = card.Info != null ? card.Info.name : null;
            if (string.IsNullOrEmpty(cardName)) return;

            Net.Send(Protocol.Play(cardName, slot.Index));
        }

        [HarmonyPatch(typeof(TurnManager), nameof(TurnManager.OnCombatBellRang))]
        [HarmonyPostfix]
        private static void OnLocalTurnEnded()
        {
            if (!Net.Connected) return;
            TurnOrder.PassedToPeer();
            Net.Send(Protocol.EndTurn);
        }
    }
}
