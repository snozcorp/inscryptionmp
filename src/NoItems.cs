using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>Keeps the campaign's consumables out of a versus match.</summary>
    internal static class NoItems
    {
        /// <summary>Empties the run's item list; ItemsManager builds the slots from it.</summary>
        internal static void StripFrom(RunState run)
        {
            if (run?.consumables == null || run.consumables.Count == 0) return;

            Trace.Info($"[items] dropping {run.consumables.Count} consumable(s) from the match run: " +
                       string.Join(", ", run.consumables.ToArray()));
            run.consumables.Clear();
        }

    }
}
