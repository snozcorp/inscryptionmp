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

            while (true)
            {
                if (Net.TryDequeue(out string msg))
                {
                    if (msg == Protocol.EndTurn)
                    {
                        Trace.Info("[opp] peer ended turn.");
                        yield break;
                    }

                    if (Protocol.TryParsePlay(msg, out string cardName, out int slotIndex))
                    {
                        yield return PlacePeerCard(cardName, slotIndex);
                    }
                }

                if (!Net.Connected)
                {
                    Trace.Warn("[opp] peer disconnected; ending turn.");
                    yield break;
                }

                yield return new WaitForEndOfFrame();
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
                Trace.Warn($"[opp] slot {slotIndex} occupied - skipping");
                yield break;
            }

            Trace.Info($"[opp] placing peer card '{cardName}' in opponent slot {slotIndex}");
            yield return board.CreateCardInSlot(info, slot);
            yield return new WaitForSeconds(0.15f);
        }

        // No AI, no scripted blueprint: nothing to queue ahead of the player.
        public override bool QueueFirstCardBeforePlayer => false;
    }
}
