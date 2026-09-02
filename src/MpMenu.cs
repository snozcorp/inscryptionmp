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
        private GUIStyle _chip, _label, _header, _field, _button, _dim;
        private Texture2D _panelBg, _chipBg, _accent;

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }
        private Rect _rect = new Rect(24f, 24f, 400f, 0f);

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

            _panelBg = Solid(new Color(0.06f, 0.05f, 0.04f, 0.97f));
            _chipBg  = Solid(new Color(0.06f, 0.05f, 0.04f, 0.88f));
            _accent  = Solid(new Color(0.85f, 0.62f, 0.25f, 1f));

            _chip = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
                padding = new RectOffset(8, 8, 4, 4),
            };
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = false,
                normal = { textColor = new Color(0.96f, 0.94f, 0.88f) },
            };
            _dim = new GUIStyle(_label) { fontSize = 13, normal = { textColor = new Color(0.62f, 0.59f, 0.53f) } };
            _header = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.80f, 0.38f) },
            };
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                padding = new RectOffset(8, 8, 6, 6),
                normal = { textColor = Color.white, background = Solid(new Color(0.15f, 0.14f, 0.12f, 1f)) },
                focused = { textColor = Color.white, background = Solid(new Color(0.20f, 0.18f, 0.15f, 1f)) },
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                padding = new RectOffset(12, 12, 9, 9),
                normal   = { textColor = new Color(0.96f, 0.94f, 0.88f), background = Solid(new Color(0.18f, 0.16f, 0.13f, 1f)) },
                hover    = { textColor = Color.white,                    background = Solid(new Color(0.30f, 0.26f, 0.19f, 1f)) },
                active   = { textColor = Color.white,                    background = Solid(new Color(0.42f, 0.34f, 0.20f, 1f)) },
            };
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
            GUI.DrawTexture(_rect, _panelBg);
            _rect = GUILayout.Window(0x4D50, _rect, DrawWindow, GUIContent.none, GUIStyle.none);
        }

        private void DrawChip()
        {
            string text = $"MP: {Net.StatusLine}    [F7] menu";
            var size = _chip.CalcSize(new GUIContent(text));
            var r = new Rect(10f, 10f, size.x + 16f, size.y + 8f);

            GUI.DrawTexture(r, _chipBg);
            GUI.DrawTexture(new Rect(r.x, r.y, 3f, r.height),
                            Net.Connected ? _accent : Texture2D.whiteTexture);

            GUI.Label(r, text, _chip);
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(10f);
            GUILayout.Label("  INSCRYPTION ONLINE", _header);
            GUILayout.Space(2f);
            GUILayout.Label($"  {Net.StatusLine}", Net.Connected ? _label : _dim);
            GUILayout.Space(12f);

            GUI.enabled = !Net.Connected && !VersusMode.InMatch;

            GUILayout.Label("  OPPONENT ADDRESS", _dim);
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
                GUILayout.Label($"  Last match: {VersusMode.LastResult}", _label);
            }

            GUILayout.Space(6f);
            GUILayout.Label("  F7 menu    F8 start    F12 abort", _dim);
            GUILayout.Space(4f);

            GUI.DragWindow();
        }
    }
}
