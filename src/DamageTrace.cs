using DiskCardGame;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>Records every point of damage alongside the board it was dealt from.</summary>
    [HarmonyPatch]
    internal static class DamageTrace
    {
        [HarmonyPatch(typeof(LifeManager), nameof(LifeManager.ShowDamageSequence))]
        [HarmonyPrefix]
        private static void LogDamage(int damage, int numWeights, bool toPlayer)
        {
            if (!VersusMode.InMatch) return;

            string who = toPlayer ? "player" : "opponent";
            Trace.Info($"[dmg] {damage} to {who} (weights {numWeights})  " +
                       $"mine [{Describe(playerSide: true)}]  theirs [{Describe(playerSide: false)}]");
        }

        /// <summary>Card name and power in each slot on one side of the board.</summary>
        private static string Describe(bool playerSide)
        {
            var board = Singleton<BoardManager>.Instance;
            if (board == null) return "no board";

            var slots = playerSide ? board.PlayerSlotsCopy : board.OpponentSlotsCopy;
            if (slots == null) return "no slots";

            var parts = new string[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                PlayableCard card = slots[i] != null ? slots[i].Card : null;
                parts[i] = card?.Info == null ? "-" : $"{card.Info.name}({card.Attack}/{card.Health})";
            }
            return string.Join(" ", parts);
        }
    }
}
