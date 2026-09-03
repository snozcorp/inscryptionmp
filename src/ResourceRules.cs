using System.Collections;
using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Makes every card cost type usable in a versus match.
    ///
    /// The base ResourcesManager already tracks bones, energy and gems - the Act 1 scene
    /// simply never grants energy, because TurnManager only does that when the active
    /// scene is Act 2 or Act 3. Granting it here makes Tech cards playable on Leshy's
    /// table, which in turn lets players bring decks from any act to the same match.
    /// </summary>
    [HarmonyPatch]
    internal static class ResourceRules
    {
        [HarmonyPatch(typeof(TurnManager), "DoUpkeepPhase")]
        [HarmonyPostfix]
        private static IEnumerator GrantEnergy(IEnumerator result, bool playerUpkeep)
        {
            // Let the original upkeep run first.
            while (result.MoveNext()) yield return result.Current;

            if (!VersusMode.InMatch || !playerUpkeep) yield break;

            var res = Singleton<ResourcesManager>.Instance;
            if (res == null) yield break;

            // Same progression the game uses in Act 3: one more max energy per turn,
            // refilled at the start of your turn.
            yield return res.AddMaxEnergy(1);
            yield return res.RefreshEnergy();
            Trace.Info($"[res] energy {res.PlayerEnergy}/{res.PlayerMaxEnergy}");
        }
    }
}
