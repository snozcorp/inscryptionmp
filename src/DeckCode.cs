using System;
using System.Collections.Generic;
using System.Text;

namespace InscryptionMP
{
    /// <summary>
    /// A deck as one line of text, for pasting into a chat: "IODECK1-<act>-<base64url>".
    /// The payload is the deck file's own lines, sigils included. Whether each card
    /// exists and is legal is the importer's business - this only gets the text across.
    /// </summary>
    public static class DeckCode
    {
        public const string Prefix = "IODECK1-";

        /// <summary>Far past any deck size rule; just stops a junk paste becoming a deck.</summary>
        public const int MaxEntries = 200;

        public static string Encode(MatchAct act, IList<string> entries)
        {
            string body = string.Join("\n", new List<string>(entries).ToArray());
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(body))
                                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return Prefix + (int)act + "-" + b64;
        }

        public static bool TryDecode(string code, out MatchAct act, out List<string> entries)
        {
            act = MatchAct.Act1;
            entries = null;
            if (string.IsNullOrEmpty(code)) return false;

            code = code.Trim();
            if (!code.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            string rest = code.Substring(Prefix.Length);
            int dash = rest.IndexOf('-');
            if (dash != 1 || !int.TryParse(rest.Substring(0, 1), out int n)) return false;
            if (!Enum.IsDefined(typeof(MatchAct), n)) return false;

            string b64 = rest.Substring(2).Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 1: return false;
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }

            string body;
            try { body = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
            catch (FormatException) { return false; }

            var list = new List<string>();
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.IndexOf(' ') >= 0) return false;   // names and sigils never have spaces
                list.Add(line);
            }
            if (list.Count == 0 || list.Count > MaxEntries) return false;

            act = (MatchAct)n;
            entries = list;
            return true;
        }
    }
}
