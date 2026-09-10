using System;
using System.Collections.Generic;

namespace InscryptionMP
{
    /// <summary>
    /// Who you are playing, and what the two of you have said.
    ///
    /// Typing is only offered where the panel is open, because Inscryption reads the
    /// keyboard straight out of Update and a text field on the board would be fighting the
    /// game for every keystroke. During a match there are fixed phrases on the number keys
    /// instead, which is the right shape for a turn-based card game anyway.
    /// </summary>
    internal static class Chat
    {
        /// <summary>What the number keys say, in order, while a match is running.</summary>
        public static readonly string[] Emotes =
        {
            "Good luck!",
            "Nice play.",
            "One second...",
            "Sorry!",
            "Your turn.",
            "Good game.",
        };

        public const int MaxLength = 120;
        private const int Keep = 60;

        internal struct Line
        {
            public string Who;
            public string Text;
            public float At;

            /// <summary>Ours, theirs, or the mod talking.</summary>
            public bool Mine;
            public bool System;
        }

        private static readonly List<Line> Lines = new List<Line>();

        /// <summary>
        /// The peer's lines arrive on the transport's reader thread while the panel is
        /// walking the list to draw it, so they wait here and are taken up a frame later
        /// by <see cref="Pump"/> rather than being appended from under the drawing.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentQueue<Line> Waiting =
            new System.Collections.Concurrent.ConcurrentQueue<Line>();

        /// <summary>Pumped every frame from the menu, on Unity's own thread.</summary>
        public static void Pump()
        {
            Line line;
            while (Waiting.TryDequeue(out line)) Add(line);
        }

        public static IList<Line> All => Lines;

        public static int Count => Lines.Count;

        /// <summary>The newest line, or null if nothing has been said.</summary>
        public static Line? Latest => Lines.Count == 0 ? (Line?)null : Lines[Lines.Count - 1];

        private static void Add(Line line)
        {
            Lines.Add(line);
            if (Lines.Count > Keep) Lines.RemoveRange(0, Lines.Count - Keep);
        }

        private static float Now => UnityEngine.Time.realtimeSinceStartup;

        /// <summary>Strips anything that would break the line protocol or the layout.</summary>
        internal static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '\n' || c == '\r' || c == '\t') { sb.Append(' '); continue; }
                if (char.IsControl(c) || char.IsSurrogate(c)) continue;

                // Format, private-use and unassigned code points draw as nothing, so
                // a name made of them is an empty gap where a player should be. Steam
                // hands back exactly that when its API never initialised.
                switch (global::System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c))
                {
                    case global::System.Globalization.UnicodeCategory.Format:
                    case global::System.Globalization.UnicodeCategory.PrivateUse:
                    case global::System.Globalization.UnicodeCategory.OtherNotAssigned:
                        continue;
                }

                sb.Append(c);
            }

            string clean = sb.ToString().Trim();
            return clean.Length > MaxLength ? clean.Substring(0, MaxLength) : clean;
        }

        /// <summary>Says something to the opponent. Returns false if there was nothing to say.</summary>
        public static bool Say(string text)
        {
            string clean = Clean(text);
            if (clean.Length == 0) return false;

            Add(new Line { Who = Player.LocalName, Text = clean, At = Now, Mine = true });
            Net.Send(Protocol.ChatLine(clean));
            return true;
        }

        /// <summary>Sends one of the fixed phrases. Index is zero-based.</summary>
        public static bool Emote(int index)
        {
            if (index < 0 || index >= Emotes.Length) return false;
            if (!Net.Connected) return false;

            Add(new Line { Who = Player.LocalName, Text = Emotes[index], At = Now, Mine = true });
            Net.Send(Protocol.EmoteLine(index));
            return true;
        }

        /// <summary>The peer said something. Called off the network thread.</summary>
        public static void NoteIncoming(string text)
        {
            string clean = Clean(text);
            if (clean.Length == 0) return;

            Waiting.Enqueue(new Line { Who = Player.PeerName, Text = clean, At = Now });
            Trace.Info($"[chat] {Player.PeerName}: {clean}");
        }

        public static void NoteEmote(int index)
        {
            if (index < 0 || index >= Emotes.Length)
            {
                Trace.Warn($"[chat] peer sent emote {index}, which we don't have");
                return;
            }
            NoteIncoming(Emotes[index]);
        }

        /// <summary>A note from the mod itself - who joined, who left.</summary>
        public static void Note(string text)
        {
            Add(new Line { Who = "", Text = text, At = Now, System = true });
        }

        /// <summary>
        /// The last thing said, while it is still worth putting on screen over the board.
        /// </summary>
        public static Line? Fresh(float seconds)
        {
            Line? last = Latest;
            if (!last.HasValue || last.Value.System) return null;
            return Now - last.Value.At <= seconds ? last : null;
        }

        public static void Clear()
        {
            Lines.Clear();
            Line ignored;
            while (Waiting.TryDequeue(out ignored)) { }
        }
    }

    /// <summary>Names for the two seats, so a lobby has people in it rather than roles.</summary>
    internal static class Player
    {
        private static string _peer;
        private static BepInEx.Configuration.ConfigEntry<string> _chosen;

        public static void Bind(BepInEx.Configuration.ConfigEntry<string> entry) => _chosen = entry;

        /// <summary>
        /// What to call you: what you set, else what Steam calls you, else which seat you
        /// are in - two players both showing as "Player" is worse than no name at all.
        /// </summary>
        public static string LocalName
        {
            get
            {
                try
                {
                    string chosen = _chosen == null ? null : Chat.Clean(_chosen.Value);
                    if (Readable(chosen)) return Trim(chosen);

                    if (SteamTransport.Available)
                    {
                        string name = Chat.Clean(Steamworks.SteamFriends.GetPersonaName());
                        if (Readable(name)) return Trim(name);
                    }
                }
                catch (Exception e)
                {
                    Trace.Warn($"[player] could not read a name: {e.Message}");
                }
                return Net.Running || Net.Connected ? (Net.IsHost ? "Host" : "Guest") : "Player";
            }
        }

        private static string Trim(string name) =>
            name.Length > MaxName ? name.Substring(0, MaxName) : name;

        private const int MaxName = 24;

        /// <summary>
        /// Whether a name would actually show up. Something with no letter or digit in
        /// it is a blank space on the score bar rather than a player.
        /// </summary>
        private static bool Readable(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) return true;
            return false;
        }

        /// <summary>What the opponent calls themselves, once they have said.</summary>
        public static string PeerName => string.IsNullOrEmpty(_peer) ? "Opponent" : _peer;

        public static bool PeerNamed => !string.IsNullOrEmpty(_peer);

        public static void NotePeerName(string name)
        {
            string clean = Chat.Clean(name);
            if (!Readable(clean))
            {
                Trace.Warn("[player] the opponent's name has nothing readable in it - "
                           + $"keeping '{PeerName}'");
                return;
            }

            _peer = Trim(clean);
            Trace.Info($"[player] opponent is {_peer}");
        }

        public static void ForgetPeer() => _peer = null;
    }
}
