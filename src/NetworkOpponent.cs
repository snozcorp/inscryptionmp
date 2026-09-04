using System;
using System.Collections;
using System.Collections.Generic;
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

                    if (Protocol.TryParseSacrifice(msg, out int sacSlot))
                    {
                        yield return SacrificePeerCard(sacSlot);
                        continue;
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

                if (!VersusMode.InMatch)
                {
                    Trace.Info("[opp] match ended while waiting");
                    yield break;
                }

                // A drop no longer ends the turn: VersusMode suspends the match and the
                // transport reconnects underneath us, so just keep waiting.
                if (!Net.Connected) VersusMode.NoteDisconnected();

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
                Protocol.SlotState wanted = Protocol.DecodeSlot(slotNames[i]);
                CardSlot slot = slots[i];
                string actual = (slot.Card != null && slot.Card.Info != null)
                    ? slot.Card.Info.name
                    : Protocol.EmptySlot;

                bool sameCard = wanted.Name == actual;

                if (!sameCard)
                {
                    if (slot.Card != null)
                    {
                        Trace.Info($"[opp] reconcile: clearing slot {i} ({actual})");
                        yield return RemoveCardAnimated(slot);
                    }

                    if (!wanted.IsEmpty)
                    {
                        CardInfo info = CardLoader.GetCardByName(wanted.Name);
                        if (info == null)
                        {
                            Trace.Error($"[opp] reconcile: unknown card '{wanted.Name}'");
                            continue;
                        }
                        Trace.Info($"[opp] reconcile: slot {i} -> {wanted.Name}");
                        yield return board.CreateCardInSlot(info, slot);
                        yield return new WaitForSeconds(0.05f);
                    }
                }

                // Same card, or one we just placed: make its numbers match theirs.
                if (!wanted.IsEmpty && slot.Card != null) MatchStats(slot.Card, wanted, i);
            }
        }

        /// <summary>Singleton id so repeated corrections replace rather than pile up.</summary>
        private const string SyncModId = "inscryptionmp-sync";

        /// <summary>
        /// Nudges our copy of a peer card until it shows the stats they report.
        ///
        /// The adjustment is a delta against what the card currently shows, folded into a
        /// single temporary mod. AddTemporaryMod replaces by singletonId, so this stays one
        /// mod per card however many times it is corrected. The peer's screen is the
        /// authority: whatever their card reads, ours is made to read the same.
        /// </summary>
        private void MatchStats(PlayableCard card, Protocol.SlotState wanted, int slotIndex)
        {
            // Nothing to match against: an older peer sends names only, and treating the
            // absent numbers as zero would wipe out its board.
            if (!wanted.HasStats) return;

            int attackDelta = wanted.Attack - card.Attack;
            int healthDelta = wanted.Health - card.Health;
            List<Ability> missing = MissingSigils(card, wanted.Sigils);

            if (attackDelta == 0 && healthDelta == 0 && missing.Count == 0) return;

            CardModificationInfo mod = card.TemporaryMods?.Find(m => m.singletonId == SyncModId);
            if (mod == null)
            {
                mod = new CardModificationInfo { singletonId = SyncModId };
            }

            mod.attackAdjustment += attackDelta;
            mod.healthAdjustment += healthDelta;
            foreach (Ability ability in missing) mod.abilities.Add(ability);

            card.AddTemporaryMod(mod);
            card.OnStatsChanged();   // also re-renders, so new sigil icons appear

            string sigilNote = missing.Count == 0 ? "" : ", gained " + string.Join(", ", missing);
            Trace.Info($"[opp] slot {slotIndex} {wanted.Name}: corrected to {wanted.Attack}/{wanted.Health} " +
                       $"(atk {attackDelta:+#;-#;0}, hp {healthDelta:+#;-#;0}{sigilNote})");
        }

        /// <summary>
        /// Sigils the peer's card has that ours doesn't yet. Reads back through the mods
        /// we've already applied, so a sigil is never added twice, and skips names this
        /// build doesn't know rather than throwing on a peer running something newer.
        /// </summary>
        private static List<Ability> MissingSigils(PlayableCard card, string[] wanted)
        {
            var missing = new List<Ability>();
            if (wanted == null || wanted.Length == 0) return missing;

            List<Ability> have = card.TemporaryMods == null
                ? new List<Ability>()
                : AbilitiesUtil.GetAbilitiesFromMods(card.TemporaryMods) ?? new List<Ability>();

            foreach (string name in wanted)
            {
                if (!Enum.IsDefined(typeof(Ability), name))
                {
                    Trace.Warn($"[opp] unknown sigil '{name}' from peer - ignoring");
                    continue;
                }

                var ability = (Ability)Enum.Parse(typeof(Ability), name);
                if (have.Contains(ability) || missing.Contains(ability)) continue;
                missing.Add(ability);
            }
            return missing;
        }

        /// <summary>Plays the peer's sacrifice with the game's own animation.</summary>
        private IEnumerator SacrificePeerCard(int slotIndex)
        {
            var board = Singleton<BoardManager>.Instance;
            if (board == null) yield break;

            var slots = board.OpponentSlotsCopy;
            if (slotIndex < 0 || slotIndex >= slots.Count) yield break;

            PlayableCard card = slots[slotIndex].Card;
            if (card == null)
            {
                Trace.Warn($"[opp] sacrifice: slot {slotIndex} already empty");
                yield break;
            }

            Trace.Info($"[opp] peer sacrificed slot {slotIndex}");
            yield return card.Die(wasSacrifice: true);
        }

        private static IEnumerator RemoveCardAnimated(CardSlot slot)
        {
            PlayableCard card = slot.Card;
            if (card == null) yield break;
            yield return card.Die(wasSacrifice: false, null, false);
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
