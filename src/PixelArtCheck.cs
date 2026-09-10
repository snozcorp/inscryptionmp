using System.Collections.Generic;
using DiskCardGame;
using GBC;
using HarmonyLib;

namespace InscryptionMP
{
    /// <summary>
    /// Names the cards that come out blank on the GBC table. PixelCardDisplayer draws
    /// baseInfo.pixelPortrait and nothing else, so a card without one is a card-shaped hole
    /// with stats on it - and from the outside there is no way to tell which card it was.
    /// </summary>
    [HarmonyPatch]
    internal static class PixelArtCheck
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        [HarmonyPatch(typeof(PixelCardDisplayer), nameof(PixelCardDisplayer.DisplayInfo))]
        [HarmonyPrefix]
        private static void NoteMissingArt(CardRenderInfo renderInfo)
        {
            if (!VersusMode.VersusContext) return;

            CardInfo info = renderInfo?.baseInfo;
            if (info == null || info.pixelPortrait != null) return;

            if (!Reported.Add(info.name)) return;
            Trace.Warn($"[art] '{info.name}' has no pixel portrait - it will draw blank here"
                       + $" (3D portrait: {(info.portraitTex == null ? "also missing" : "present")})");
        }

        /// <summary>
        /// Everything the act would offer, checked in one go. Cheaper than finding out one
        /// blank card at a time, and it runs before anybody has built a deck around one.
        /// </summary>
        internal static void AuditPool()
        {
            if (!ActInfo.UsesPixelArt(ActInfo.Selected)) return;

            var blank = new List<string>();
            foreach (CardInfo c in DeckStore.Pool)
                if (c != null && c.pixelPortrait == null) blank.Add(c.name);

            if (blank.Count == 0)
            {
                Trace.Info("[art] every card in the Act 2 pool has pixel art");
                return;
            }

            Trace.Error($"[art] {blank.Count} card(s) offered for Act 2 with no pixel art: "
                        + string.Join(", ", blank.ToArray()));
        }
    }
}
