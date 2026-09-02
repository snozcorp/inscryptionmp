using DiskCardGame;

namespace InscryptionMP
{
    /// <summary>
    /// Hides campaign scenery that has no business being visible in a versus match.
    ///
    /// We host matches inside the Act 1 scene, so map furniture comes along for the ride -
    /// most visibly the player figurine, which stands on the board mid-match.
    /// </summary>
    internal static class TableProps
    {
        private static bool _markerHidden;

        public static void HidePlayerMarker()
        {
            var marker = PlayerMarker.Instance;
            if (marker == null || _markerHidden) return;

            marker.gameObject.SetActive(false);
            _markerHidden = true;
            Trace.Info("[props] hid the player figurine");
        }

        public static void RestorePlayerMarker()
        {
            if (!_markerHidden) return;

            var marker = PlayerMarker.Instance;
            if (marker != null) marker.gameObject.SetActive(true);
            _markerHidden = false;
            Trace.Info("[props] restored the player figurine");
        }
    }
}
