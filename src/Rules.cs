using System;
using System.Collections.Generic;

namespace InscryptionMP
{
    /// <summary>How a player's deck hands them cards.</summary>
    public enum CardDraw
    {
        /// <summary>The game's own draw: a random card from what is left.</summary>
        Random = 0,

        /// <summary>Off the top, in the order the deck was built.</summary>
        InOrder = 1,
    }

    /// <summary>What sits in the second pile beside the deck.</summary>
    public enum SidePile
    {
        /// <summary>What the act ships with: squirrels in Act 1, empty vessels in Act 3.</summary>
        Standard = 0,

        /// <summary>Act 1's bee figurine pile - 1/1 and airborne. No effect elsewhere.</summary>
        Bees = 1,

        /// <summary>No second pile at all. Your deck is the only thing you draw from.</summary>
        None = 2,
    }

    /// <summary>
    /// The house rules a match is played under. Encoded as a key=value list rather than a
    /// fixed set of fields, so a later rule is an extra key an older build ignores instead
    /// of another protocol version.
    /// </summary>
    public struct Rules
    {
        /// <summary>Whether the host is running anything other than the defaults.</summary>
        public bool Custom;

        public CardDraw Draw;

        /// <summary>How far the scales must tip. The game's own number is 5.</summary>
        public int DamageToWin;

        public int StartingHand;
        public int DrawsPerTurn;
        public int MaxDeck;
        public int SigilsPerCard;
        public SidePile Side;

        /// <summary>Whether the player who joined takes the first turn instead of the host.</summary>
        public bool GuestFirst;

        public int StartingBones;

        public static Rules Default => new Rules
        {
            Custom = false,
            Draw = CardDraw.Random,
            DamageToWin = 5,
            StartingHand = 4,
            DrawsPerTurn = 1,
            MaxDeck = 20,
            SigilsPerCard = 2,
            Side = SidePile.Standard,
            GuestFirst = false,
            StartingBones = 0,
        };

        // ------------------------------------------------------------------ the wire

        public string Encode()
        {
            return "custom=" + (Custom ? "1" : "0")
                 + ";draw=" + (Draw == CardDraw.InOrder ? "order" : "random")
                 + ";win=" + DamageToWin
                 + ";hand=" + StartingHand
                 + ";draws=" + DrawsPerTurn
                 + ";deck=" + MaxDeck
                 + ";sigils=" + SigilsPerCard
                 + ";side=" + SideKey(Side)
                 + ";first=" + (GuestFirst ? "guest" : "host")
                 + ";bones=" + StartingBones;
        }

        public static Rules Decode(string payload)
        {
            var rules = Default;
            if (string.IsNullOrEmpty(payload)) return rules;

            foreach (string pair in payload.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;

                string key = pair.Substring(0, eq).Trim();
                string value = pair.Substring(eq + 1).Trim();

                // Unknown keys are somebody else's newer rule and unknown values are the
                // same thing; both keep the default rather than refusing the whole line.
                // Numbers are clamped, so a garbled one can't produce a match that never
                // ends or a hand bigger than the deck.
                switch (key)
                {
                    case "custom": rules.Custom = value == "1"; break;
                    case "draw":   rules.Draw = value == "order" ? CardDraw.InOrder : CardDraw.Random; break;
                    case "win":    rules.DamageToWin = Clamp(value, rules.DamageToWin, 1, 20); break;
                    case "hand":   rules.StartingHand = Clamp(value, rules.StartingHand, 1, 10); break;
                    case "draws":  rules.DrawsPerTurn = Clamp(value, rules.DrawsPerTurn, 1, 3); break;
                    case "deck":   rules.MaxDeck = Clamp(value, rules.MaxDeck, DeckLimits.Min, 60); break;
                    case "sigils": rules.SigilsPerCard = Clamp(value, rules.SigilsPerCard, 1, 6); break;
                    case "side":   rules.Side = SideFrom(value, rules.Side); break;
                    case "first":  rules.GuestFirst = value == "guest"; break;
                    case "bones":  rules.StartingBones = Clamp(value, rules.StartingBones, 0, 10); break;
                }
            }
            return rules;
        }

        private static int Clamp(string raw, int fallback, int min, int max)
        {
            int n;
            if (!int.TryParse(raw, out n)) return fallback;
            return n < min ? min : (n > max ? max : n);
        }

        private static string SideKey(SidePile side)
        {
            switch (side)
            {
                case SidePile.Bees: return "bees";
                case SidePile.None: return "none";
                default:            return "standard";
            }
        }

        private static SidePile SideFrom(string value, SidePile fallback)
        {
            switch (value)
            {
                case "bees":     return SidePile.Bees;
                case "none":     return SidePile.None;
                case "standard": return SidePile.Standard;
                default:         return fallback;
            }
        }

        // ------------------------------------------------------------------- reading

        public bool SameAs(Rules other)
        {
            return Custom == other.Custom
                && Draw == other.Draw
                && DamageToWin == other.DamageToWin
                && StartingHand == other.StartingHand
                && DrawsPerTurn == other.DrawsPerTurn
                && MaxDeck == other.MaxDeck
                && SigilsPerCard == other.SigilsPerCard
                && Side == other.Side
                && GuestFirst == other.GuestFirst
                && StartingBones == other.StartingBones;
        }

        /// <summary>Whether anything here differs from the game's own numbers.</summary>
        public bool IsVanilla
        {
            get
            {
                Rules d = Default;
                d.Custom = Custom;   // the flag itself is not a rule
                return SameAs(d);
            }
        }

        public string DrawName => Draw == CardDraw.InOrder ? "In order" : "Random";

        public string SideName
        {
            get
            {
                switch (Side)
                {
                    case SidePile.Bees: return "Bees";
                    case SidePile.None: return "None";
                    default:            return "Standard";
                }
            }
        }

        /// <summary>
        /// The short form, for a row that also has to hold a label and a button. The full
        /// list is one click away, and a value that runs under the button is worse than a
        /// value that admits there is more of it.
        /// </summary>
        public string ShortSummary(int show = 2)
        {
            if (IsVanilla) return "The game's own rules";

            List<string> parts = Changes();
            if (parts.Count <= show) return string.Join("  /  ", parts.ToArray());

            List<string> head = parts.GetRange(0, show);
            head.Add("+" + (parts.Count - show) + " more");
            return string.Join("  /  ", head.ToArray());
        }

        /// <summary>The rules in one line, for the lobby.</summary>
        public string Summary()
        {
            if (IsVanilla) return "The game's own rules.";
            return string.Join("  /  ", Changes().ToArray());
        }

        /// <summary>Every rule that differs from the game's own, in words.</summary>
        private List<string> Changes()
        {
            var parts = new List<string>();
            Rules d = Default;

            if (Draw != d.Draw) parts.Add("draw " + DrawName.ToLowerInvariant());
            if (DamageToWin != d.DamageToWin) parts.Add(DamageToWin + " to win");
            if (StartingHand != d.StartingHand) parts.Add("hand " + StartingHand);
            if (DrawsPerTurn != d.DrawsPerTurn) parts.Add(DrawsPerTurn + " draws a turn");
            if (MaxDeck != d.MaxDeck) parts.Add("deck up to " + MaxDeck);
            if (SigilsPerCard != d.SigilsPerCard) parts.Add(SigilsPerCard + " sigils a card");
            if (Side != d.Side) parts.Add("side pile " + SideName.ToLowerInvariant());
            if (GuestFirst != d.GuestFirst) parts.Add("guest goes first");
            if (StartingBones != d.StartingBones) parts.Add(StartingBones + " starting bones");

            return parts;
        }
    }

    /// <summary>Deck sizes that hold whatever the rules say.</summary>
    public static class DeckLimits
    {
        /// <summary>Below this a deck runs out before a match can resolve.</summary>
        public const int Min = 6;

        /// <summary>The most any rule may allow, so the deck builder can page for it.</summary>
        public const int Ceiling = 60;
    }
}
