using System;
using BepInEx.Configuration;

namespace InscryptionMP
{
    /// <summary>
    /// Which rules apply, and who gets to say. Both clients run their own battle against
    /// their own deck, so a rule that changes how a deck behaves is only fair if both are
    /// playing under it - which means one of them has to decide. The host does, and the
    /// other player sees the rules before committing to the match.
    /// </summary>
    public static class MatchRules
    {
        private static ConfigEntry<string> _saved;

        /// <summary>What the host has set up, remembered between sessions.</summary>
        private static Rules _desired = Rules.Default;

        /// <summary>What the host told us, or null until they have.</summary>
        private static Rules? _fromHost;

        /// <summary>The rules the running match locked in when it started.</summary>
        private static Rules _live = Rules.Default;

        public static void Bind(ConfigEntry<string> entry)
        {
            _saved = entry;
            _desired = Rules.Decode(entry.Value);
            Trace.Info($"[rules] loaded: {_desired.Encode()}");
        }

        /// <summary>
        /// Takes a whole set of rules in one go. The config file arrives this way and so
        /// does a scripted run; a player turns the dials one at a time instead.
        /// </summary>
        public static void Adopt(string payload)
        {
            if (!WeDecide) return;

            _desired = Rules.Decode(payload);
            Trace.Info($"[rules] adopted: {_desired.Encode()}");
            Changed();
        }

        public static Rules Desired => _desired;

        /// <summary>Whether this client is the one that decides.</summary>
        public static bool WeDecide => !Net.Connected || Net.IsHost;

        /// <summary>Whether the host has custom rules switched on at all.</summary>
        public static bool CustomOn => Effective.Custom;

        /// <summary>
        /// True when a rule we asked for is being ignored because the opponent's build
        /// predates match rules and would play under the defaults regardless.
        /// </summary>
        public static bool DowngradedForPeer =>
            Net.Connected && Net.IsHost && !Net.PeerSpeaksV5 && _desired.Custom;

        /// <summary>What a match started right now would be played under.</summary>
        public static Rules Effective
        {
            get
            {
                Rules mine = _desired.Custom ? _desired : Keeping(Rules.Default, _desired.Custom);

                if (!Net.Connected) return mine;

                // A peer that cannot be told the rules cannot play by them, and a match
                // where one deck draws in order and the other doesn't is worse than one
                // where neither does.
                if (!Net.PeerSpeaksV5) return Rules.Default;

                if (Net.IsHost) return mine;
                return _fromHost ?? Rules.Default;
            }
        }

        /// <summary>Vanilla rules, but remembering whether the switch is on.</summary>
        private static Rules Keeping(Rules b, bool custom)
        {
            b.Custom = custom;
            return b;
        }

        /// <summary>The rules of the match that is actually running.</summary>
        public static Rules Live => VersusMode.InMatch ? _live : Effective;

        /// <summary>Fixes the rules for the duration of a match.</summary>
        public static void Lock()
        {
            _live = Effective;
            Trace.Info($"[rules] match locked to {_live.Encode()}");
        }

        // ---------------------------------------------------------------- the host's side

        /// <summary>Turns the whole custom set on or off without forgetting what it holds.</summary>
        public static void SetCustom(bool on)
        {
            if (!WeDecide || _desired.Custom == on) return;

            _desired.Custom = on;
            Trace.Info("[rules] custom rules " + (on ? "on" : "off"));
            Changed();
        }

        /// <summary>Steps one dial along. Returns false when it isn't ours to turn.</summary>
        public static bool Cycle(string key, int delta)
        {
            if (!WeDecide) return false;

            RuleBook.Dial dial = RuleBook.Find(key);
            if (dial == null) return false;

            int count = dial.Choices.Length;
            int next = (dial.IndexOf(_desired) + delta % count + count) % count;

            _desired = dial.WithIndex(_desired, next);
            _desired.Custom = true;   // turning a dial is asking for custom rules
            Trace.Info($"[rules] {dial.Label} is now {dial.Choices[next]}");
            Changed();
            return true;
        }

        /// <summary>Puts every dial back to the game's own number.</summary>
        public static void ResetToDefaults()
        {
            if (!WeDecide) return;

            bool custom = _desired.Custom;
            _desired = Keeping(Rules.Default, custom);
            Trace.Info("[rules] reset to the game's own rules");
            Changed();
        }

        private static void Changed()
        {
            Persist();

            // Nobody agreed to these. Both sides step out of ready so the match that
            // starts is the one both players last looked at.
            Lobby.NoteRulesChanged();
        }

        private static void Persist()
        {
            try
            {
                if (_saved != null) _saved.Value = _desired.Encode();
            }
            catch (Exception e)
            {
                Trace.Warn($"[rules] could not save: {e.Message}");
            }
        }

        // -------------------------------------------------------------- the other side

        /// <summary>The host has told us what we're playing.</summary>
        public static void NoteHostRules(string payload)
        {
            Rules incoming = Rules.Decode(payload);

            // The host's own copy is authoritative for them; hearing it back would let a
            // stray message overwrite what they just set.
            if (Net.Connected && Net.IsHost)
            {
                Trace.Warn("[rules] ignoring rules from a peer while hosting");
                return;
            }

            bool changed = !_fromHost.HasValue || !_fromHost.Value.SameAs(incoming);
            _fromHost = incoming;
            if (!changed) return;

            Trace.Info($"[rules] host set: {incoming.Encode()}");
            Lobby.NoteRulesChanged();
        }

        /// <summary>A new opponent's rules are their own.</summary>
        public static void ForgetPeer() => _fromHost = null;

        /// <summary>The line the wire carries, or null when the peer can't read it.</summary>
        public static string Announcement =>
            Net.PeerSpeaksV5 ? Protocol.RulesLine(Effective.Encode()) : null;
    }
}
