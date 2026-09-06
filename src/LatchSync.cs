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
    /// Sends where a latcher latched, so both screens agree.
    ///
    /// Latch.OnPreDeathAnimation asks the owner to pick but runs an AI for an opponent
    /// card, so each client chose a different target. Same trap as Sniper, same fix.
    /// </summary>
    [HarmonyPatch]
    internal static class LatchSync
    {
        /// <summary>How long to wait for the peer's choice before letting the AI decide.</summary>
        private const float ChoiceTimeout = 30f;

        /// <summary>The latcher resolving its death, to tell whose mod this is. One at a
        /// time - the trigger handler finishes each pre-death animation before the next.</summary>
        private static PlayableCard _latcher;

        /// <summary>Set while calling the vanilla AI by reflection, which would otherwise
        /// re-enter our own prefix.</summary>
        private static bool _inFallback;

        private static readonly MethodInfo VanillaAi = AccessTools.Method(typeof(Latch), "AISelectTarget");

        // ------------------------------------------------------------------ our latcher

        [HarmonyPatch(typeof(Latch), nameof(Latch.OnPreDeathAnimation))]
        [HarmonyPrefix]
        private static void NoteWhoseLatcher(Latch __instance)
        {
            // AbilityBehaviour.Card is protected and is only GetComponent behind it.
            _latcher = __instance != null ? __instance.GetComponent<PlayableCard>() : null;
        }

        /// <summary>
        /// Tells the peer which card we latched onto. fromLatch is the game's own marker,
        /// so this covers bomb, brittle and shield without naming any of them.
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

            // A slot we can't latch onto means the boards have already drifted.
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

        /// <summary>A slot the peer sent, in our terms: their side is our opponent side.</summary>
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
