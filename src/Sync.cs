using System.Collections.Generic;
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

        /// <summary>Exposed so a reconnect can re-assert our board.</summary>
        internal static string[] SnapshotPlayerSlotsPublic() => SnapshotPlayerSlots();

        /// <summary>
        /// What each of our player slots is showing: card and its current stats, or "-".
        /// The peer applies these verbatim, so this is the sender's own screen as truth.
        /// </summary>
        private static string[] SnapshotPlayerSlots()
        {
            var board = Singleton<BoardManager>.Instance;
            var slots = board != null ? board.PlayerSlotsCopy : null;
            int count = slots != null ? slots.Count : 4;

            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                var card = slots[i] != null ? slots[i].Card : null;
                if (card == null || card.Info == null)
                {
                    names[i] = Protocol.EmptySlot;
                    continue;
                }

                // A protocol 2 peer parses the whole token as a card name, so sending it
                // stats would leave it unable to resolve anything on our board.
                names[i] = Net.PeerSpeaksV3
                    ? Protocol.EncodeSlot(card.Info.name, card.Attack, card.Health, GainedSigils(card))
                    : card.Info.name;
            }
            Trace.Info("[sync] board snapshot: " + string.Join("|", names));
            return names;
        }

        /// <summary>
        /// Abilities this card has gained during the match. Temporary mods are where a
        /// totem buff, a latch or an evolution lands, so they are exactly the difference
        /// between our card and the same card on the peer's screen.
        /// </summary>
        private static string[] GainedSigils(PlayableCard card)
        {
            var gained = new List<Ability>();

            // Picked up during the match: totems, latches, evolution.
            if (card.TemporaryMods != null && card.TemporaryMods.Count > 0)
            {
                var fromMods = AbilitiesUtil.GetAbilitiesFromMods(card.TemporaryMods);
                if (fromMods != null) gained.AddRange(fromMods);
            }

            // Chosen in the deck builder. These live on the card's own Mods because that
            // is what makes the game treat them as real abilities, but the peer resolves
            // our card by name and gets the plain version - so they have to travel too.
            if (card.Info?.Mods != null && card.Info.Mods.Count > 0)
            {
                var fromCard = AbilitiesUtil.GetAbilitiesFromMods(card.Info.Mods);
                if (fromCard != null)
                    foreach (Ability a in fromCard)
                        if (!gained.Contains(a)) gained.Add(a);
            }

            if (gained.Count == 0) return null;

            var names = new string[gained.Count];
            for (int i = 0; i < gained.Count; i++) names[i] = gained[i].ToString();
            return names;
        }

        [HarmonyPatch(typeof(BoardManager), nameof(BoardManager.ResolveCardOnBoard))]
        [HarmonyPrefix]
        private static void OnLocalCardPlayed(PlayableCard card, CardSlot slot)
        {
            if (!Net.Connected || !VersusMode.InMatch) return;
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
            if (!Net.Connected || !VersusMode.InMatch) return;

            // Send the authoritative state of our side before passing. Replaying
            // individual plays can't express sacrifices or deaths, so the peer's copy of
            // our board drifts; this reconciles it every turn.
            Net.Send(Protocol.Board(SnapshotPlayerSlots()));

            TurnOrder.PassedToPeer();
            Net.Send(Protocol.EndTurn);
        }
    }
}
