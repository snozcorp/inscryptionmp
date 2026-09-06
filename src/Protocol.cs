namespace InscryptionMP
{
    /// <summary>Line protocol. Deliberately human-readable so the log IS the debugger.</summary>
    public static class Protocol
    {
        public const string EndTurn = "END";
        /// <summary>
        /// Bumped whenever the wire format changes. Two clients on different protocol versions
        /// connect happily and then desync in confusing ways, so they refuse each other up
        /// front instead.
        /// </summary>
        public const int Version = 3;

        /// <summary>The oldest protocol we can still play against.</summary>
        public const int MinCompatible = 2;

        public const string HelloPrefix = "HELLO ";

        public static string Hello => HelloPrefix + Version + " " + Plugin.Version;

        /// <summary>Parses a peer greeting into its protocol version and mod version.</summary>
        public static bool TryParseHello(string msg, out int protocolVersion, out string modVersion)
        {
            protocolVersion = 0;
            modVersion = "?";
            if (msg == null || !msg.StartsWith(HelloPrefix)) return false;

            string[] parts = msg.Substring(HelloPrefix.Length).Split(' ');
            if (parts.Length < 1 || !int.TryParse(parts[0], out protocolVersion)) return false;
            if (parts.Length > 1) modVersion = parts[1];
            return true;
        }
        public const string StartPrefix = "START ";

        /// <summary>Asks the peer to start a match on a given act's table.</summary>
        public static string StartMatch(MatchAct act) => StartPrefix + (int)act;

        public static bool TryParseStart(string msg, out MatchAct act)
        {
            act = MatchAct.Act1;
            if (msg == null || !msg.StartsWith(StartPrefix)) return false;
            if (!int.TryParse(msg.Substring(StartPrefix.Length), out int n)) return false;
            if (n < 1 || n > 3) return false;
            act = (MatchAct)n;
            return true;
        }
        public const string SacrificePrefix = "SAC ";
        public const string BoardPrefix = "BOARD ";

        /// <summary>A card in the sender's slot N was sacrificed.</summary>
        public static string Sacrifice(int slotIndex) => SacrificePrefix + slotIndex;

        public static bool TryParseSacrifice(string msg, out int slotIndex)
        {
            slotIndex = -1;
            return msg != null
                   && msg.StartsWith(SacrificePrefix)
                   && int.TryParse(msg.Substring(SacrificePrefix.Length), out slotIndex);
        }
        public const string EmptySlot = "-";

        /// <summary>
        /// One slot in a board snapshot: which card, and the stats it is actually showing.
        /// </summary>
        public struct SlotState
        {
            public string Name;
            public int Attack;
            public int Health;

            /// <summary>
            /// Abilities the card has picked up during the match, beyond the ones its
            /// definition ships with.
            /// </summary>
            public string[] Sigils;

            /// <summary>
            /// Whether the sender actually told us its numbers. A protocol 2 peer sends
            /// names only, and treating "absent" as 0/0 would zero out its whole board.
            /// </summary>
            public bool HasStats;

            public bool IsEmpty => string.IsNullOrEmpty(Name) || Name == EmptySlot;
        }

        /// <summary>
        /// "-" for an empty slot, otherwise "Name:attack/health", with ":sigil,sigil"
        /// appended when the card has gained any.
        /// </summary>
        public static string EncodeSlot(string name, int attack, int health, string[] sigils = null)
        {
            if (string.IsNullOrEmpty(name) || name == EmptySlot) return EmptySlot;

            string token = name + ":" + attack + "/" + health;
            if (sigils != null && sigils.Length > 0) token += ":" + string.Join(",", sigils);
            return token;
        }

        public static SlotState DecodeSlot(string token)
        {
            var state = new SlotState { Name = EmptySlot, Sigils = EmptySigils };
            if (string.IsNullOrEmpty(token) || token == EmptySlot) return state;

            string[] parts = token.Split(':');
            state.Name = parts[0];

            if (parts.Length > 1)
            {
                string[] stats = parts[1].Split('/');
                if (stats.Length == 2
                    && int.TryParse(stats[0], out int atk)
                    && int.TryParse(stats[1], out int hp))
                {
                    state.Attack = atk;
                    state.Health = hp;
                    state.HasStats = true;
                }
            }

            if (parts.Length > 2 && parts[2].Length > 0)
                state.Sigils = parts[2].Split(',');

            return state;
        }

        private static readonly string[] EmptySigils = new string[0];

        /// <summary>Authoritative snapshot of the sender's player slots.</summary>
        public static string Board(string[] encodedSlots)
        {
            return BoardPrefix + string.Join("|", encodedSlots);
        }

        public static bool TryParseBoard(string msg, out string[] slots)
        {
            slots = null;
            if (msg == null || !msg.StartsWith(BoardPrefix)) return false;
            slots = msg.Substring(BoardPrefix.Length).Split('|');
            return true;
        }
        public const string AimPrefix = "AIM ";

        /// <summary>
        /// Which slots a Sniper card was aimed at, decided by the client that owns it.
        /// </summary>
        public static string Aim(int attackerSlot, int[] targets)
        {
            return AimPrefix + attackerSlot + " " + string.Join(",", System.Array.ConvertAll(targets, t => t.ToString()));
        }

        public static bool TryParseAim(string msg, out int attackerSlot, out int[] targets)
        {
            attackerSlot = -1;
            targets = null;
            if (msg == null || !msg.StartsWith(AimPrefix)) return false;

            string[] parts = msg.Substring(AimPrefix.Length).Split(' ');
            if (parts.Length != 2 || !int.TryParse(parts[0], out attackerSlot)) return false;

            string[] raw = parts[1].Split(',');
            var list = new System.Collections.Generic.List<int>();
            foreach (string r in raw)
                if (int.TryParse(r, out int t)) list.Add(t);

            if (list.Count == 0) return false;
            targets = list.ToArray();
            return true;
        }

        public const string LatchPrefix = "LATCH ";

        /// <summary>
        /// Which card a latcher latched onto, decided by whoever owns it. The side is from
        /// the sender's point of view - P is their half - since the receiver's are flipped.
        /// </summary>
        public static string LatchTarget(bool sendersOwnSide, int slotIndex)
        {
            return LatchPrefix + (sendersOwnSide ? "P" : "O") + " " + slotIndex;
        }

        public static bool TryParseLatch(string msg, out bool sendersOwnSide, out int slotIndex)
        {
            sendersOwnSide = false;
            slotIndex = -1;
            if (msg == null || !msg.StartsWith(LatchPrefix)) return false;

            string[] parts = msg.Substring(LatchPrefix.Length).Split(' ');
            if (parts.Length != 2) return false;
            if (parts[0] != "P" && parts[0] != "O") return false;
            if (!int.TryParse(parts[1], out slotIndex)) return false;

            sendersOwnSide = parts[0] == "P";
            return true;
        }

        public const string Won  = "OVER WON";    // sender is telling us THEY won
        public const string Lost = "OVER LOST";

        public static string Play(string cardName, int slotIndex) => $"PLAY {cardName} {slotIndex}";

        public static bool TryParsePlay(string msg, out string cardName, out int slotIndex)
        {
            cardName = null;
            slotIndex = -1;
            if (msg == null || !msg.StartsWith("PLAY ")) return false;

            string[] parts = msg.Split(' ');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[2], out slotIndex)) return false;

            cardName = parts[1];
            return true;
        }
    }
}
