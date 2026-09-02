using Steamworks;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// The mod's front end: a centred panel for finding an opponent and starting a match,
    /// plus a compact status chip when it's closed.
    ///
    /// Steam is the primary path - lobbies give friend invites, NAT traversal and a
    /// browser for free. The direct address fields stay as a LAN and non-Steam fallback.
    /// </summary>
    internal class MpMenu : MonoBehaviour
    {
        internal static string Ip = "127.0.0.1";
        internal static string PortText = Net.DefaultPort.ToString();

        private const float PanelW = 480f;
        private float PanelH => _tab == 0 ? 500f : 640f;

        private bool _open;
        private Rect _rect;
        private Vector2 _lobbyScroll, _deckScroll, _poolScroll;
        private int _tab;   // 0 = play, 1 = deck

        private GUIStyle _chip, _label, _dim, _header, _section, _field, _button, _small;
        private Texture2D _panelBg, _chipBg, _accent, _rule;

        private void Update()
        {
            SteamTransport.Poll();

            if (Input.GetKeyDown(KeyCode.F7)) _open = !_open;
            if (Input.GetKeyDown(KeyCode.F8)) VersusMode.StartAnywhere(this);
            if (Input.GetKeyDown(KeyCode.F12)) VersusMode.Abort(this);

            VersusMode.TickPendingStart(this);

            // Nothing drains the inbox outside a match, so watch for the peer asking us
            // to start one. Without this, only the clicking player enters a battle.
            if (!VersusMode.InMatch && !VersusMode.PendingStart && Net.Connected)
            {
                while (Net.TryDequeue(out string msg))
                {
                    if (msg == Protocol.StartMatch)
                    {
                        Trace.Info("[versus] peer started a match - joining");
                        VersusMode.StartAnywhere(this, tellPeer: false);
                        break;
                    }
                }
            }

            if (VersusMode.InMatch && !Net.Connected)
                VersusMode.Finish(this, playerWon: true, reason: "peer disconnected");
        }

        private static int ParsedPort =>
            int.TryParse(PortText, out int p) && p > 0 && p < 65536 ? p : Net.DefaultPort;

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private void EnsureStyles()
        {
            if (_chip != null) return;

            _panelBg = Solid(new Color(0.06f, 0.05f, 0.04f, 0.98f));
            _chipBg = Solid(new Color(0.06f, 0.05f, 0.04f, 0.88f));
            _accent = Solid(new Color(0.85f, 0.62f, 0.25f, 1f));
            _rule = Solid(new Color(1f, 1f, 1f, 0.10f));

            _chip = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
                padding = new RectOffset(10, 10, 5, 5),
            };
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.96f, 0.94f, 0.88f) },
            };
            _dim = new GUIStyle(_label) { fontSize = 13, normal = { textColor = new Color(0.60f, 0.57f, 0.51f) } };
            _small = new GUIStyle(_label) { fontSize = 12, normal = { textColor = new Color(0.55f, 0.52f, 0.47f) } };
            _header = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.80f, 0.38f) },
            };
            _section = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.62f, 0.25f) },
            };
            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 15,
                padding = new RectOffset(8, 8, 6, 6),
                normal = { textColor = Color.white, background = Solid(new Color(0.15f, 0.14f, 0.12f, 1f)) },
                focused = { textColor = Color.white, background = Solid(new Color(0.21f, 0.19f, 0.16f, 1f)) },
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                padding = new RectOffset(12, 12, 9, 9),
                normal = { textColor = new Color(0.96f, 0.94f, 0.88f), background = Solid(new Color(0.18f, 0.16f, 0.13f, 1f)) },
                hover = { textColor = Color.white, background = Solid(new Color(0.31f, 0.26f, 0.18f, 1f)) },
                active = { textColor = Color.white, background = Solid(new Color(0.44f, 0.35f, 0.20f, 1f)) },
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

            float h = PanelH;
            _rect = new Rect((Screen.width - PanelW) * 0.5f,
                             (Screen.height - h) * 0.5f,
                             PanelW, h);
            GUI.Window(0x4D50, _rect, DrawWindow, GUIContent.none, GUIStyle.none);

            DrawCursor();
        }

        private void DrawChip()
        {
            string turn = VersusMode.InMatch
                ? (TurnOrder.IsMyTurn ? "  |  YOUR TURN" : "  |  opponent's turn")
                : "";
            string text = $"MP: {Net.StatusLine}{turn}    [F7] menu";
            var size = _chip.CalcSize(new GUIContent(text));
            var r = new Rect(10f, 10f, size.x + 16f, size.y + 8f);

            GUI.DrawTexture(r, _chipBg);
            bool myTurn = VersusMode.InMatch && TurnOrder.IsMyTurn;
            GUI.DrawTexture(new Rect(r.x, r.y, 3f, r.height),
                            myTurn ? _accent : (Net.Connected ? _rule : _accent));
            GUI.Label(r, text, _chip);
        }

        /// <summary>
        /// The game draws its own cursor into the 3D scene, which our panel then covers -
        /// so you cannot see what you are about to click. Draw a marker on top instead.
        /// </summary>
        private void DrawCursor()
        {
            Vector3 m = Input.mousePosition;
            float x = m.x;
            float y = Screen.height - m.y;   // GUI space is y-down

            if (!_rect.Contains(new Vector2(x, y))) return;

            const float len = 9f;
            const float thick = 2f;

            // Dark backing so the crosshair reads against any panel colour.
            GUI.DrawTexture(new Rect(x - len - 1f, y - 1f, len * 2f + 2f, thick + 2f), _panelBg);
            GUI.DrawTexture(new Rect(x - 1f, y - len - 1f, thick + 2f, len * 2f + 2f), _panelBg);

            GUI.DrawTexture(new Rect(x - len, y, len * 2f, thick), _accent);
            GUI.DrawTexture(new Rect(x, y - len, thick, len * 2f), _accent);
        }

        private void Rule()
        {
            GUILayout.Space(6f);
            GUILayout.Box(GUIContent.none, GUIStyle.none, GUILayout.Height(1f));
            var r = GUILayoutUtility.GetLastRect();
            GUI.DrawTexture(new Rect(r.x, r.y, PanelW - 28f, 1f), _rule);
            GUILayout.Space(6f);
        }

        private void DrawWindow(int id)
        {
            float h = PanelH;
            GUI.DrawTexture(new Rect(0f, 0f, PanelW, h), _accent);
            GUI.DrawTexture(new Rect(2f, 2f, PanelW - 4f, h - 4f), _panelBg);

            GUILayout.BeginArea(new Rect(14f, 12f, PanelW - 28f, h - 24f));

            GUILayout.Label("INSCRYPTION ONLINE", _header);
            GUILayout.Label(Net.StatusLine, Net.Connected ? _label : _dim);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_tab == 0 ? "> PLAY" : "PLAY", _button)) _tab = 0;
            if (GUILayout.Button(_tab == 1 ? "> DECK" : "DECK", _button)) _tab = 1;
            GUILayout.EndHorizontal();
            Rule();

            if (_tab == 1)
            {
                DrawDeckTab();
                GUILayout.EndArea();
                return;
            }

            bool idle = !Net.Connected && !VersusMode.InMatch;

            // ---- Steam ----
            GUILayout.Label("STEAM", _section);
            if (!SteamTransport.Available)
            {
                GUILayout.Label("Steam not detected - use a direct address below.", _small);
            }
            else
            {
                GUI.enabled = idle;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host Lobby", _button)) SteamTransport.HostLobby();
                if (GUILayout.Button("Find Games", _button)) SteamTransport.RefreshLobbies();
                GUILayout.EndHorizontal();

                if (SteamTransport.Lobbies.Count > 0)
                {
                    GUILayout.Space(4f);
                    _lobbyScroll = GUILayout.BeginScrollView(_lobbyScroll, GUILayout.Height(86f));
                    foreach (var lobby in SteamTransport.Lobbies)
                    {
                        if (GUILayout.Button(lobby.Value, _button))
                            SteamTransport.JoinLobby(lobby.Key);
                    }
                    GUILayout.EndScrollView();
                }
                GUI.enabled = true;
            }

            Rule();

            // ---- Direct / LAN ----
            GUILayout.Label("DIRECT  (LAN / non-Steam)", _section);
            GUI.enabled = idle;
            GUILayout.BeginHorizontal();
            Ip = GUILayout.TextField(Ip, 64, _field);
            GUILayout.Label(":", _label, GUILayout.Width(8f));
            PortText = GUILayout.TextField(PortText, 5, _field, GUILayout.Width(64f));
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", _button)) Net.Host(ParsedPort);
            if (GUILayout.Button("Join", _button)) Net.Join(Ip, ParsedPort);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            Rule();

            // ---- Match ----
            GUI.enabled = Net.Connected && !VersusMode.InMatch && !VersusMode.PendingStart && DeckStore.IsValid;
            if (GUILayout.Button("START MATCH", _button)) VersusMode.StartAnywhere(this);
            GUI.enabled = true;
            if (!DeckStore.IsValid)
                GUILayout.Label($"Your deck needs {DeckStore.MinCards}-{DeckStore.MaxCards} cards - see the DECK tab.", _small);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (VersusMode.InMatch && GUILayout.Button("Abort Match", _button)) VersusMode.Abort(this);
            if (Net.Running && GUILayout.Button("Disconnect", _button)) Net.Shutdown();
            GUILayout.EndHorizontal();

            if (VersusMode.LastResult != null && !VersusMode.InMatch)
                GUILayout.Label($"Last match: {VersusMode.LastResult}", _label);

            GUILayout.FlexibleSpace();
            GUILayout.Label("F7 menu    F8 start    F12 abort", _small);
            GUILayout.EndArea();
        }

        private void DrawDeckTab()
        {
            var deck = DeckStore.Deck;

            GUILayout.Label($"YOUR DECK   {deck.Count} / {DeckStore.MaxCards}", _section);
            if (!DeckStore.IsValid)
                GUILayout.Label($"Needs at least {DeckStore.MinCards} cards to play.", _small);

            // Collapse duplicates so the list reads as "3x Squirrel" rather than repeating.
            var counts = new System.Collections.Generic.List<string>();
            foreach (string name in deck)
                if (!counts.Contains(name)) counts.Add(name);

            _deckScroll = GUILayout.BeginScrollView(_deckScroll, GUILayout.Height(150f));
            if (counts.Count == 0)
                GUILayout.Label("Empty - add cards below.", _small);
            foreach (string name in counts)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{DeckStore.CountOf(name)}x  {name}", _label);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("-", _button, GUILayout.Width(34f))) { DeckStore.Remove(name); break; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            Rule();

            GUILayout.Label("CARD POOL", _section);
            _poolScroll = GUILayout.BeginScrollView(_poolScroll, GUILayout.Height(210f));
            foreach (var card in DeckStore.Pool)
            {
                string id = card.name;
                GUILayout.BeginHorizontal();
                string cost = card.BloodCost > 0 ? $"{card.BloodCost} blood"
                            : card.BonesCost > 0 ? $"{card.BonesCost} bones"
                            : "free";
                GUILayout.Label($"{card.DisplayedNameEnglish}", _label, GUILayout.Width(160f));
                GUILayout.Label(cost, _small, GUILayout.Width(70f));
                GUILayout.FlexibleSpace();
                int have = DeckStore.CountOf(id);
                if (have > 0) GUILayout.Label($"x{have}", _dim, GUILayout.Width(28f));
                GUI.enabled = deck.Count < DeckStore.MaxCards;
                if (GUILayout.Button("+", _button, GUILayout.Width(34f))) { DeckStore.Add(id); break; }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            Rule();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Deck", _button)) DeckStore.Save();
            if (GUILayout.Button("Reset", _button)) DeckStore.ResetToStarter();
            GUILayout.EndHorizontal();
            GUILayout.Label("Each player brings their own deck.", _small);
        }
    }
}
