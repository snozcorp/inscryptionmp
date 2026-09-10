using System;

namespace InscryptionMP
{
    /// <summary>
    /// The dials the host can turn, as data. One list drives the rules screen, the lobby
    /// summary and the cycling, so a new rule is an entry here rather than another row of
    /// hand-written buttons that drifts out of step with what the match actually does.
    /// </summary>
    internal static class RuleBook
    {
        internal sealed class Dial
        {
            public string Key;
            public string Label;

            /// <summary>What each position reads as, in order.</summary>
            public string[] Choices;

            /// <summary>One line saying what turning it does.</summary>
            public string Note;

            public Func<Rules, int> IndexOf;
            public Func<Rules, int, Rules> WithIndex;

            public string TextOf(Rules r)
            {
                int i = IndexOf(r);
                return i >= 0 && i < Choices.Length ? Choices[i] : "?";
            }
        }

        /// <summary>
        /// Position of a value in a list, or the nearest thing to it. A peer can send a
        /// number no dial has a position for - it is clamped, not rejected - and the screen
        /// still has to show something true.
        /// </summary>
        private static int Nearest(int[] values, int value)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
            return best;
        }

        private static readonly int[] WinValues = { 3, 5, 7, 10 };
        private static readonly int[] HandValues = { 3, 4, 5, 6 };
        private static readonly int[] DrawValues = { 1, 2 };
        private static readonly int[] DeckValues = { 10, 20, 30, 40 };
        private static readonly int[] SigilValues = { 2, 3, 4 };
        private static readonly int[] BoneValues = { 0, 1, 2, 3 };

        public static readonly Dial[] Dials =
        {
            new Dial
            {
                Key = "draw",
                Label = "Card draw",
                Choices = new[] { "Random", "In order" },
                Note = "In order deals off the top of your deck, in the order you built it.",
                IndexOf = r => r.Draw == CardDraw.InOrder ? 1 : 0,
                WithIndex = (r, i) => { r.Draw = i == 1 ? CardDraw.InOrder : CardDraw.Random; return r; },
            },
            new Dial
            {
                Key = "win",
                Label = "Damage to win",
                Choices = new[] { "3", "5", "7", "10" },
                Note = "How far the scales have to tip. The game's own number is 5.",
                IndexOf = r => Nearest(WinValues, r.DamageToWin),
                WithIndex = (r, i) => { r.DamageToWin = WinValues[i]; return r; },
            },
            new Dial
            {
                Key = "hand",
                Label = "Starting hand",
                Choices = new[] { "3", "4", "5", "6" },
                Note = "Cards dealt before the first turn, the side pile's one included.",
                IndexOf = r => Nearest(HandValues, r.StartingHand),
                WithIndex = (r, i) => { r.StartingHand = HandValues[i]; return r; },
            },
            new Dial
            {
                Key = "draws",
                Label = "Draws a turn",
                Choices = new[] { "1", "2" },
                Note = "How many cards you take at the start of each of your turns.",
                IndexOf = r => Nearest(DrawValues, r.DrawsPerTurn),
                WithIndex = (r, i) => { r.DrawsPerTurn = DrawValues[i]; return r; },
            },
            new Dial
            {
                Key = "deck",
                Label = "Deck size",
                Choices = new[] { "10", "20", "30", "40" },
                Note = "The most cards a deck may hold. Both of you have to fit inside it.",
                IndexOf = r => Nearest(DeckValues, r.MaxDeck),
                WithIndex = (r, i) => { r.MaxDeck = DeckValues[i]; return r; },
            },
            new Dial
            {
                Key = "sigils",
                Label = "Sigils a card",
                Choices = new[] { "2", "3", "4" },
                Note = "Including the ones a card is printed with. Act 2 draws the first two.",
                IndexOf = r => Nearest(SigilValues, r.SigilsPerCard),
                WithIndex = (r, i) => { r.SigilsPerCard = SigilValues[i]; return r; },
            },
            new Dial
            {
                Key = "side",
                Label = "Side pile",
                Choices = new[] { "Standard", "Bees", "None" },
                Note = "Squirrels in Act 1 and vessels in Act 3. Bees are 1/1 and airborne.",
                IndexOf = r => (int)r.Side,
                WithIndex = (r, i) => { r.Side = (SidePile)i; return r; },
            },
            new Dial
            {
                Key = "first",
                Label = "First turn",
                Choices = new[] { "Host", "Opponent" },
                Note = "Who plays first. The host does unless you say otherwise.",
                IndexOf = r => r.GuestFirst ? 1 : 0,
                WithIndex = (r, i) => { r.GuestFirst = i == 1; return r; },
            },
            new Dial
            {
                Key = "bones",
                Label = "Starting bones",
                Choices = new[] { "0", "1", "2", "3" },
                Note = "Bones in hand before the first card is played. Act 3 deals in energy.",
                IndexOf = r => Nearest(BoneValues, r.StartingBones),
                WithIndex = (r, i) => { r.StartingBones = BoneValues[i]; return r; },
            },
        };

        public static Dial Find(string key)
        {
            foreach (Dial d in Dials)
                if (d.Key == key) return d;
            return null;
        }
    }
}
