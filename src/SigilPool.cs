using System;
using System.Collections.Generic;
using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// The sigils a player may put on a card, for the act they are building for.
    ///
    /// Not every sigil can be drawn in every act. Act 2 renders through
    /// PixelCardAbilityIcons, which reads AbilityInfo.pixelIcon - a sigil without one draws
    /// an empty space, and there is no artwork anywhere in the game to fall back on. Acts 1
    /// and 3 use the 3D icon, with our own pixel-art fallback behind it.
    ///
    /// Offering a sigil that can't be seen would be worse than not offering it: both
    /// players would have a card doing something with nothing on its face to say so.
    /// </summary>
    public static class SigilPool
    {
        private static List<Ability> _cache;
        private static MatchAct _cachedFor = (MatchAct)0;

        public static List<Ability> For(MatchAct act)
        {
            if (_cache != null && _cachedFor == act) return _cache;

            var pool = new List<Ability>();

            try
            {
                foreach (Ability ability in Enum.GetValues(typeof(Ability)))
                {
                    if (ability == Ability.None) continue;

                    AbilityInfo info = AbilitiesUtil.GetInfo(ability);
                    if (info == null) continue;

                    // Without a rulebook name it is an internal marker rather than
                    // something a player would recognise or could read up on.
                    if (string.IsNullOrEmpty(info.rulebookName)) continue;

                    if (!CanBeGrafted(info, act)) continue;
                    if (!CanBeDrawn(info, act)) continue;

                    pool.Add(ability);
                }

                pool.Sort((a, b) => string.Compare(
                    AbilitiesUtil.GetInfo(a).rulebookName,
                    AbilitiesUtil.GetInfo(b).rulebookName,
                    StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                Trace.Error($"[sigils] could not build the pool: {e.Message}");
            }

            Trace.Info($"[sigils] {pool.Count} sigils available in {ActInfo.Name(act)}");
            _cache = pool;
            _cachedFor = act;
            return pool;
        }

        /// <summary>
        /// Whether the game itself considers this sigil something you can put on a card.
        ///
        /// Every ability in the game includes boss powers, conduit plumbing and one-off
        /// scripted effects, which is hundreds of entries and pages of nonsense to scroll.
        /// The game already marks the ones meant to be grafted onto a card - Part1Modular
        /// is what totems draw from, Part3BuildACard is P03's card builder - so use its
        /// judgement rather than inventing our own list.
        /// </summary>
        private static bool CanBeGrafted(AbilityInfo info, MatchAct act)
        {
            if (info.metaCategories == null) return false;

            // Ask the act's own list rather than pooling all three. P03's build-a-card
            // sigils on Leshy's table is both a longer list and the wrong list - these
            // categories exist precisely because each act grafts a different set.
            if (act == MatchAct.Act3)
            {
                return info.metaCategories.Contains(AbilityMetaCategory.Part3Modular)
                    || info.metaCategories.Contains(AbilityMetaCategory.Part3BuildACard);
            }

            // Act 2 has no modular list of its own in the base game; it plays with the
            // same creatures as Act 1, so Leshy's totem set is the right one.
            return info.metaCategories.Contains(AbilityMetaCategory.Part1Modular);
        }

        private static bool CanBeDrawn(AbilityInfo info, MatchAct act)
        {
            if (ActInfo.UsesPixelArt(act)) return info.pixelIcon != null;

            // The 3D table loads its icon by name; our own fallback covers a missing one
            // with the pixel art, so either being present is enough.
            if (info.pixelIcon != null) return true;
            return AbilitiesUtil.LoadAbilityIcon(info.ability.ToString()) != null;
        }

        /// <summary>What the rulebook calls it, for the picker.</summary>
        public static string DisplayName(Ability ability)
        {
            AbilityInfo info = AbilitiesUtil.GetInfo(ability);
            return info == null || string.IsNullOrEmpty(info.rulebookName)
                ? ability.ToString()
                : info.rulebookName;
        }

        public static void Reset()
        {
            _cache = null;
            _cachedFor = (MatchAct)0;
        }
    }
}
