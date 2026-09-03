namespace InscryptionMP
{
    /// <summary>Line protocol. Deliberately human-readable so the log IS the debugger.</summary>
    public static class Protocol
    {
        public const string EndTurn = "END";
        /// <summary>
        /// Bumped whenever the wire format changes. Two clients on different protocol
        /// versions connect happily and then desync in confusing ways, so they refuse
        /// each other up front instead.
        /// </summary>
        public const int Version = 2;

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

        /// <summary>Authoritative snapshot of the sender's four player slots.</summary>
        public static string Board(string[] slotCardNames)
        {
            return BoardPrefix + string.Join("|", slotCardNames);
        }

        public static bool TryParseBoard(string msg, out string[] slots)
        {
            slots = null;
            if (msg == null || !msg.StartsWith(BoardPrefix)) return false;
            slots = msg.Substring(BoardPrefix.Length).Split('|');
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
