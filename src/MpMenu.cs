using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// The mod's front end: a togglable panel for hosting, joining, and starting a match,
    /// plus a compact status chip when it's closed.
    ///
    /// Drawn with IMGUI rather than Inscryption's own UI system - matching the game's
    /// menu cards is a large detour, and this needs to be usable from the main menu, the
    /// map, and mid-match alike.
    /// </summary>
    internal class MpMenu : MonoBehaviour
    {
        internal static string Ip = "127.0.0.1";
        internal static string PortText = Net.DefaultPort.ToString();

        private bool _open;
        private GUIStyle _chip, _label, _header, _field, _button;
        private Rect _rect = new Rect(20f, 20f, 340f, 0f);

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7)) _open = !_open;

            // Keep the shortcuts working for anyone who's learned them.
            if (Input.GetKeyDown(KeyCode.F8))  VersusMode.StartAnywhere(this);
            if (Input.GetKeyDown(KeyCode.F9))  Net.Host(ParsedPort);
            if (Input.GetKeyDown(KeyCode.F10)) Net.Join(Ip, ParsedPort);
            if (Input.GetKeyDown(KeyCode.F12)) VersusMode.Abort(this);

            VersusMode.TickPendingStart(this);

            if (VersusMode.InMatch && !Net.Connected)
                VersusMode.Finish(this, playerWon: true, reason: "peer disconnected");
        }

        private static int ParsedPort =>
            int.TryParse(PortText, out int p) && p > 0 && p < 65536 ? p : Net.DefaultPort;

        private void EnsureStyles()
        {
            if (_chip != null) return;

            _chip = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
                padding = new RectOffset(8, 8, 4, 4),
            };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(0.88f, 0.86f, 0.80f) } };
            _header = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.55f) },
            };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 13, padding = new RectOffset(10, 10, 6, 6) };
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (!_open)
            {
                DrawChip();
                return;
            }

            _rect.height = 0f;   // let GUILayout size it
            _rect = GUILayout.Window(0x4D50, _rect, DrawWindow, GUIContent.none, GUI.skin.box);
        }

        private void DrawChip()
        {
            string text = $"MP: {Net.StatusLine}    [F7] menu";
            var size = _chip.CalcSize(new GUIContent(text));
            var r = new Rect(10f, 10f, size.x + 16f, size.y + 8f);

            var prev = GUI.color;
            GUI.color = Net.Connected ? new Color(0f, 0.4f, 0f, 0.75f) : new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(r, text, _chip);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(4f);
            GUILayout.Label("INSCRYPTION  ONLINE", _header);
            GUILayout.Space(6f);

            GUILayout.Label(Net.Connected ? $"Status: {Net.StatusLine}" : $"Status: {Net.StatusLine}", _label);
            GUILayout.Space(8f);

            GUI.enabled = !Net.Connected && !VersusMode.InMatch;

            GUILayout.Label("Opponent address", _label);
            GUILayout.BeginHorizontal();
            Ip = GUILayout.TextField(Ip, 64, _field);
            GUILayout.Label(":", _label, GUILayout.Width(8f));
            PortText = GUILayout.TextField(PortText, 5, _field, GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", _button)) Net.Host(ParsedPort);
            if (GUILayout.Button("Join", _button)) Net.Join(Ip, ParsedPort);
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            GUILayout.Space(8f);

            GUI.enabled = Net.Connected && !VersusMode.InMatch && !VersusMode.PendingStart;
            if (GUILayout.Button("Start Match", _button)) VersusMode.StartAnywhere(this);
            GUI.enabled = true;

            if (VersusMode.InMatch)
            {
                GUILayout.Space(4f);
                if (GUILayout.Button("Abort Match", _button)) VersusMode.Abort(this);
            }

            if (Net.Connected || Net.Running)
            {
                GUILayout.Space(4f);
                if (GUILayout.Button("Disconnect", _button)) Net.Shutdown();
            }

            if (VersusMode.LastResult != null && !VersusMode.InMatch)
            {
                GUILayout.Space(6f);
                GUILayout.Label($"Last match: {VersusMode.LastResult}", _label);
            }

            GUILayout.Space(6f);
            GUILayout.Label("F7 menu   F8 start   F12 abort", _label);
            GUILayout.Space(4f);

            GUI.DragWindow();
        }
    }
}
