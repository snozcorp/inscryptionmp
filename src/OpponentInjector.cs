using System.Collections.Generic;
using DiskCardGame;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// When a multiplayer session is live, swap whatever opponent the encounter asked for
    /// with our network-driven one. Everything else about the battle stays vanilla.
    /// </summary>
    [HarmonyPatch]
    internal static class OpponentInjector
    {
        [HarmonyPatch(typeof(Opponent), nameof(Opponent.SpawnOpponent))]
        [HarmonyPostfix]
        private static void Swap(ref Opponent __result, EncounterData encounterData)
        {
            // Gate on an actual match, not merely being connected - otherwise starting a
            // campaign battle while sat in a lobby replaces Leshy with a network opponent.
            if (!VersusMode.InMatch || __result == null) return;

            GameObject go = __result.gameObject;
            Trace.Info($"[inject] replacing {__result.GetType().Name} with NetworkOpponent");

            Object.DestroyImmediate(__result);

            var netOpp = go.AddComponent<NetworkOpponent>();
            netOpp.NumLives = netOpp.StartingLives;
            netOpp.TurnPlan = new List<List<CardInfo>>();
            netOpp.Difficulty = 0;

            __result = netOpp;
        }
    }
}
