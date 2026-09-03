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
        /// <summary>
        /// Grown to fit whatever optional lines are showing. A fixed height pushed the
        /// buttons off the bottom as soon as a couple of warnings appeared at once.
        /// </summary>
        private float PanelH
        {
            get
            {
                float h = 520f;
                if (ActInfo.Selected != MatchAct.Act1) h += 22f;   // experimental notice
                if (!DeckStore.IsValid) h += 22f;                  // deck size warning
                if (Net.HandshakeError != null) h += 66f;          // version mismatch block
                if (VersusMode.Suspended) h += 66f;                // reconnect countdown
                if (NativeDeckBuilder.LastError != null) h += 22f;
                if (VersusMode.LastResult != null && !VersusMode.InMatch) h += 22f;
                if (SteamTransport.Lobbies.Count > 0) h += 90f;    // lobby list
                return h;
            }
        }

        private bool _open;
        private bool _matchWasActive;
        private Rect _rect;
        private Vector2 _lobbyScroll;

        private GUIStyle _chip, _label, _dim, _header, _section, _field, _button, _small;
        private Texture2D _panelBg, _chipBg, _accent, _rule;

        private void Update()
        {
            SteamTransport.Poll();

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (NativeDeckBuilder.IsOpen) NativeDeckBuilder.Close();
                else _open = !_open;
            }
            if (Input.GetKeyDown(KeyCode.F8)) VersusMode.StartAnywhere(this);
            if (Input.GetKeyDown(KeyCode.F12)) VersusMode.Abort(this);

            VersusMode.TickPendingStart(this);
            NativeDeckBuilder.TickPendingOpen(this);

            // Get out of the way when a match STARTS, however it was triggered - button,
            // hotkey, or the peer asking us to join. Edge-triggered on purpose: closing it
            // every frame while in a match made F7 unable to reopen the menu at all, which
            // also hid the abort button and the disconnect countdown.
            bool matchActive = VersusMode.InMatch || VersusMode.PendingStart;
            if (matchActive && !_matchWasActive) _open = false;
            _matchWasActive = matchActive;

            // Start requests arrive out-of-band. Draining the inbox here instead would
            // silently discard any other message that happened to be queued.
            if (Net.PendingStartRequest.HasValue)
            {
                MatchAct act = Net.PendingStartRequest.Value;
                Net.PendingStartRequest = null;
                if (!VersusMode.InMatch && !VersusMode.PendingStart)
                {
                    // The host picks the act; we follow, so both load the same scene.
                    ActInfo.Current = act;
                    Trace.Info($"[versus] peer started a {ActInfo.Name(act)} match - joining");
                    VersusMode.StartAnywhere(this, tellPeer: false);
                }
            }

            // Handled here rather than in the opponent's wait loop so a result lands
            // whatever phase we're in - including our own turn.
            if (Net.PendingResult.HasValue)
            {
                bool weWon = Net.PendingResult.Value;
                Net.PendingResult = null;
                if (VersusMode.InMatch)
                    VersusMode.Finish(this, weWon, "peer reported the result");
            }

            // A drop suspends the match rather than forfeiting it; the transports keep
            // trying to re-establish underneath.
            if (VersusMode.InMatch && !Net.Connected) VersusMode.NoteDisconnected();
            if (VersusMode.InMatch && Net.Connected && VersusMode.Suspended) VersusMode.NoteReconnected();
            VersusMode.TickSuspension(this);
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

            // The 3D card view needs the screen to itself.
            if (NativeDeckBuilder.IsOpen)
            {
                DrawCardViewHint();
                DrawCursor();
                return;
            }

            if (!_open)
            {
                DrawChip();
                return;
            }

            float h = PanelH;
            _rect = new Rect((Screen.width - PanelW) * 0.5f,
                             (Screen.height - h) * 0.5f,
                             PanelW, h);
            // Deliberately NOT GUI.Window: IMGUI composites windows on top of everything
            // else drawn in OnGUI regardless of call order, which painted over the cursor
            // every frame. We draw our own background anyway, so the window bought nothing.
            GUI.BeginGroup(_rect);
            DrawWindow(0);
            GUI.EndGroup();

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
        private void DrawCursor() => DrawCursor(default(Rect));

        /// <summary>
        /// The game draws its cursor into the 3D scene, so anything we render covers it.
        /// Draw our own unconditionally while our UI is up - gating it on "over the panel"
        /// meant it vanished over the cards, which is exactly where it's needed.
        /// </summary>
        private void DrawCursor(Rect _unused)
        {
            Vector3 m = Input.mousePosition;
            float x = m.x;
            float y = Screen.height - m.y;   // GUI space is y-down

            const float len = 11f;
            const float thick = 3f;

            var dark = new Color(0f, 0f, 0f, 0.9f);
            var prev = GUI.color;

            // Dark outline first so it reads against cards, table and panel alike.
            GUI.color = dark;
            GUI.DrawTexture(new Rect(x - len - 1f, y - 1f, len * 2f + 2f, thick + 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1f, y - len - 1f, thick + 2f, len * 2f + 2f), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 0.85f, 0.35f, 1f);
            GUI.DrawTexture(new Rect(x - len, y, len * 2f, thick), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y - len, thick, len * 2f), Texture2D.whiteTexture);

            GUI.color = prev;
        }

        /// <summary>
        /// Control bar for the 3D card view. The cards themselves are the game's, but
        /// paging and switching between pool and deck need controls the card array
        /// doesn't provide.
        /// </summary>
        private void DrawCardViewHint()
        {
            bool pool = NativeDeckBuilder.PoolMode;
            string mode = pool ? "CLICK A CARD TO ADD IT" : "CLICK A CARD TO REMOVE IT";
            string counts = $"deck {DeckStore.Deck.Count}/{DeckStore.MaxCards}" +
                            $"     page {NativeDeckBuilder.Page + 1}/{NativeDeckBuilder.PageCount}";

            const float w = 700f, h = 84f;
            var bar = new Rect((Screen.width - w) * 0.5f, 12f, w, h);

            GUI.DrawTexture(bar, _accent);
            GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, bar.width - 4f, bar.height - 4f), _panelBg);

            GUILayout.BeginArea(new Rect(bar.x + 12f, bar.y + 8f, bar.width - 24f, bar.height - 16f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(mode, _section);
            GUILayout.FlexibleSpace();
            GUILayout.Label(counts, _small);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Prev", _button, GUILayout.Width(90f))) NativeDeckBuilder.PrevPage();
            if (GUILayout.Button("Next >", _button, GUILayout.Width(90f))) NativeDeckBuilder.NextPage();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(pool ? "View My Deck" : "Browse All Cards", _button, GUILayout.Width(190f)))
                NativeDeckBuilder.ToggleMode();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Done  [F7]", _button, GUILayout.Width(120f))) NativeDeckBuilder.Close();
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            if (!pool && DeckStore.Deck.Count == 0)
            {
                var hint = new Rect((Screen.width - 400f) * 0.5f, bar.yMax + 16f, 400f, 30f);
                GUI.DrawTexture(hint, _panelBg);
                GUI.Label(new Rect(hint.x + 10f, hint.y + 5f, hint.width, hint.height),
                          "Your deck is empty - browse all cards to add some.", _chip);
            }

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

            GUILayout.BeginHorizontal();
            GUILayout.Label("INSCRYPTION ONLINE", _header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("v" + Plugin.Version, _small);
            GUILayout.EndHorizontal();
            GUILayout.Label(Net.StatusLine + "   -   " + ActInfo.Name(ActInfo.Selected), Net.Connected ? _label : _dim);
            if (Net.HandshakeError != null)
            {
                GUILayout.Label("INCOMPATIBLE VERSIONS", _section);
                GUILayout.Label(Net.HandshakeError, _small);
                GUILayout.Label("Both players need the same build of the mod.", _small);
            }
            GUILayout.Space(6f);

            // Act picker. The act decides which table the match is played on, so it has to
            // be chosen before starting and both clients follow the host's choice.
            GUILayout.Label("ACT", _section);
            GUI.enabled = !VersusMode.InMatch;
            GUILayout.BeginHorizontal();
            foreach (MatchAct act in new[] { MatchAct.Act1, MatchAct.Act2, MatchAct.Act3 })
            {
                bool supported = ActInfo.IsSupported(act);
                bool selected = ActInfo.Selected == act;
                GUI.enabled = !VersusMode.InMatch && supported;

                string label = (selected ? "> " : "") + ActInfo.Name(act) + (supported ? "" : " (n/a)");
                if (GUILayout.Button(label, _button)) ActInfo.Selected = act;
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            if (ActInfo.Selected != MatchAct.Act1)
                GUILayout.Label("Experimental - this act is still being brought up.", _small);

            Rule();

            // Deck editing goes straight to the game's card table - an IMGUI list of card
            // names was a poor second view of something the game already renders properly.
            GUI.enabled = !VersusMode.InMatch;
            if (GUILayout.Button($"EDIT {ActInfo.Name(ActInfo.Selected).ToUpper()} DECK  ({DeckStore.Deck.Count}/{DeckStore.MaxCards})", _button))
                OpenCardView();
            GUI.enabled = true;

            if (VersusMode.InMatch)
                GUILayout.Label("Finish the match to edit your deck.", _small);
            else if (!DeckStore.IsValid)
                GUILayout.Label($"Your deck needs {DeckStore.MinCards}-{DeckStore.MaxCards} cards.", _small);
            if (NativeDeckBuilder.LastError != null)
                GUILayout.Label(NativeDeckBuilder.LastError, _small);

            Rule();

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
            GUI.enabled = Net.Connected && !VersusMode.InMatch && !VersusMode.PendingStart
                          && DeckStore.IsValid && Net.HandshakeError == null;
            if (GUILayout.Button("START MATCH", _button)) VersusMode.StartAnywhere(this);
            GUI.enabled = true;


            if (VersusMode.Suspended)
            {
                GUILayout.Space(6f);
                GUILayout.Label("OPPONENT DISCONNECTED", _section);
                GUILayout.Label($"Holding the match for {Mathf.CeilToInt(VersusMode.SuspendedSecondsLeft)}s.", _small);
                GUILayout.Label("They can relaunch and rejoin - nothing is lost.", _small);
            }

            if (!Net.Connected && !VersusMode.InMatch)
                GUILayout.Label("Host a lobby, or find one, to play someone.", _small);
            else if (Net.Connected && !VersusMode.InMatch && DeckStore.IsValid
                     && Net.HandshakeError == null)
                GUILayout.Label("Either player can start - you both enter together.", _small);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (VersusMode.InMatch &&
                GUILayout.Button(VersusMode.Suspended ? "Give Up Waiting" : "Abort Match", _button))
                VersusMode.Abort(this);
            if (Net.Running && GUILayout.Button("Disconnect", _button)) Net.Shutdown();
            GUILayout.EndHorizontal();

            if (VersusMode.LastResult != null && !VersusMode.InMatch)
                GUILayout.Label($"Last match: {VersusMode.LastResult}", _label);

            GUILayout.FlexibleSpace();
            GUILayout.Label("F7 menu     F8 start match     F12 abort", _small);
            GUILayout.EndArea();
        }

        /// <summary>Loads the table if needed, then opens the game's card view.</summary>
        private void OpenCardView()
        {
            _open = false;
            if (NativeDeckBuilder.Available)
            {
                NativeDeckBuilder.Open(this, poolMode: true);
                return;
            }

            NativeDeckBuilder.PendingOpen = true;
            VersusMode.LoadTableOnly();
        }
    }
}
