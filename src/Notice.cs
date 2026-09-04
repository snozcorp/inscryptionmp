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

    /// <summary>
    /// The one line of text that tells the player what just happened.
    ///
    /// Every dead-button bug in this mod has had the same shape: the action worked, or
    /// failed for a knowable reason, and the UI showed nothing either way. Find Games
    /// searched and found nothing; a direct Join failed with the reason sitting in the log
    /// file. Both looked identical to a button that does nothing.
    ///
    /// So anything the player triggers says what it is doing, and anything that fails says
    /// why, in words rather than silence.
    ///
    /// Deliberately uses DateTime rather than UnityEngine.Time: notices are set from the
    /// network threads, and Unity's time API is main-thread only.
    /// </summary>
    public static class Notice
    {
        private const double FadeSeconds = 7.0;
        private const double FadeSecondsBad = 20.0;

        public static string Text { get; private set; }
        public static NoticeKind Kind { get; private set; }

        private static DateTime _setAt = DateTime.MinValue;

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
            _setAt = DateTime.UtcNow;
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

                double life = Kind == NoticeKind.Bad ? FadeSecondsBad : FadeSeconds;
                return (DateTime.UtcNow - _setAt).TotalSeconds < life;
            }
        }
    }
}
