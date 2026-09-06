using System;

namespace InscryptionMP
{
    public enum NoticeKind
    {
        /// <summary>Something happened. Fades on its own.</summary>
        Info,
        /// <summary>Something is happening and hasn't finished. Stays until replaced or cleared.</summary>
        Busy,
        /// <summary>It worked. Fades.</summary>
        Good,
        /// <summary>It didn't work, and the reason matters. Stays much longer.</summary>
        Bad,
    }

    /// <summary>The one line of text that tells the player what just happened.</summary>
    public static class Notice
    {
        private const int FadeMs = 7000;
        private const int FadeMsBad = 20000;

        public static string Text { get; private set; }
        public static NoticeKind Kind { get; private set; }

        /// <summary>When the notice was set, as a tick count rather than a DateTime.</summary>
        private static volatile int _setAt;

        public static void Say(string text)  => Set(text, NoticeKind.Info);
        public static void Busy(string text) => Set(text, NoticeKind.Busy);
        public static void Good(string text) => Set(text, NoticeKind.Good);

        /// <summary>A failure, with the reason. Logged too, because these are the ones worth keeping.</summary>
        public static void Bad(string text)
        {
            Set(text, NoticeKind.Bad);
            Trace.Warn("[ui] " + text);
        }

        public static void Clear()
        {
            Text = null;
            Kind = NoticeKind.Info;
        }

        /// <summary>Clears only if the current notice is still the busy one we put up.</summary>
        public static void ClearIfBusy()
        {
            if (Kind == NoticeKind.Busy) Clear();
        }

        private static void Set(string text, NoticeKind kind)
        {
            Text = text;
            Kind = kind;
            _setAt = Environment.TickCount;
        }

        /// <summary>
        /// Whether there is anything worth showing. Busy notices never expire on their own -
        /// something is genuinely still in progress and saying so until it ends is the point.
        /// </summary>
        public static bool Visible
        {
            get
            {
                if (string.IsNullOrEmpty(Text)) return false;
                if (Kind == NoticeKind.Busy) return true;

                int life = Kind == NoticeKind.Bad ? FadeMsBad : FadeMs;

                // Unchecked subtraction so this still behaves when TickCount wraps.
                return unchecked(Environment.TickCount - _setAt) < life;
            }
        }
    }
}
