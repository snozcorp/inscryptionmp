using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>Which act's table a match is played on.</summary>
    public enum MatchAct
    {
        Act1 = 1,
        Act2 = 2,
        Act3 = 3,
    }

    /// <summary>
    /// Per-act facts the rest of the mod needs.
    ///
    /// The act determines far more than the card list: the scene brings its own card
    /// renderer, resource UI, board and sigil icons. Trying to draw one act's cards on
    /// another act's table means supplying art that table doesn't have, so a match is
    /// played on the table the cards belong to.
    /// </summary>
    public static class ActInfo
    {
        /// <summary>The act currently selected for the next match.</summary>
        public static MatchAct Selected { get; set; } = MatchAct.Act1;

        /// <summary>The act the current match is actually running as.</summary>
        public static MatchAct Current { get; internal set; } = MatchAct.Act1;

        public static string SceneFor(MatchAct act)
        {
            switch (act)
            {
                case MatchAct.Act2: return "GBC_CardBattle";
                case MatchAct.Act3: return "Part3_Cabin";
                default:            return "Part1_Cabin";
            }
        }

        /// <summary>Which card temple belongs to each act's table.</summary>
        public static CardTemple TempleFor(MatchAct act)
        {
            switch (act)
            {
                case MatchAct.Act2: return CardTemple.Undead;   // GBC uses all four; see DeckStore
                case MatchAct.Act3: return CardTemple.Tech;
                default:            return CardTemple.Nature;
            }
        }

        /// <summary>
        /// Whether this act's table grants energy. TurnManager grants it when the active
        /// scene is Act 2 or Act 3, so Act 1 cards must be payable with blood and bones.
        /// </summary>
        public static bool GrantsEnergy(MatchAct act) => act != MatchAct.Act1;

        /// <summary>Whether this act's table deals in Mox gems.</summary>
        public static bool GrantsGems(MatchAct act) => act == MatchAct.Act2;

        /// <summary>
        /// The meta categories that mark a card as something this act would offer a
        /// player. Each act uses its own: ChoiceNode is Act 1's, Act 3 uses Part3Random,
        /// Act 2 uses the GBC ones. Testing only for ChoiceNode left other acts empty.
        /// </summary>
        public static CardMetaCategory[] OfferedCategories(MatchAct act)
        {
            switch (act)
            {
                case MatchAct.Act2:
                    return new[] { CardMetaCategory.GBCPlayable, CardMetaCategory.GBCPack };
                case MatchAct.Act3:
                    return new[] { CardMetaCategory.Part3Random, CardMetaCategory.Rare };
                default:
                    return new[] { CardMetaCategory.ChoiceNode, CardMetaCategory.Rare };
            }
        }

        public static string Name(MatchAct act)
        {
            switch (act)
            {
                case MatchAct.Act2: return "Act 2";
                case MatchAct.Act3: return "Act 3";
                default:            return "Act 1";
            }
        }

        /// <summary>Deck file suffix, so each act keeps its own deck.</summary>
        public static string DeckSuffix(MatchAct act)
        {
            return "-act" + (int)act;
        }

        /// <summary>
        /// Acts proven to work end to end. Act 1 is shipped; the others are being brought
        /// up and should not be offered to players until they actually play.
        /// </summary>
        public static bool IsSupported(MatchAct act)
        {
            return act == MatchAct.Act1 || Experimental;
        }

        /// <summary>Set from config to expose acts that are still being worked on.</summary>
        public static bool Experimental { get; set; }
    }
}
