using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// Keeps consumable items out of a versus match.
    ///
    /// A versus run is synthesised with RunState.ResetPart1Run, and that seeds the run's
    /// consumables the same way a campaign run does - a Squirrel Bottle, then Pliers or the
    /// Special Dagger, then a Fish Hook. Nothing about them survives contact with a match:
    ///
    /// - **Nothing they do is sent to the other player.** The Pliers deal a point of damage
    ///   to the opponent and the Dagger deals four, both straight to the scales. The peer
    ///   never hears about it, so from that moment the two clients disagree about the score
    ///   and eventually about who won.
    /// - **The Dagger writes to the player's profile.** SetEventCompleted(SpecialDaggerUsed)
    ///   is not run state, it is the save file's story events, which a match has no business
    ///   touching - the mod's whole promise is that your campaign is left alone. The Pliers
    ///   are milder but still bump an ascension stat and can fire an achievement.
    /// - **The Dagger queues a post-battle map node** (ChooseEyeballNodeData) onto a run
    ///   with no map, to be walked to once the battle ends.
    ///
    /// So a match carries none. Both players get the same empty slots, which is at least
    /// symmetrical, and the board stays the only thing that decides a match. Syncing them
    /// properly is a real feature and wants its own protocol message; this is the floor.
    ///
    /// **The hammer is not one of these.** It lives in a slot of its own rather than in the
    /// run's consumables, it only ever targets the player's own side, and the card it
    /// smashes is gone from the turn-end snapshot like any other death - so it needs no
    /// syncing and is left alone. Refusing every ConsumableItem took it out too, which is
    /// why that guard is gone: HammerItem derives from TargetSlotItem, which derives from
    /// ConsumableItem.
    /// </summary>
    internal static class NoItems
    {
        /// <summary>
        /// Takes the items back out of the synthesised run.
        ///
        /// Emptying the list is enough on its own: ItemsManager builds the slots from it at
        /// Start, so with nothing in it there is nothing on the table to pick up.
        /// </summary>
        internal static void StripFrom(RunState run)
        {
            if (run?.consumables == null || run.consumables.Count == 0) return;

            Trace.Info($"[items] dropping {run.consumables.Count} consumable(s) from the match run: " +
                       string.Join(", ", run.consumables.ToArray()));
            run.consumables.Clear();
        }

    }
}
