using System.Collections;
using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>Makes Sniper work when the card belongs to the other player.</summary>
    [HarmonyPatch]
    internal static class SniperSync
    {
        /// <summary>How long to wait for the peer's aim before falling back.</summary>
        private const float AimTimeout = 30f;

        [HarmonyPatch(typeof(CombatPhaseManager), "SlotAttackSequence")]
        [HarmonyPrefix]
        private static bool ReplaceForSniper(CombatPhaseManager __instance, CardSlot slot, ref IEnumerator __result)
        {
            if (!VersusMode.InMatch) return true;                       // campaign: untouched
            if (slot == null || slot.Card == null) return true;
            if (!slot.Card.HasAbility(Ability.Sniper)) return true;     // deterministic, leave alone

            __result = SniperAttack(__instance, slot);
            return false;
        }

        private static IEnumerator SniperAttack(CombatPhaseManager combat, CardSlot slot)
        {
            PlayableCard card = slot.Card;
            var board = Singleton<BoardManager>.Instance;
            var views = Singleton<ViewManager>.Instance;

            // An opponent card strikes our side, and ours strikes theirs. The engine's own
            // GetOpposingSlots makes this distinction; only its Sniper branch forgets it.
            List<CardSlot> targetSide = card.OpponentCard ? board.PlayerSlotsCopy : board.OpponentSlotsCopy;

            int numAttacks = card.HasTriStrike() ? 3
                           : card.HasAbility(Ability.SplitStrike) ? 2
                           : 1;

            var chosen = new List<CardSlot>();

            if (card.OpponentCard)
            {
                if (Net.PeerSpeaksV3)
                {
                    yield return WaitForPeerAim(slot.Index, numAttacks, targetSide, chosen);
                }
                else
                {
                    // An older peer will never tell us where it aimed, so don't stall the
                    // match waiting. Their card hits the slot opposite, which is at least
                    // deterministic and matches what their own screen shows most often.
                    Trace.Info("[sniper] peer is on an older protocol - using the slot opposite");
                    if (slot.Index >= 0 && slot.Index < targetSide.Count) chosen.Add(targetSide[slot.Index]);
                }
            }
            else
            {
                yield return AimLocally(combat, slot, targetSide, numAttacks, chosen);

                if (Net.PeerSpeaksV3)
                {
                    var indices = new int[chosen.Count];
                    for (int i = 0; i < chosen.Count; i++) indices[i] = targetSide.IndexOf(chosen[i]);
                    Net.Send(Protocol.Aim(slot.Index, indices));
                }
            }

            if (views != null) views.SwitchToView(board.CombatView);
            foreach (CardSlot target in chosen)
                yield return combat.SlotAttackSlot(slot, target, chosen.Count > 1 ? 0.1f : 0f);
        }

        /// <summary>Vanilla's prompt, aimed at whichever side this card actually attacks.</summary>
        private static IEnumerator AimLocally(CombatPhaseManager combat, CardSlot slot,
                                              List<CardSlot> targetSide, int numAttacks,
                                              List<CardSlot> chosen)
        {
            var board = Singleton<BoardManager>.Instance;
            var views = Singleton<ViewManager>.Instance;

            if (views != null)
            {
                views.SwitchToView(board.CombatView);
                views.Controller.SwitchToControlMode(board.ChoosingSlotViewMode);
                views.Controller.LockState = ViewLockState.Unlocked;
            }

            for (int i = 0; i < numAttacks; i++)
            {
                yield return board.ChooseTarget(
                    targetSide, targetSide,
                    s => chosen.Add(s),
                    null, null, () => false, CursorType.Target);
            }

            if (views != null)
            {
                views.Controller.SwitchToControlMode(board.DefaultViewMode);
                views.Controller.LockState = ViewLockState.Locked;
            }
        }

        /// <summary>Waits for the owning client to say where it aimed.</summary>
        private static IEnumerator WaitForPeerAim(int attackerSlot, int numAttacks,
                                                  List<CardSlot> targetSide, List<CardSlot> chosen)
        {
            Trace.Info($"[sniper] waiting for peer to aim their card in slot {attackerSlot}");

            float deadline = Time.realtimeSinceStartup + AimTimeout;
            int[] targets = null;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (Net.TryTakeAim(attackerSlot, out targets)) break;
                if (!VersusMode.InMatch) yield break;
                yield return new WaitForEndOfFrame();
            }

            if (targets == null)
            {
                Trace.Warn($"[sniper] no aim from peer for slot {attackerSlot} - falling back to the slot opposite");
                if (attackerSlot >= 0 && attackerSlot < targetSide.Count) chosen.Add(targetSide[attackerSlot]);
                yield break;
            }

            foreach (int t in targets)
            {
                if (t < 0 || t >= targetSide.Count)
                {
                    Trace.Warn($"[sniper] peer aimed at slot {t}, which doesn't exist here - ignoring");
                    continue;
                }
                chosen.Add(targetSide[t]);
            }

            Trace.Info($"[sniper] peer aimed slot {attackerSlot} at {string.Join(", ", targets)}");
        }
    }
}
