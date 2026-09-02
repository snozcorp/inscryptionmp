using System.Collections;
using DiskCardGame;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// An Opponent whose plays come from the network instead of the AI.
    ///
    /// Core idea: each client runs an ordinary single-player battle. You are always
    /// "the player" on your own screen; your peer is always "the opponent". A card the
    /// peer plays into THEIR player slot N appears in OUR opponent slot N. The engine's
    /// existing combat resolution is untouched.
    /// </summary>
    public class NetworkOpponent : Opponent
    {
        protected override string BlueprintSubfolderName => "";

        /// <summary>
        /// Runs during our OpponentTurn. Drains network messages, materialising the
        /// peer's plays onto our opponent side, until they signal end of turn.
        /// </summary>
        public override IEnumerator QueueNewCards(bool doTween = true, bool changeView = true)
        {
            Trace.Info("[opp] waiting for peer's turn...");

            // OpponentTurn locks the view because a vanilla opponent takes a second or two.
            // Ours can block indefinitely on the network, so leave the player free to look
            // around rather than freezing them in whatever view they happened to be in.
            var views = Singleton<ViewManager>.Instance;
            ViewLockState previousLock = ViewLockState.Locked;
            if (views != null)
            {
                previousLock = views.Controller.LockState;
                views.SwitchToView(View.Default);
                views.Controller.LockState = ViewLockState.Unlocked;
            }

            try
            {
            while (true)
            {
                if (Net.TryDequeue(out string msg))
                {
                    if (msg == Protocol.EndTurn)
                    {
                        Trace.Info("[opp] peer ended turn.");
                        TurnOrder.TakenFromPeer();
                        yield break;
                    }

                    if (msg == Protocol.Won || msg == Protocol.Lost)
                    {
                        // Peer is reporting the outcome from their side; ours is the inverse.
                        VersusMode.Finish(this, msg == Protocol.Lost, "peer reported result");
                        yield break;
                    }

                    if (Protocol.TryParseBoard(msg, out string[] slotNames))
                    {
                        yield return ReconcileBoard(slotNames);
                        continue;
                    }

                    if (Protocol.TryParsePlay(msg, out string cardName, out int slotIndex))
                    {
                        yield return PlacePeerCard(cardName, slotIndex);
                    }
                }

                if (!Net.Connected)
                {
                    Trace.Warn("[opp] peer disconnected mid-turn - ending match");
                    VersusMode.Finish(this, playerWon: true, reason: "peer disconnected");
                    yield break;
                }

                yield return new WaitForEndOfFrame();
            }
            }
            finally
            {
                if (views != null) views.Controller.LockState = previousLock;
            }
        }

        private IEnumerator PlacePeerCard(string cardName, int slotIndex)
        {
            CardInfo info = CardLoader.GetCardByName(cardName);
            if (info == null)
            {
                Trace.Error($"[opp] unknown card '{cardName}' - skipping");
                yield break;
            }

            var board = Singleton<BoardManager>.Instance;
            var slots = board.OpponentSlotsCopy;
            if (slotIndex < 0 || slotIndex >= slots.Count)
            {
                Trace.Error($"[opp] slot {slotIndex} out of range - skipping");
                yield break;
            }

            CardSlot slot = slots[slotIndex];
            if (slot.Card != null)
            {
                // Almost always a sacrifice: they consumed what was here and played over
                // it. Their side is authoritative, so replace rather than drop the play.
                Trace.Info($"[opp] slot {slotIndex} occupied - replacing");
                RemoveCard(slot);
            }

            Trace.Info($"[opp] placing peer card '{cardName}' in opponent slot {slotIndex}");
            yield return board.CreateCardInSlot(info, slot);
            yield return new WaitForSeconds(0.15f);
        }

        /// <summary>
        /// Makes our copy of the peer's side match the snapshot they sent.
        ///
        /// Individual plays can't express sacrifices or combat deaths, so replaying them
        /// lets the board drift apart permanently. Reconciling against their own view of
        /// their board each turn keeps the two clients honest.
        /// </summary>
        private IEnumerator ReconcileBoard(string[] slotNames)
        {
            var board = Singleton<BoardManager>.Instance;
            if (board == null) yield break;

            var slots = board.OpponentSlotsCopy;
            int count = Mathf.Min(slotNames.Length, slots.Count);

            for (int i = 0; i < count; i++)
            {
                string wanted = slotNames[i];
                CardSlot slot = slots[i];
                string actual = (slot.Card != null && slot.Card.Info != null)
                    ? slot.Card.Info.name
                    : Protocol.EmptySlot;

                if (wanted == actual) continue;

                if (slot.Card != null)
                {
                    Trace.Info($"[opp] reconcile: clearing slot {i} ({actual})");
                    RemoveCard(slot);
                    yield return new WaitForSeconds(0.05f);
                }

                if (wanted != Protocol.EmptySlot)
                {
                    CardInfo info = CardLoader.GetCardByName(wanted);
                    if (info == null)
                    {
                        Trace.Error($"[opp] reconcile: unknown card '{wanted}'");
                        continue;
                    }
                    Trace.Info($"[opp] reconcile: slot {i} -> {wanted}");
                    yield return board.CreateCardInSlot(info, slot);
                    yield return new WaitForSeconds(0.05f);
                }
            }
        }

        private static void RemoveCard(CardSlot slot)
        {
            PlayableCard card = slot.Card;
            if (card == null) return;
            card.UnassignFromSlot();
            card.ExitBoard(0.2f, Vector3.zero);
        }

        // No AI, no scripted blueprint: nothing to queue ahead of the player.
        public override bool QueueFirstCardBeforePlayer => false;
    }
}
