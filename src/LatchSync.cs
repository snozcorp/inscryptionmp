using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Makes a latcher fasten its sigil to the same card on both screens.
    ///
    /// Latch.OnPreDeathAnimation branches on who owns the dying card:
    ///
    ///     if (base.Card.OpponentCard)  yield return AISelectTarget(...);
    ///     else                         yield return ChooseTarget(...);
    ///
    /// which is the same trap Sniper sets. The player who owns the latcher picks a target
    /// by hand; on the other screen that card is an opponent card, so their client runs the
    /// AI and picks its own - and AIEvaluateTarget adds 1000 to a card whose side matches
    /// the sigil's sign, so for a bomb it deliberately reaches for the *other* side of the
    /// board. The two clients then disagree about which card is carrying the bomb, and once
    /// it goes off they disagree about which cards are still alive.
    ///
    /// So the owner's choice travels, exactly like an aim: the client that made the choice
    /// sends it, and the other applies it instead of guessing. If the peer never says - an
    /// older build, or a crash mid-choice - the AI still runs, because a bomb on the wrong
    /// card is better than a match frozen forever.
    /// </summary>
    [HarmonyPatch]
    internal static class LatchSync
    {
        /// <summary>How long to wait for the peer's choice before letting the AI decide.</summary>
        private const float ChoiceTimeout = 30f;

        /// <summary>
        /// The latcher currently resolving its death, so the mod it applies can be traced
        /// back to whose card it came from. One at a time: the trigger handler runs each
        /// pre-death animation to completion before starting the next.
        /// </summary>
        private static PlayableCard _latcher;

        /// <summary>Set while we call the vanilla AI back through reflection, which would
        /// otherwise re-enter our own prefix and recurse forever.</summary>
        private static bool _inFallback;

        private static readonly MethodInfo VanillaAi = AccessTools.Method(typeof(Latch), "AISelectTarget");

        // ------------------------------------------------------------------ our latcher

        [HarmonyPatch(typeof(Latch), nameof(Latch.OnPreDeathAnimation))]
        [HarmonyPrefix]
        private static void NoteWhoseLatcher(Latch __instance)
        {
            // AbilityBehaviour.Card is protected, and is only GetComponent<PlayableCard>()
            // behind the property - so ask the component directly rather than by reflection.
            _latcher = __instance != null ? __instance.GetComponent<PlayableCard>() : null;
        }

        /// <summary>
        /// Tells the peer which card we latched onto.
        ///
        /// fromLatch is the game's own marker on the modification, so this catches every
        /// latcher - bomb, brittle and shield - without naming any of them. Only fires for
        /// a latcher of ours: when it is theirs, we are the side doing the waiting, and
        /// echoing their choice back would leave a message nobody consumes.
        /// </summary>
        [HarmonyPatch(typeof(PlayableCard), nameof(PlayableCard.AddTemporaryMod))]
        [HarmonyPostfix]
        private static void TellPeerWhereWeLatched(PlayableCard __instance, CardModificationInfo mod)
        {
            if (mod == null || !mod.fromLatch) return;
            if (!VersusMode.InMatch || !Net.Connected || !Net.PeerSpeaksV3) return;
            if (__instance == null) return;

            if (_latcher == null || _latcher.OpponentCard) return;   // theirs: they'll tell us

            CardSlot slot = __instance.Slot;
            if (slot == null) return;

            Trace.Info($"[latch] we latched onto {Name(__instance)} in " +
                       $"{(slot.IsPlayerSlot ? "our" : "their")} slot {slot.Index} - telling the peer");
            Net.Send(Protocol.LatchTarget(slot.IsPlayerSlot, slot.Index));
        }

        // ---------------------------------------------------------------- their latcher

        [HarmonyPatch(typeof(Latch), "AISelectTarget")]
        [HarmonyPrefix]
        private static bool UsePeersChoice(Latch __instance, List<CardSlot> validTargets,
                                           Action<CardSlot> chosenCallback, ref IEnumerator __result)
        {
            if (_inFallback) return true;          // our own call back into the vanilla AI
            if (!VersusMode.InMatch) return true;  // campaign: untouched

            __result = TakePeersChoice(__instance, validTargets, chosenCallback);
            return false;
        }

        private static IEnumerator TakePeersChoice(Latch latch, List<CardSlot> validTargets,
                                                   Action<CardSlot> chosenCallback)
        {
            if (!Net.PeerSpeaksV3)
            {
                Trace.Info("[latch] peer is on an older protocol - letting the AI choose");
                yield return RunVanillaAi(latch, validTargets, chosenCallback);
                yield break;
            }

            Trace.Info("[latch] waiting for the peer to say where they latched");

            float deadline = Time.realtimeSinceStartup + ChoiceTimeout;
            bool arrived = false;
            bool theirSide = false;
            int index = -1;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (Net.TryTakeLatch(out theirSide, out index)) { arrived = true; break; }
                if (!VersusMode.InMatch) yield break;
                yield return new WaitForEndOfFrame();
            }

            if (!arrived)
            {
                Trace.Warn("[latch] peer never said - letting the AI choose");
                yield return RunVanillaAi(latch, validTargets, chosenCallback);
                yield break;
            }

            CardSlot target = Resolve(theirSide, index);

            // Their board is authoritative for their side, but a slot we can't latch onto
            // means the two boards have already drifted - and forcing it would put the
            // sigil somewhere the game refuses to accept.
            if (target == null || target.Card == null || !validTargets.Contains(target))
            {
                Trace.Warn($"[latch] peer latched onto a slot we can't use " +
                           $"({(theirSide ? "their" : "our")} {index}) - letting the AI choose");
                yield return RunVanillaAi(latch, validTargets, chosenCallback);
                yield break;
            }

            Trace.Info($"[latch] peer latched onto {Name(target.Card)} in " +
                       $"{(theirSide ? "their" : "our")} slot {index}");
            chosenCallback(target);
            yield return new WaitForSeconds(0.1f);
        }

        /// <summary>
        /// A slot index the peer sent, in our terms. Their own side of the board is our
        /// opponent side, and the other way round.
        /// </summary>
        private static CardSlot Resolve(bool sendersOwnSide, int index)
        {
            var board = Singleton<BoardManager>.Instance;
            if (board == null) return null;

            List<CardSlot> slots = sendersOwnSide ? board.OpponentSlotsCopy : board.PlayerSlotsCopy;
            if (slots == null || index < 0 || index >= slots.Count) return null;
            return slots[index];
        }

        private static IEnumerator RunVanillaAi(Latch latch, List<CardSlot> validTargets,
                                                 Action<CardSlot> chosenCallback)
        {
            if (VanillaAi == null || latch == null) yield break;

            IEnumerator routine = null;
            _inFallback = true;
            try
            {
                routine = VanillaAi.Invoke(latch, new object[] { validTargets, chosenCallback }) as IEnumerator;
            }
            catch (Exception e)
            {
                Trace.Error($"[latch] could not run the vanilla AI: {e.Message}");
            }
            finally
            {
                _inFallback = false;
            }

            if (routine != null) yield return routine;
        }

        private static string Name(PlayableCard card)
        {
            return card?.Info != null ? card.Info.name : "a card";
        }
    }
}
