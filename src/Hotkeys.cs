using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// Dev harness: F8 start versus match, F9 host, F10 join localhost, F11 status.
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
            if (Input.GetKeyDown(KeyCode.F8))  VersusMode.Start(this);
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
            if (Net.Connected && !VersusMode.InMatch) text += "   [F8] start versus match";
            else if (VersusMode.InMatch)              text += "   (in match)";
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
