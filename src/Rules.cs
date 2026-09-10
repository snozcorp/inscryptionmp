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

    /// <summary>
    /// The house rules a match is played under. Encoded as a key=value list rather than a
    /// fixed set of fields, so a later rule is an extra key an older build ignores instead
    /// of another protocol version.
    /// </summary>
    public struct Rules
    {
        public CardDraw Draw;

        public static Rules Default => new Rules { Draw = CardDraw.Random };

        public string Encode() => "draw=" + (Draw == CardDraw.InOrder ? "order" : "random");

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

                // Unknown keys are somebody else's newer rule, and an unknown value for a
                // key we do know is the same thing. Both keep the default rather than
                // refusing the whole line.
                if (key == "draw") rules.Draw = value == "order" ? CardDraw.InOrder : CardDraw.Random;
            }
            return rules;
        }

        public string DrawName => Draw == CardDraw.InOrder ? "In order" : "Random";

        public bool SameAs(Rules other) => Draw == other.Draw;
    }
}
