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

        public static Rules Desired => _desired;

        /// <summary>Whether this client is the one that decides.</summary>
        public static bool WeDecide => !Net.Connected || Net.IsHost;

        /// <summary>
        /// True when a rule we asked for is being ignored because the opponent's build
        /// predates match rules and would play under the defaults regardless.
        /// </summary>
        public static bool DowngradedForPeer =>
            Net.Connected && Net.IsHost && !Net.PeerSpeaksV5 && !_desired.SameAs(Rules.Default);

        /// <summary>What a match started right now would be played under.</summary>
        public static Rules Effective
        {
            get
            {
                if (!Net.Connected) return _desired;

                // A peer that cannot be told the rules cannot play by them, and a match
                // where one deck draws in order and the other doesn't is worse than one
                // where neither does.
                if (!Net.PeerSpeaksV5) return Rules.Default;

                if (Net.IsHost) return _desired;
                return _fromHost ?? Rules.Default;
            }
        }

        /// <summary>The rules of the match that is actually running.</summary>
        public static Rules Live => VersusMode.InMatch ? _live : Effective;

        /// <summary>Fixes the rules for the duration of a match.</summary>
        public static void Lock()
        {
            _live = Effective;
            Trace.Info($"[rules] match locked to {_live.Encode()}");
        }

        /// <summary>Sets a rule as the host. Returns true if anything changed.</summary>
        public static bool SetDraw(CardDraw draw)
        {
            if (!WeDecide) return false;
            if (_desired.Draw == draw) return false;

            _desired.Draw = draw;
            Persist();
            Trace.Info($"[rules] card draw is now {_desired.DrawName}");

            // Nobody agreed to these. Both sides step out of ready so the match that
            // starts is the one both players last looked at.
            Lobby.NoteRulesChanged();
            return true;
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
            Net.PeerSpeaksV5 ? Protocol.RulesLine(_desired.Encode()) : null;
    }
}
