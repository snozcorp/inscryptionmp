using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Keeps the game running while its window is not the one in front. Unity stops updating
    /// an unfocused player unless told otherwise, so alt-tabbing stops a networked client
    /// dead - no card the opponent plays arrives and the turn never passes, while from their
    /// side you have simply stopped playing without disconnecting. Put back the way the game
    /// wanted it as soon as there is no peer to keep up with.
    /// </summary>
    internal static class Background
    {
        private static bool _known;
        private static bool _original;
        private static bool _wanted;

        /// <summary>Set for the autopilot, which drives two clients on one desktop.</summary>
        private static bool _pinned;

        private static float _nextBeat;

        /// <summary>Pumped from the menu's update, which runs in whatever scene we are in.</summary>
        public static void Tick()
        {
            Remember();

            bool wanted = _pinned || Net.Running || Net.Connected || VersusMode.InMatch;

            // Re-asserted rather than only set on the edge: the game sets this for itself
            // during startup, which lands after our first pass and quietly puts it back.
            if (wanted && !Application.runInBackground) Application.runInBackground = true;

            if (wanted != _wanted)
            {
                _wanted = wanted;
                if (!wanted) Application.runInBackground = _original;
                Trace.Info(wanted
                    ? "[focus] keeping the game awake while there is an opponent to keep up with"
                    : $"[focus] no opponent - back to the game's own setting ({_original})");
            }

            Heartbeat();
        }

        private static void Remember()
        {
            if (_known) return;
            _original = Application.runInBackground;
            _known = true;
            Trace.Info($"[focus] the game runs in the background by default: {_original}");
        }

        /// <summary>Never turned off again: a test client has no focus to wait for.</summary>
        public static void ForceOn()
        {
            Remember();
            _pinned = true;
            _wanted = true;
            Application.runInBackground = true;
            Trace.Info("[focus] pinned awake for the autopilot");
        }

        /// <summary>
        /// Under autopilot only. A frame counter that stops climbing is the difference
        /// between "the client is stuck" and "the client is not being updated at all", and
        /// they look identical from outside.
        /// </summary>
        private static void Heartbeat()
        {
            if (!Autopilot.Active) return;
            if (Time.realtimeSinceStartup < _nextBeat) return;

            _nextBeat = Time.realtimeSinceStartup + 5f;
            Trace.Info($"[focus] frame {Time.frameCount}, focused={Application.isFocused}, " +
                       $"background={Application.runInBackground}");
        }
    }
}
