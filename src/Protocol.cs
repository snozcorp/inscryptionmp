namespace InscryptionMP
{
    /// <summary>Line protocol. Deliberately human-readable so the log IS the debugger.</summary>
    public static class Protocol
    {
        public const string EndTurn = "END";
        public const string Hello = "HELLO 1";

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
