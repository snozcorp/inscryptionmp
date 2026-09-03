using DiskCardGame;
using GBC;

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

        /// <summary>
        /// Whether this act's table will host cards of a given temple. Act 1 is Leshy's
        /// creatures and Act 3 is P03's machines, but Act 2 is the GBC game where all four
        /// scrybes' cards appear together - so it accepts everything.
        /// </summary>
        public static bool AcceptsTemple(MatchAct act, CardTemple temple)
        {
            switch (act)
            {
                case MatchAct.Act2: return true;
                case MatchAct.Act3: return temple == CardTemple.Tech;
                default:            return temple == CardTemple.Nature;
            }
        }

        /// <summary>
        /// Act 2 renders through PixelCardDisplayer, which draws pixelPortrait rather than
        /// the 3D portraitTex. A card with only one of the two is fine on the act that
        /// uses it and blank on the other.
        /// </summary>
        public static bool UsesPixelArt(MatchAct act) => act == MatchAct.Act2;

        /// <summary>
        /// Whether this act's scene has a GameFlowManager.
        ///
        /// Act 1 and Act 3 are explorable scenes with a flow manager driving their game
        /// states. GBC_CardBattle is a self-contained battle scene - the flow around it
        /// lives in the overworld scene it is loaded from, so there is no flow manager
        /// here at all and waiting for one waits forever.
        /// </summary>
        public static bool HasFlowManager(MatchAct act) => act != MatchAct.Act2;

        /// <summary>
        /// Which board theme the GBC table should dress itself in. The theme is normally
        /// chosen by the NPC being fought; with no NPC, follow whatever the player's own
        /// deck is mostly made of.
        /// </summary>
        public static PixelBoardSpriteSetter.BoardTheme ThemeForTemple(CardTemple temple)
        {
            switch (temple)
            {
                case CardTemple.Tech:   return PixelBoardSpriteSetter.BoardTheme.Tech;
                case CardTemple.Undead: return PixelBoardSpriteSetter.BoardTheme.Undead;
                case CardTemple.Wizard: return PixelBoardSpriteSetter.BoardTheme.Wizard;
                default:                return PixelBoardSpriteSetter.BoardTheme.Nature;
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

    }
}
