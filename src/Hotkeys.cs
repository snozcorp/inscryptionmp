using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Dev harness: F8 versus match, F9 host, F10 join, F11 status, F12 abort match.
    /// Also draws a small always-on status overlay - without it there is no in-game
    /// feedback at all and you cannot tell a working host from a dead one.
    /// </summary>
    internal class Hotkeys : MonoBehaviour
    {
        private GUIStyle _style;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))  Net.Host();
            if (Input.GetKeyDown(KeyCode.F10)) Net.Join("127.0.0.1");
            if (Input.GetKeyDown(KeyCode.F8))  VersusMode.StartAnywhere(this);
            VersusMode.TickPendingStart(this);
            if (Input.GetKeyDown(KeyCode.F12)) VersusMode.Abort(this);

            // If the peer vanishes while we're in a match, don't leave the player sat at a
            // table waiting on a turn that will never arrive.
            if (VersusMode.InMatch && !Net.Connected)
                VersusMode.Finish(this, playerWon: true, reason: "peer disconnected");

            if (Input.GetKeyDown(KeyCode.F11))
                Trace.Info($"[status] {Net.StatusLine}");
        }

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    normal = { textColor = Color.white },
                    padding = new RectOffset(8, 8, 4, 4),
                };
            }

            string text = $"MP: {Net.StatusLine}";
            string blocker = VersusMode.Blocker;
            if (blocker != null)                      text += $"   ({blocker})";
            else if (Net.Connected && !VersusMode.InMatch) text += "   [F8] start versus match";
            else if (VersusMode.InMatch)              text += "   (in match)   [F12] abort";
            if (!VersusMode.InMatch && VersusMode.LastResult != null)
                text += $"   last match: {VersusMode.LastResult}";
            var size = _style.CalcSize(new GUIContent(text));
            var rect = new Rect(10f, 10f, size.x + 16f, size.y + 8f);

            Color bg = Net.Connected ? new Color(0f, 0.4f, 0f, 0.75f)
                                     : new Color(0f, 0f, 0f, 0.65f);
            var prev = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(rect, text, _style);
        }
    }
}
