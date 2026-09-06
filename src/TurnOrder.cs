using System.Collections;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>Gives the two clients a shared notion of whose turn it is.</summary>
    [HarmonyPatch]
    internal static class TurnOrder
    {
        /// <summary>True when it is this client's turn to act.</summary>
        public static bool IsMyTurn { get; private set; }

        public static void BeginMatch(bool goFirst)
        {
            IsMyTurn = goFirst;
            Trace.Info($"[turn] match begins - {(goFirst ? "we go first" : "peer goes first")}");
        }

        /// <summary>Local player rang the bell: hand the turn over.</summary>
        public static void PassedToPeer()
        {
            IsMyTurn = false;

            // Anything still queued here belongs to a peer turn that has already been and
            // gone without being used - a Sniper card that died before it could attack.
            // Their next turn's aims arrive after this point, so nothing live is lost.
            Net.ClearAims();

            Trace.Info("[turn] passed to peer");
        }

        /// <summary>Peer finished their turn: it's ours now.</summary>
        public static void TakenFromPeer()
        {
            IsMyTurn = true;
            Trace.Info("[turn] taken from peer");
        }

        [HarmonyPatch(typeof(TurnManager), "PlayerTurn")]
        [HarmonyPrefix]
        private static bool SkipWhenNotOurTurn(ref IEnumerator __result)
        {
            if (!VersusMode.InMatch) return true;
            if (IsMyTurn) return true;

            Trace.Info("[turn] not our turn - skipping player phase");
            __result = NoTurn();
            return false;
        }

        private static IEnumerator NoTurn()
        {
            yield break;
        }
    }
}
