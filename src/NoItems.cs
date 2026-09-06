using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// Keeps the campaign's consumables out of a versus match.
    ///
    /// ResetPart1Run seeds a run with them, and none of it is synced: the Pliers and Dagger
    /// damage the scales without telling the peer, and the Dagger writes SpecialDaggerUsed
    /// to the save file's story events and queues a map node onto a run with no map.
    ///
    /// The hammer is left alone - its own slot, its own side of the board, and the card it
    /// smashes leaves the turn-end snapshot like any other death.
    /// </summary>
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
