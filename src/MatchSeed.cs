using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// The game seeds the shuffle and opening hand from the run. A match's run is synthetic
    /// and identical every time, so every match dealt the same opening hand. Each match
    /// adds its own roll on top.
    /// </summary>
    [HarmonyPatch]
    internal static class MatchSeed
    {
        public static int Current { get; private set; }

        private static readonly System.Random Dice = new System.Random();

        public static void Roll()
        {
            Current = Dice.Next(1, 1000000);
            Trace.Info($"[seed] this match deals from seed offset {Current}");
        }

        [HarmonyPatch(typeof(SaveFile), nameof(SaveFile.GetCurrentRandomSeed))]
        [HarmonyPostfix]
        private static void PerMatch(ref int __result)
        {
            if (VersusMode.InMatch) __result += Current;
        }
    }
}
