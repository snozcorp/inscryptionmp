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
        /// <summary>
        /// Mirrors a local sacrifice to the peer as it happens.
        ///
        /// Without this the opponent only learns at the bell, via the board snapshot, and
        /// the sacrificed cards simply blink out of existence - which reads as a glitch
        /// rather than as the other player paying a cost.
        /// </summary>
        [HarmonyPatch(typeof(PlayableCard), nameof(PlayableCard.Sacrifice))]
        [HarmonyPrefix]
        private static void OnLocalSacrifice(PlayableCard __instance)
        {
            if (!Net.Connected || !VersusMode.InMatch) return;
            if (__instance == null) return;

            CardSlot slot = __instance.Slot;
            if (slot == null || !slot.IsPlayerSlot) return;

            var tm = Singleton<TurnManager>.Instance;
            if (tm == null || !tm.IsPlayerTurn) return;

            Net.Send(Protocol.Sacrifice(slot.Index));
        }

        /// <summary>Card name in each of our player slots, or "-" for empty.</summary>
        private static string[] SnapshotPlayerSlots()
        {
            var board = Singleton<BoardManager>.Instance;
            var slots = board != null ? board.PlayerSlotsCopy : null;
            int count = slots != null ? slots.Count : 4;

            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                var card = slots[i] != null ? slots[i].Card : null;
                names[i] = (card != null && card.Info != null) ? card.Info.name : Protocol.EmptySlot;
            }
            Trace.Info("[sync] board snapshot: " + string.Join("|", names));
            return names;
        }

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

            // Send the authoritative state of our side before passing. Replaying
            // individual plays can't express sacrifices or deaths, so the peer's copy of
            // our board drifts; this reconciles it every turn.
            Net.Send(Protocol.Board(SnapshotPlayerSlots()));

            TurnOrder.PassedToPeer();
            Net.Send(Protocol.EndTurn);
        }
    }
}
