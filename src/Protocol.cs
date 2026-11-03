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
        ///
        /// 4 added the lobby negotiation - VOTE and READY - so both players agree on an act
        /// and both commit before anything loads.
        ///
        /// 5 added RULES, the host's house rules for the match, plus NAME, CHAT and EMOTE.
        /// The rules payload is a key=value list, so a later rule is a key an older build
        /// skips rather than another version.
        ///
        /// 6 (mod 2.0) added SCALE and changed what both sides simulate - sigils that only
        /// help their owner, the win post in combat - so it can't be mixed with older ones.
        /// </summary>
        public const int Version = 6;

        /// <summary>
        /// The oldest protocol we can still play against. 2.0 fixed how both clients run
        /// the same combat; a 1.x peer would put every one of those desyncs straight back.
        /// </summary>
        public const int MinCompatible = 6;

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

        public const string VotePrefix = "VOTE ";
        public const string ReadyPrefix = "READY ";

        /// <summary>
        /// Which act the sender wants to play. Both sides run their own battle on their own
        /// table, so a match is only coherent if they picked the same one.
        /// </summary>
        public static string Vote(MatchAct act) => VotePrefix + (int)act;

        public static bool TryParseVote(string msg, out MatchAct act)
        {
            act = MatchAct.Act1;
            if (msg == null || !msg.StartsWith(VotePrefix)) return false;
            if (!int.TryParse(msg.Substring(VotePrefix.Length), out int n)) return false;
            if (n < 1 || n > 3) return false;
            act = (MatchAct)n;
            return true;
        }

        /// <summary>Whether the sender has committed to starting the act they voted for.</summary>
        public static string Ready(bool ready) => ReadyPrefix + (ready ? "1" : "0");

        public static bool TryParseReady(string msg, out bool ready)
        {
            ready = false;
            if (msg == null || !msg.StartsWith(ReadyPrefix)) return false;

            string value = msg.Substring(ReadyPrefix.Length).Trim();
            if (value != "0" && value != "1") return false;
            ready = value == "1";
            return true;
        }

        public const string RulesPrefix = "RULES ";

        /// <summary>
        /// The house rules the host is running. A key=value list rather than fixed fields,
        /// so a rule the receiver has never heard of is one it skips.
        /// </summary>
        public static string RulesLine(string payload) => RulesPrefix + payload;

        public static bool TryParseRules(string msg, out string payload)
        {
            payload = null;
            if (msg == null || !msg.StartsWith(RulesPrefix)) return false;
            payload = msg.Substring(RulesPrefix.Length).Trim();
            return true;
        }

        public const string NamePrefix = "NAME ";
        public const string ChatPrefix = "CHAT ";
        public const string EmotePrefix = "EMOTE ";

        /// <summary>What the sender would like to be called in the lobby.</summary>
        public static string NameLine(string name) => NamePrefix + name;

        public static bool TryParseName(string msg, out string name)
        {
            name = null;
            if (msg == null || !msg.StartsWith(NamePrefix)) return false;
            name = msg.Substring(NamePrefix.Length).Trim();
            return name.Length > 0;
        }

        /// <summary>Something the sender typed. The rest of the line, spaces and all.</summary>
        public static string ChatLine(string text) => ChatPrefix + text;

        public static bool TryParseChat(string msg, out string text)
        {
            text = null;
            if (msg == null || !msg.StartsWith(ChatPrefix)) return false;
            text = msg.Substring(ChatPrefix.Length);
            return text.Length > 0;
        }

        /// <summary>
        /// One of the fixed phrases, by position. Sent as a number so both players read it
        /// in their own build's wording rather than the sender's.
        /// </summary>
        public static string EmoteLine(int index) => EmotePrefix + index;

        public static bool TryParseEmote(string msg, out int index)
        {
            index = -1;
            return msg != null
                   && msg.StartsWith(EmotePrefix)
                   && int.TryParse(msg.Substring(EmotePrefix.Length).Trim(), out index);
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

        /// <summary>
        /// How a match ended, stated from the *receiver's* side: "OVER WON" means the
        /// sender is telling you that you won, because they lost or walked out.
        ///
        /// Which way round these read has always been what every sender meant - conceding
        /// sends YouWon, and the comment at that call site says "they won; we left" - but
        /// the receiver read them the other way and turned each one into its opposite. The
        /// wire strings are unchanged, so a 1.4 peer's report is still understood; it is
        /// only their own screen that keeps the old build's inverted answer.
        /// </summary>
        public const string YouWon  = "OVER WON";
        public const string YouLost = "OVER LOST";

        public const string ScalePrefix = "SCALE ";

        /// <summary>
        /// The sender's balance (their damage dealt minus taken), sent just before END. Both
        /// sides have resolved exactly the same combats by then, so the receiver takes it as
        /// the truth - the sender defended the last combat with both boards freshly synced.
        /// </summary>
        public static string Scale(int balance) => ScalePrefix + balance;

        public static bool TryParseScale(string msg, out int balance)
        {
            balance = 0;
            return msg != null
                   && msg.StartsWith(ScalePrefix)
                   && int.TryParse(msg.Substring(ScalePrefix.Length).Trim(), out balance);
        }

        public const string StatePrefix = "STATE ";

        /// <summary>
        /// What the sender holds that cards read their stats from: hand size, bones and
        /// sacrifices this turn. Sent with BOARD so the receiver's copies of those cards
        /// count the sender's resources rather than its own.
        /// </summary>
        public static string State(int hand, int bones, int sacrifices) =>
            StatePrefix + hand + " " + bones + " " + sacrifices;

        public static bool TryParseState(string msg, out int hand, out int bones, out int sacrifices)
        {
            hand = bones = sacrifices = 0;
            if (msg == null || !msg.StartsWith(StatePrefix)) return false;

            string[] parts = msg.Substring(StatePrefix.Length).Trim().Split(' ');
            return parts.Length == 3
                   && int.TryParse(parts[0], out hand) && hand >= 0
                   && int.TryParse(parts[1], out bones) && bones >= 0
                   && int.TryParse(parts[2], out sacrifices) && sacrifices >= 0;
        }

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
