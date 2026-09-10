using System.Collections.Generic;
using DiskCardGame;
using Steamworks;
using UnityEngine;

namespace InscryptionMP
{
    /// <summary>
    /// The mod's front end: a centred panel for finding an opponent and starting a match, plus
    /// a compact status chip when it's closed.
    /// </summary>
    internal class MpMenu : MonoBehaviour
    {
        internal static string Ip = "127.0.0.1";
        internal static string PortText = Net.DefaultPort.ToString();

        private const float PanelW = 510f;

        /// <summary>Height of the panel as IMGUI actually laid it out last frame.</summary>
        private float _measuredH = 300f;

        /// <summary>Height of the card-view control bar, measured the same way.</summary>
        private float _barH = 110f;

        private bool _open;
        private bool _matchWasActive;

        /// <summary>Which of the panel's screens is in front.</summary>
        /// <summary>
        /// Which page the panel is on. One page does one thing: the front of the menu is a
        /// list of the things you might have opened it for, and each of them is its own
        /// screen rather than another block stacked onto the same one.
        /// </summary>
        private enum Page { Home, Online, Local, Deck, Rules }

        private Page _page = Page.Home;

        /// <summary>What is half-typed into the chat box.</summary>
        private string _draft = "";

        private Vector2 _chatScroll;

        /// <summary>The dial whose note is being shown, so the rules screen can explain itself.</summary>
        private string _explaining;


        private Rect _rect;
        private Vector2 _lobbyScroll;

        private GUIStyle _chip, _label, _dim, _header, _section, _field, _button, _small;
        private GUIStyle _primary, _quiet, _warn, _resultText, _good, _busy, _close, _pageLabel;
        private GUIStyle _countLabel, _version;
        private GUIStyle _seat, _seatState, _seatReady;
        private GUIStyle _chatLine, _chatMine, _chatSystem, _chatHead;
        private GUIStyle _scoreName, _scoreNum, _scoreTurn;
        private GUIStyle _arrow, _dialValue;
        private GUIStyle _vote, _voteBoth;
        private GUIStyle _chipState, _chipInfo;
        private Texture2D _panelBg, _chipBg, _accent;

        private void Update()
        {
            Background.Tick();
            Chat.Pump();
            SteamTransport.Poll();
            EmoteKeys();

            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (NativeDeckBuilder.IsOpen) NativeDeckBuilder.Close();
                else _open = !_open;
            }
            // F8 does whatever the start button does. Forcing a match past a negotiation
            // the other player is still in the middle of is not a shortcut, it's a desync.
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (Lobby.Negotiating) Lobby.ToggleReady();
                else VersusMode.StartAnywhere(this);
            }
            if (Input.GetKeyDown(KeyCode.F12)) VersusMode.Abort(this);

            VersusMode.TickPendingStart(this);
            NativeDeckBuilder.TickPendingOpen(this);
            Lobby.Tick(this);

            // Keep the lobby list alive while it is the thing being looked at. Driven from
            // here rather than OnGUI, which runs several times a frame.
            if (_open && !NativeDeckBuilder.IsOpen && !VersusMode.InMatch
                && !Net.Connected && !Net.Running)
                SteamTransport.AutoRefresh();

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
                    // Selected too, not just Current: the deck store is keyed on Selected,
                    // so leaving it behind would bring our Act 1 deck to their Act 3 table.
                    ActInfo.Current = act;
                    ActInfo.Selected = act;
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

            // Whatever route was taken, a match without a battle behind it is over. The
            // patch above catches the pause menu; this catches anything else that leaves
            // the scene, rather than leaving the score bar on screen forever.
            if (VersusMode.InMatch && !VersusMode.PendingStart
                && Singleton<TurnManager>.Instance == null)
                VersusMode.NoteLeftByGame("the battle is no longer loaded");

            // A drop suspends the match rather than forfeiting it; the transports keep
            // trying to re-establish underneath.
            if (VersusMode.InMatch && !Net.Connected) VersusMode.NoteDisconnected();
            if (VersusMode.InMatch && Net.Connected && VersusMode.Suspended) VersusMode.NoteReconnected();
            VersusMode.TickSuspension(this);
        }

        /// <summary>
        /// The number keys say one of the fixed phrases while a match is running. Only with
        /// the panel and the card table shut, so they never fire out from under someone
        /// typing.
        /// </summary>
        private void EmoteKeys()
        {
            if (NativeDeckBuilder.IsOpen || ChatHasFocus) return;
            if (!Net.Connected || !VersusMode.InMatch) return;

            for (int i = 0; i < Chat.Emotes.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Chat.Emote(i);
                    return;
                }
        }

        private static int ParsedPort =>
            int.TryParse(PortText, out int p) && p > 0 && p < 65536 ? p : Net.DefaultPort;

        // ------------------------------------------------------------------ palette
        //
        // Inscryption is bone and gold on dark wood. Everything here is drawn from that,
        // so state reads through warmth and brightness rather than through a separate
        // colour language of greens and reds bolted on top.

        private static readonly Color Ink       = new Color(0.055f, 0.048f, 0.042f, 0.97f);  // panel ground
        private static readonly Color InkSoft   = new Color(0.105f, 0.092f, 0.078f, 1f);     // raised rows
        private static readonly Color Bone      = new Color(0.93f, 0.90f, 0.83f, 1f);        // primary text
        private static readonly Color BoneDim   = new Color(0.60f, 0.57f, 0.51f, 1f);        // secondary text
        private static readonly Color BoneFaint = new Color(0.40f, 0.38f, 0.35f, 1f);        // hints
        private static readonly Color Gold      = new Color(0.84f, 0.64f, 0.28f, 1f);        // accent, active
        private static readonly Color GoldSoft  = new Color(0.84f, 0.64f, 0.28f, 0.28f);     // borders
        private static readonly Color Rust      = new Color(0.72f, 0.34f, 0.26f, 1f);        // trouble
        private static readonly Color Moss      = new Color(0.45f, 0.74f, 0.36f, 1f);        // ahead

        /// <summary>A one-pixel outline. Cheaper to read than a thick slab of colour.</summary>
        private void Frame(Rect r, Color c, float t = 1f)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private static Font _gameFont;
        private static bool _fontSearched;

        /// <summary>The game's own typeface, if we can reach one.</summary>
        private static Font GameFont()
        {
            if (_fontSearched) return _gameFont;
            _fontSearched = true;

            try
            {
                var loaded = Resources.FindObjectsOfTypeAll<Font>();
                var names = new System.Text.StringBuilder();
                Font plain = null;

                foreach (Font f in loaded)
                {
                    if (f == null) continue;
                    names.Append(f.name).Append(" | ");

                    string n = f.name.ToLowerInvariant();
                    if (n.Contains("arial")) continue;

                    // Marksman is the game's face. The "Crisp" cut is the one it uses where
                    // text has to stay readable small, which is all of our UI; "Pocket" is
                    // the GBC pixel font and would look wrong outside Act 2.
                    if (n.Contains("marksman") && n.Contains("crisp")) { _gameFont = f; break; }
                    if (n.Contains("marksman") && plain == null) plain = f;
                }

                if (_gameFont == null) _gameFont = plain;
                Trace.Info($"[ui] fonts available: {names}");
                Trace.Info($"[ui] using font: {(_gameFont == null ? "built-in" : _gameFont.name)}" +
                           (_gameFont == null ? "" : $" (dynamic={_gameFont.dynamic}, baked size={_gameFont.fontSize})"));
            }
            catch (System.Exception e)
            {
                Trace.Warn($"[ui] font lookup failed: {e.Message}");
            }
            return _gameFont;
        }

        /// <summary>
        /// Points every style at the game's face, and stops asking for sizes it can't give.
        /// </summary>
        private void ApplyFont()
        {
            Font f = GameFont();
            if (f == null) return;

            foreach (GUIStyle st in new[] { _chip, _label, _dim, _small, _header, _section,
                                            _field, _button, _primary, _quiet, _warn, _good,
                                            _busy, _close, _resultText, _chipState, _chipInfo,
                                            _pageLabel, _countLabel, _vote, _voteBoth, _version,
                                            _seat, _seatState, _seatReady, _chatLine, _chatMine,
                                            _chatSystem, _chatHead, _arrow, _dialValue,
                                            _scoreName, _scoreNum, _scoreTurn })
            {
                if (st == null) continue;
                st.font = f;
                if (!f.dynamic) st.fontSize = 0;
            }
        }

        private void EnsureStyles()
        {
            if (_chip != null) return;

            _panelBg = Solid(Ink);
            _chipBg  = Solid(new Color(Ink.r, Ink.g, Ink.b, 0.93f));
            _accent  = Solid(Gold);

            _label = new GUIStyle(GUI.skin.label) { fontSize = 17, normal = { textColor = Bone } };
            _dim   = new GUIStyle(_label) { fontSize = 16, normal = { textColor = BoneDim } };
            _small = new GUIStyle(_label) { fontSize = 14, normal = { textColor = BoneDim } };

            _chip = new GUIStyle(_label);

            _header = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Bone },
            };

            // Section labels earn their separation from space above them, not from a rule.
            _section = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 0, 4),
                normal = { textColor = Gold },
            };

            _field = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 17,
                padding = new RectOffset(10, 10, 8, 8),
                normal  = { textColor = Bone, background = Solid(InkSoft) },
                focused = { textColor = Color.white, background = Solid(new Color(0.16f, 0.14f, 0.12f, 1f)) },
            };

            // Secondary: an outline, not a filled slab. Filled grey buttons everywhere is
            // what made this read as a debug overlay.
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                padding = new RectOffset(14, 14, 10, 10),
                normal = { textColor = Bone,        background = Solid(InkSoft) },
                hover  = { textColor = Color.white, background = Solid(new Color(0.17f, 0.15f, 0.12f, 1f)) },
                active = { textColor = Gold,        background = Solid(new Color(0.22f, 0.19f, 0.15f, 1f)) },
            };

            _primary = new GUIStyle(_button)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.10f, 0.08f, 0.05f), background = Solid(Gold) },
                hover  = { textColor = new Color(0.06f, 0.05f, 0.03f), background = Solid(new Color(0.95f, 0.74f, 0.36f, 1f)) },
                active = { textColor = Color.black,                    background = Solid(new Color(1f, 0.83f, 0.48f, 1f)) },
            };

            _quiet = new GUIStyle(_button)
            {
                fontSize = 14,
                padding = new RectOffset(10, 10, 7, 7),
                normal = { textColor = BoneDim, background = Solid(new Color(0.085f, 0.075f, 0.065f, 1f)) },
                hover  = { textColor = Bone,    background = Solid(new Color(0.14f, 0.125f, 0.105f, 1f)) },
            };

            _close = new GUIStyle(_quiet)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Rust,        background = Solid(new Color(0.13f, 0.07f, 0.06f, 1f)) },
                hover  = { textColor = Color.white, background = Solid(new Color(0.45f, 0.16f, 0.12f, 1f)) },
            };

            _pageLabel = new GUIStyle(_label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Bone },
            };

            _countLabel = new GUIStyle(_pageLabel) { alignment = TextAnchor.MiddleRight };

            // A seat in the lobby: the name on the left, its state on the right, both on
            // one baseline so two players read as a pair rather than two rows.
            _seat = new GUIStyle(_label) { fontSize = 17, wordWrap = false };
            _seatState = new GUIStyle(_label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                wordWrap = false,
                normal = { textColor = BoneFaint },
            };
            _seatReady = new GUIStyle(_seatState) { normal = { textColor = Gold } };

            _scoreName = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                wordWrap = false,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Bone },
            };
            _scoreNum = new GUIStyle(_scoreName)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
            };
            _scoreTurn = new GUIStyle(_scoreName)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _chatHead = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                padding = new RectOffset(2, 2, 0, 0),
                normal = { textColor = Gold },
                hover = { textColor = Bone },
            };

            _chatLine = new GUIStyle(_small) { fontSize = 14, wordWrap = true, normal = { textColor = Bone } };
            _chatMine = new GUIStyle(_chatLine) { normal = { textColor = BoneDim } };
            _chatSystem = new GUIStyle(_chatLine) { fontStyle = FontStyle.Italic, normal = { textColor = BoneFaint } };

            _arrow = new GUIStyle(_button)
            {
                fontSize = 15,
                padding = new RectOffset(0, 0, 8, 8),
                alignment = TextAnchor.MiddleCenter,
            };
            _dialValue = new GUIStyle(_label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                normal = { textColor = Bone },
            };

            // A width of its own and no wrapping. Sharing the title row with a flexible
            // space, the version was laid out one glyph narrower than it draws - every
            // release since 1.1 has quietly called itself "v1.4." on its own panel.
            _version = new GUIStyle(_small) { alignment = TextAnchor.MiddleRight, wordWrap = false };

            // The tally under each act button. Small enough to read as an annotation on the
            // button rather than as another row of controls.
            _vote = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                padding = new RectOffset(0, 0, 2, 0),
                normal = { textColor = BoneFaint },
            };
            _voteBoth = new GUIStyle(_vote) { normal = { textColor = Gold } };


            _warn = new GUIStyle(_small) { normal = { textColor = Rust } };
            _good = new GUIStyle(_small) { normal = { textColor = Gold } };
            _busy = new GUIStyle(_small) { normal = { textColor = BoneDim } };
            _resultText = new GUIStyle(_label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = Gold } };

            // No padding on the chip rows: the chip positions each one itself, and padding
            // baked into a style is what made CalcSize disagree with the drawn rect.
            _chipState = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = Bone },
            };
            _chipInfo = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = BoneDim },
            };

            // After every style exists, not partway through building them: half of these
            // were still null here and the throw left the whole panel unstyled and blank.
            //
            // GUI.skin.label wraps by default, so a box even slightly too narrow keeps the
            // first word and drops the rest - "deck 8/20" rendered as "deck".
            // Anything that is one line by nature. GUI.skin.label wraps by default, and a
            // box even slightly too narrow keeps the first word and drops the rest - which
            // is how the panel came to call itself "INSCRYPTION".
            foreach (GUIStyle st in new[] { _countLabel, _pageLabel, _section, _chipState,
                                            _chipInfo, _vote, _voteBoth, _header, _label,
                                            _dim, _seat, _seatState, _seatReady, _dialValue })
                if (st != null) st.wordWrap = false;

            ApplyFont();
        }

        private void OnGUI()
        {
            EnsureStyles();

            // The 3D card view needs the screen to itself.
            if (NativeDeckBuilder.IsOpen)
            {
                // Scaled like the rest of the UI. Returning early skipped the matrix, so
                // this bar stayed at 1x - and it's the one being read continuously while
                // browsing cards.
                _viewScale = FitWidth(CardBarW + 48f);

                Matrix4x4 barRestore = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(_viewScale, _viewScale, 1f));
                DrawCardViewHint();
                GUI.matrix = barRestore;

                DrawChatWindow();
                DrawCursor();
                return;
            }

            if (!_open)
            {
                Matrix4x4 chipRestore = GUI.matrix;

                if (VersusMode.InMatch)
                {
                    _barScale = FitWidth(620f);
                    GUI.matrix = Matrix4x4.Scale(new Vector3(_barScale, _barScale, 1f));
                    DrawScoreBar();
                }
                else
                {
                    float chipScale = FitWidth(460f);
                    GUI.matrix = Matrix4x4.Scale(new Vector3(chipScale, chipScale, 1f));
                    DrawChip();
                }

                GUI.matrix = chipRestore;

                DrawChatWindow();
                DrawCursor();
                return;
            }

            // Positions are worked out in unscaled space, then the matrix magnifies it.
            _panelScale = FitScale(PanelW + Pad * 2f, _measuredH + 28f);
            float vw = Screen.width / _panelScale;
            float vh = Screen.height / _panelScale;
            _rect = new Rect((vw - PanelW) * 0.5f,
                             (vh - _measuredH) * 0.5f,
                             PanelW, _measuredH);
            // Deliberately NOT GUI.Window: IMGUI composites windows on top of everything
            // else drawn in OnGUI regardless of call order, which painted over the cursor
            // every frame. We draw our own background anyway, so the window bought nothing.
            Matrix4x4 restore = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_panelScale, _panelScale, 1f));

            GUI.BeginGroup(_rect);
            DrawWindow(0);
            GUI.EndGroup();

            // The cursor is drawn from raw screen coordinates, so it has to be outside
            // the scaled matrix or it lands in the wrong place.
            GUI.matrix = restore;
            DrawChatWindow();
            DrawCursor();
        }

        /// <summary>Colour for the current connection/turn state - the chip's whole job.</summary>
        /// <summary>
        /// State reads through warmth: gold when it's on you to act, bone when it isn't, rust
        /// when something is wrong.
        /// </summary>
        private Color StateColour()
        {
            if (VersusMode.Suspended) return Rust;
            if (VersusMode.InMatch) return TurnOrder.IsMyTurn ? Gold : BoneDim;
            if (Net.Connected) return Bone;
            if (Net.Running) return BoneDim;
            return BoneFaint;
        }

        /// <summary>
        /// The state, in as few words as carry it. Nothing about which key to press - that
        /// belongs on the line below, and having it in both places meant the chip said "F7"
        /// twice.
        /// </summary>
        private static string StateText()
        {
            if (VersusMode.Suspended) return "OPPONENT LEFT";
            if (VersusMode.InMatch) return TurnOrder.IsMyTurn ? "YOUR TURN" : "THEIR TURN";
            if (Net.Connected)
                return Lobby.Negotiating ? "IN LOBBY  " + Lobby.ReadyCount + "/2" : "READY";
            if (Net.Running) return Net.IsHost ? "HOSTING" : "CONNECTING";
            return "OFFLINE";
        }

        /// <summary>
        /// The line under the state: what is happening, or the one thing worth doing next.
        /// </summary>
        private static string DetailText()
        {
            if (VersusMode.Suspended)
                return "Rejoining within " + Mathf.CeilToInt(VersusMode.SuspendedSecondsLeft) + "s keeps the match";

            if (VersusMode.InMatch) return ScalesText();

            if (Net.Connected)
            {
                if (!Lobby.Negotiating) return "Press F7 to start a match";
                if (Lobby.BothReady) return "Starting...";
                if (Lobby.MyReady) return "Waiting for your opponent to press start";
                return Lobby.Blocker ?? "Press F7 to start the " + ActInfo.Name(Lobby.MyAct) + " match";
            }

            // Nothing to add that the state doesn't already say. The chip skips a null row,
            // which reads better than repeating "connecting" underneath "CONNECTING".
            if (Net.Running) return Net.IsHost ? "Waiting for an opponent to join" : null;
            return "Press F7 to find a game";
        }

        /// <summary>
        /// How the scales are leaning, from the player's side. Null outside a battle.
        /// </summary>
        private static string ScalesText()
        {
            var life = Singleton<LifeManager>.Instance;
            if (life == null) return null;

            int balance = life.Balance;
            string lead = balance == 0 ? "Level"
                        : balance > 0 ? "You lead by " + balance
                        : "They lead by " + (-balance);

            int toWin = life.DamageUntilPlayerWin;
            return lead + " on the scales - " + toWin + " to win";
        }

        /// <summary>
        /// The score, across the top: each player's name and how far ahead they are, green
        /// or red, with whose turn it is between them. A chip in the corner could only ever
        /// show one side of a thing that has two.
        /// </summary>
        private void DrawScoreBar()
        {
            var life = Singleton<LifeManager>.Instance;
            int lead = life == null ? 0 : life.Balance;

            string turn = VersusMode.Suspended ? "OPPONENT LEFT"
                        : TurnOrder.IsMyTurn ? "YOUR TURN" : "THEIR TURN";

            const float w = 560f, padX = 18f, padY = 12f, rowH = 28f, gap = 10f;

            float vw = Screen.width / _barScale;
            var box = new Rect((vw - w) * 0.5f, 14f, w, rowH + padY * 2f);

            GUI.DrawTexture(box, _chipBg);
            Frame(box, GoldSoft);

            float col = (w - padX * 2f) / 3f;
            float top = box.y + padY;

            Side(new Rect(box.x + padX, top, col - gap, rowH), Player.LocalName, lead, false);
            Side(new Rect(box.xMax - padX - col + gap, top, col - gap, rowH), Player.PeerName, -lead, true);

            var turnStyle = new GUIStyle(_scoreTurn)
            {
                normal = { textColor = VersusMode.Suspended ? Rust
                                     : TurnOrder.IsMyTurn ? Gold : BoneDim },
            };
            GUI.Label(new Rect(box.x + padX + col, top, col, rowH), turn, turnStyle);
        }

        /// <summary>One player's half: their name, and how far ahead they are.</summary>
        private void Side(Rect r, string name, int lead, bool rightAligned)
        {
            Color tint = lead > 0 ? Moss : lead < 0 ? Rust : BoneDim;
            string num = lead > 0 ? "+" + lead : lead.ToString();

            var numStyle = new GUIStyle(_scoreNum)
            {
                alignment = rightAligned ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight,
                normal = { textColor = tint },
            };
            var nameStyle = new GUIStyle(_scoreName)
            {
                alignment = rightAligned ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
            };

            const float numW = 46f;
            if (rightAligned)
            {
                GUI.Label(new Rect(r.x, r.y, numW, r.height), num, numStyle);
                GUI.Label(new Rect(r.x + numW + 10f, r.y, r.width - numW - 10f, r.height), name, nameStyle);
            }
            else
            {
                GUI.Label(new Rect(r.x, r.y, r.width - numW - 10f, r.height), name, nameStyle);
                GUI.Label(new Rect(r.xMax - numW, r.y, numW, r.height), num, numStyle);
            }
        }

        /// <summary>What the score bar was drawn at last pass.</summary>
        private float _barScale = MaxScale;

        /// <summary>The always-on status marker.</summary>
        private void DrawChip()
        {
            string state = StateText();
            string detail = DetailText();

            // The notice and the detail line are two ways of saying what is going on, and
            // they often land on the same words. Only one of them is worth the row.
            bool notice = Notice.Visible && !Echoes(Notice.Text, detail) && !Echoes(Notice.Text, state);

            var stateStyle = new GUIStyle(_chipState) { normal = { textColor = StateColour() } };

            Vector2 stateSize = stateStyle.CalcSize(new GUIContent(state));
            Vector2 detailSize = string.IsNullOrEmpty(detail) ? Vector2.zero : _chipInfo.CalcSize(new GUIContent(detail));
            Vector2 noticeSize = notice ? NoticeStyle().CalcSize(new GUIContent(Notice.Text)) : Vector2.zero;

            const float padX = 16f, padY = 12f, rowGap = 6f;

            float innerW = Mathf.Max(stateSize.x, Mathf.Max(detailSize.x, noticeSize.x));
            float w = Mathf.Max(innerW + padX * 2f, 200f);

            float h = padY * 2f + stateSize.y;
            if (detailSize.y > 0f) h += rowGap + detailSize.y;
            if (notice) h += rowGap + noticeSize.y;

            var r = new Rect(14f, 14f, w, h);
            GUI.DrawTexture(r, _chipBg);
            Frame(r, GoldSoft);

            float x = r.x + padX;
            float y = r.y + padY;
            float rowW = r.width - padX * 2f;

            GUI.Label(new Rect(x, y, rowW, stateSize.y), state, stateStyle);
            y += stateSize.y + rowGap;

            if (detailSize.y > 0f)
            {
                GUI.Label(new Rect(x, y, rowW, detailSize.y), detail, _chipInfo);
                y += detailSize.y + rowGap;
            }

            if (notice)
                GUI.Label(new Rect(x, y, rowW, noticeSize.y), Notice.Text, NoticeStyle());
        }

        private GUIStyle NoticeStyle() => StyleFor(Notice.Kind);

        private GUIStyle StyleFor(NoticeKind kind)
        {
            switch (kind)
            {
                case NoticeKind.Bad:  return _warn;
                case NoticeKind.Good: return _good;
                case NoticeKind.Busy: return _busy;
                default:              return _small;
            }
        }

        /// <summary>
        /// Every line of prose the panel has drawn this pass. The status line, the act
        /// agreement, the last result and the notice are all written by different parts of
        /// the mod, and they regularly arrive at the same sentence - which is what made the
        /// panel say things twice.
        /// </summary>
        private readonly List<string> _said = new List<string>();

        /// <summary>
        /// Draws a line unless the panel has already said it in some other wording, and
        /// remembers it either way.
        /// </summary>
        private void Say(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text) || AlreadySaid(text)) return;
            _said.Add(text);
            GUILayout.Label(text, style);
        }

        /// <summary>Letters and digits only, so wording and punctuation stop mattering.</summary>
        private static string Bare(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            foreach (char c in text)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>
        /// Whether two lines say the same thing. One containing the other counts - "you won"
        /// against "last match: you won" - with a length floor so a short phrase can't
        /// swallow an unrelated longer one.
        /// </summary>
        private static bool Echoes(string a, string b)
        {
            string x = Bare(a), y = Bare(b);
            if (x.Length == 0 || y.Length == 0) return false;
            if (x == y) return true;
            if (y.Length >= 8 && x.Contains(y)) return true;
            return x.Length >= 8 && y.Contains(x);
        }

        private bool AlreadySaid(string text)
        {
            foreach (string line in _said)
                if (Echoes(text, line)) return true;
            return false;
        }

        /// <summary>The notice, drawn inside the panel so it's visible with the menu open.</summary>
        private void DrawNotice()
        {
            if (!Notice.Visible) return;
            if (AlreadySaid(Notice.Text)) return;
            GUILayout.Space(6f);
            GUILayout.Label(Notice.Text, NoticeStyle());
        }

        /// <summary>
        /// The game draws its own cursor into the 3D scene, which our panel then covers -
        /// so you cannot see what you are about to click. Draw a marker on top instead.
        /// </summary>
        private void DrawCursor() => DrawCursor(default(Rect));

        /// <summary>
        /// The game draws its cursor into the 3D scene, so anything we render covers it.
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
        /// Control bar for the 3D card view. The cards themselves are the game's, but paging
        /// and switching between pool and deck need controls the card array doesn't provide.
        /// </summary>
        private void DrawCardViewHint()
        {
            bool pool = NativeDeckBuilder.PoolMode;
            bool sigils = NativeDeckBuilder.SigilMode;

            bool selected = NativeDeckBuilder.HasSelection;

            string mode = sigils
                ? "SIGILS FOR " + NativeDeckBuilder.SigilCardName.ToUpperInvariant()
                : pool ? "CLICK A CARD TO ADD IT"
                : selected ? NativeDeckBuilder.SelectedCardName.ToUpperInvariant() + " SELECTED"
                           : "CLICK A CARD TO CHOOSE IT";

            string counts = sigils
                ? NativeDeckBuilder.SigilBudget
                : $"deck {DeckStore.Deck.Count}/{DeckStore.MaxCards}";

            const float w = CardBarW;
            const float barPad = 14f;

            var bar = new Rect((Screen.width / _viewScale - w) * 0.5f, 12f, w, _barH);

            GUI.DrawTexture(bar, _panelBg);
            Frame(bar, GoldSoft);

            // Generous area to lay out in; the drawn height comes from what was measured
            // last frame. A fixed 84px was too short for the larger font and clipped the
            // whole button row off the bottom, which read as the buttons not existing.
            GUILayout.BeginArea(new Rect(bar.x + barPad, bar.y + barPad, bar.width - barPad * 2f, 400f));
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();

            // ExpandWidth(false), or the label takes the whole row by default and pushes
            // the page counter off the end of the bar - which is why shortening the text
            // never brought it back.
            GUILayout.Label(mode, _section, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();

            // A width of its own, like the page counter: left to measure itself this read
            // as "deck" with the numbers clipped off the end.
            GUILayout.Label(counts, _countLabel, GUILayout.Width(240f));
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Prev", _button, GUILayout.Width(80f))) NativeDeckBuilder.PrevPage();

            // Between the arrows with a width of its own. Sharing the row above with a label
            // meant it kept being pushed off the end of the bar.
            GUILayout.Space(12f);
            // A fixed height matching the buttons, so MiddleCenter has something to centre
            // within. Not ExpandHeight: the area around this is deliberately over-tall so
            // the bar can measure itself, and the label expanded to fill all of it.
            GUILayout.Label($"{NativeDeckBuilder.Page + 1} / {NativeDeckBuilder.PageCount}",
                            _pageLabel, GUILayout.Width(90f), GUILayout.Height(ButtonRowHeight));
            GUILayout.Space(12f);

            if (GUILayout.Button("Next >", _button, GUILayout.Width(80f))) NativeDeckBuilder.NextPage();
            GUILayout.FlexibleSpace();
            if (sigils)
            {
                if (GUILayout.Button("Back to Deck", _button, GUILayout.Width(160f)))
                    NativeDeckBuilder.ExitSigilMode();

                GUILayout.Space(8f);
                if (GUILayout.Button("Remove Card", _quiet, GUILayout.Width(160f)))
                    NativeDeckBuilder.RemoveTargetCard();
            }
            else if (!pool && selected)
            {
                // A card is chosen, so both things you might do to it are their own button.
                if (GUILayout.Button("Add Sigils", _primary, GUILayout.Width(140f)))
                    NativeDeckBuilder.EditSelectedSigils();

                GUILayout.Space(8f);
                if (GUILayout.Button("Delete Card", _quiet, GUILayout.Width(140f)))
                    NativeDeckBuilder.DeleteSelected();

                GUILayout.Space(8f);
                if (GUILayout.Button("Cancel", _quiet, GUILayout.Width(100f)))
                    NativeDeckBuilder.ClearSelection();
            }
            else if (!pool)
            {
                if (GUILayout.Button("Browse All Cards", _button, GUILayout.Width(190f)))
                    NativeDeckBuilder.ToggleMode();
            }
            else if (GUILayout.Button("View My Deck", _button, GUILayout.Width(190f)))
                NativeDeckBuilder.ToggleMode();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Done", _button, GUILayout.Width(90f))) NativeDeckBuilder.Close();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint)
                _barH = GUILayoutUtility.GetLastRect().yMax + barPad * 2f;

            GUILayout.EndArea();

            if (!pool && DeckStore.Deck.Count == 0)
            {
                var hint = new Rect((Screen.width - 400f) * 0.5f, bar.yMax + 16f, 400f, 30f);
                GUI.DrawTexture(hint, _panelBg);
                GUI.Label(new Rect(hint.x + 10f, hint.y + 5f, hint.width, hint.height),
                          "Your deck is empty - browse all cards to add some.", _chip);
            }

            if (!pool && !sigils && selected) DrawOrderBar();
        }

        /// <summary>Height of the ordering strip, measured the way the bar above it is.</summary>
        private float _orderH = 60f;

        /// <summary>
        /// Where the chosen card sits in the deck, and the two buttons that move it. Along
        /// the bottom rather than in the bar at the top: the bar already covers the back row
        /// of cards, and the whole point of this is to look at them while reordering.
        /// </summary>
        private void DrawOrderBar()
        {
            const float w = 780f;
            const float pad = 12f;

            float vw = Screen.width / _viewScale;
            float vh = Screen.height / _viewScale;
            var bar = new Rect((vw - w) * 0.5f, vh - _orderH - 18f, w, _orderH);

            GUI.DrawTexture(bar, _panelBg);
            Frame(bar, GoldSoft);

            GUILayout.BeginArea(new Rect(bar.x + pad, bar.y + pad, bar.width - pad * 2f, 300f));
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("DRAW POSITION   " + NativeDeckBuilder.SelectedPosition,
                            _section, GUILayout.Width(210f));

            GUI.enabled = NativeDeckBuilder.CanMoveSelected(-1);
            if (GUILayout.Button("Move Earlier", _quiet, GUILayout.Width(150f)))
                NativeDeckBuilder.MoveSelected(-1);

            GUILayout.Space(8f);
            GUI.enabled = NativeDeckBuilder.CanMoveSelected(1);
            if (GUILayout.Button("Move Later", _quiet, GUILayout.Width(150f)))
                NativeDeckBuilder.MoveSelected(1);
            GUI.enabled = true;

            GUILayout.Space(14f);
            GUILayout.Label(MatchRules.Effective.Draw == CardDraw.InOrder
                            ? "Dealt in this order"
                            : "Matters under In order",
                            _small, GUILayout.Width(200f));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint)
                _orderH = GUILayoutUtility.GetLastRect().yMax + pad * 2f;

            GUILayout.EndArea();
        }

        /// <summary>
        /// The panel shows one of three screens, because at any moment only one of them is what
        /// you are trying to do: find an opponent, set up a match, or play one.
        /// </summary>
        private void DrawWindow(int id)
        {
            var panel = new Rect(0f, 0f, PanelW, _measuredH);
            GUI.DrawTexture(panel, _panelBg);
            Frame(panel, GoldSoft);

            // A 2px gold slab around the whole panel read as a warning box. A hairline
            // does the same job of separating it from the table behind.
            Frame(new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f, panel.height - 6f),
                  new Color(1f, 1f, 1f, 0.04f));

            // IMGUI runs this twice a frame - layout, then repaint - so the record of what
            // has been said has to start empty on each pass, not each frame.
            _said.Clear();

            GUILayout.BeginArea(new Rect(Pad, Pad, PanelW - Pad * 2f, _measuredH));
            GUILayout.BeginVertical();

            DrawTitle();

            if (VersusMode.InMatch) DrawMatchScreen();
            else if (_page == Page.Rules) DrawRulesScreen();
            else if (_page == Page.Deck) DrawDeckScreen();
            else if (_page == Page.Online) DrawOnlineScreen();
            else if (_page == Page.Local) DrawLocalScreen();
            else if (Net.Connected) DrawLobbyScreen();
            else DrawHomeScreen();

            DrawNotice();

            GUILayout.Space(Gap);
            GUILayout.Label(Lobby.Negotiating
                            ? "F7 menu     F8 ready up     F12 abort"
                            : "F7 menu     F8 start match     F12 abort", _small);

            GUILayout.EndVertical();

            // Measure what was just laid out, so the next frame sizes and centres the
            // panel around it exactly instead of guessing.
            if (Event.current.type == EventType.Repaint)
                _measuredH = GUILayoutUtility.GetLastRect().yMax + Pad * 2f;

            GUILayout.EndArea();
        }

        /// <summary>
        /// How much larger to draw the interface. 1.6 is the size it is designed at; on a
        /// window too short or too narrow for that it comes down until it fits, because a
        /// panel drawn past the edge of the screen loses whichever rows fall off it - the
        /// title at the top and the hotkey line at the bottom, which is where someone
        /// stuck would look first.
        /// </summary>
        private const float MaxScale = 1.6f;

        /// <summary>Past this it stops being readable, so it stops shrinking instead.</summary>
        private const float MinScale = 0.8f;

        /// <summary>What the panel and the card view were drawn at last pass.</summary>
        private float _panelScale = MaxScale;
        private float _viewScale = MaxScale;

        private static float FitWidth(float w)
        {
            return Mathf.Clamp(Mathf.Min(MaxScale, Screen.width / Mathf.Max(1f, w)),
                               MinScale, MaxScale);
        }

        private static float FitScale(float w, float h)
        {
            float fit = Mathf.Min(Screen.width / Mathf.Max(1f, w), Screen.height / Mathf.Max(1f, h));
            return Mathf.Clamp(Mathf.Min(MaxScale, fit), MinScale, MaxScale);
        }

        /// <summary>Width of the card view's control bar, and the thing that sizes it.</summary>
        private const float CardBarW = 860f;

        private const float Pad = 24f;   // outer breathing room
        private const float Gap = 14f;   // between related rows
        private const float Section = 22f;   // between groups

        /// <summary>Height of a control-bar button, for lining labels up beside them.</summary>
        private const float ButtonRowHeight = 44f;

        private void DrawTitle()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("INSCRYPTION ONLINE", _header);
            GUILayout.FlexibleSpace();
            GUILayout.Label("v" + Plugin.Version, _version, GUILayout.Width(70f));

            // Reachable with the mouse. F7 still works, but a panel you can only dismiss
            // with a key you have to already know about isn't much of a panel.
            GUILayout.Space(8f);
            if (GUILayout.Button("X", _close, GUILayout.Width(30f), GUILayout.Height(24f)))
                _open = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            Rule();
            GUILayout.Space(6f);
        }

        /// <summary>A hairline divider, used once under the title and nowhere else.</summary>
        private void Rule()
        {
            var r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            var prev = GUI.color;
            GUI.color = GoldSoft;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        // --------------------------------------------------------------------- home

        /// <summary>
        /// One button per thing you might have opened the panel for. Everything behind
        /// them used to be on this screen at once, which made the first thing you saw a
        /// wall rather than a choice.
        /// </summary>
        private bool MenuItem(string label, string caption, bool primary = false)
        {
            bool hit = GUILayout.Button(label, primary ? _primary : _button, GUILayout.Height(46f));
            GUILayout.Space(3f);
            GUILayout.Label(caption, _small);
            GUILayout.Space(Gap);
            return hit;
        }

        /// <summary>A row of the lobby: what it is, what it is set to, and the way in.</summary>
        private bool InfoRow(string label, string value, string action)
        {
            // The value gets a width of its own rather than measuring itself: left to
            // expand it pushed the button clean off the side of the panel.
            const float actionW = 118f;
            float valueW = PanelW - Pad * 2f - 78f - actionW - 16f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _section, GUILayout.Width(78f));
            GUILayout.Label(value, _label, GUILayout.Width(valueW));
            GUILayout.FlexibleSpace();
            bool hit = GUILayout.Button(action, _button, GUILayout.Width(actionW), GUILayout.Height(32f));
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
            return hit;
        }

        private void BackRow(string title)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _section);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back", _quiet, GUILayout.Width(90f))) _page = Page.Home;
            GUILayout.EndHorizontal();
            GUILayout.Space(Gap);
        }

        /// <summary>What the deck row says about itself, wherever it is drawn.</summary>
        private static string DeckSummary()
        {
            int n = DeckStore.Deck.Count;
            return ActInfo.Name(ActInfo.Selected) + "  -  " + n + " card" + (n == 1 ? "" : "s")
                   + (DeckStore.IsValid ? "" : "   (needs " + DeckStore.MinCards + "-"
                                                + DeckStore.MaxCards + ")");
        }

        private void DrawHomeScreen()
        {
            if (Net.HandshakeError != null) { DrawHandshakeError(); return; }

            // Hosting or dialling out is a state of its own: offering the menu while one is
            // already in flight is how you end up with two lobbies and no opponent.
            if (Net.Running && !Net.Connected) { DrawWaitingSection(); return; }

            // Whichever of the two can actually be used leads.
            bool steam = SteamTransport.Available;

            if (MenuItem("ONLINE PLAY", steam
                    ? "Host a Steam lobby, or find one to join."
                    : "Steam isn't running - use local play instead.", primary: steam))
                _page = Page.Online;

            if (MenuItem("LOCAL PLAY", "Play over a LAN, or straight to an address.",
                         primary: !steam))
                _page = Page.Local;

            if (MenuItem("EDIT DECK", DeckSummary())) _page = Page.Deck;

            if (MenuItem("MATCH RULES", MatchRules.Effective.Custom
                    ? "Custom  -  " + MatchRules.Effective.Summary()
                    : "The game's own rules. Yours to set as host."))
                _page = Page.Rules;
        }

        // ------------------------------------------------------------------- online

        private void DrawOnlineScreen()
        {
            BackRow("ONLINE PLAY");

            if (Net.Running && !Net.Connected) { DrawWaitingSection(); return; }

            if (!SteamTransport.Available)
            {
                Say("Steam isn't running, so there are no lobbies to host or find.", _warn);
                GUILayout.Space(Gap);
                if (GUILayout.Button("Local play instead", _button, GUILayout.Height(38f)))
                    _page = Page.Local;
                return;
            }

            DrawFindSection();
        }

        // -------------------------------------------------------------------- local

        private void DrawLocalScreen()
        {
            BackRow("LOCAL PLAY");

            if (Net.Running && !Net.Connected) { DrawWaitingSection(); return; }

            Say("One of you hosts and reads out their address; the other joins it. "
                + "On the same network, that address is the host's local IP.", _small);
            GUILayout.Space(Gap);

            GUILayout.BeginHorizontal();
            Ip = GUILayout.TextField(Ip, _field);
            GUILayout.Label(":", _label, GUILayout.Width(8f));
            PortText = GUILayout.TextField(PortText, _field, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            GUILayout.Space(Gap);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", _primary, GUILayout.Height(40f))) Net.Host(ParsedPort);
            GUILayout.Space(8f);
            if (GUILayout.Button("Join", _primary, GUILayout.Height(40f))) Net.Join(Ip, ParsedPort);
            GUILayout.EndHorizontal();
        }

        // --------------------------------------------------------------------- deck

        private void DrawDeckScreen()
        {
            BackRow("ACT AND DECK");
            DrawDeckSection();
        }

        /// <summary>Waiting on a peer that hasn't turned up yet - and the way out of it.</summary>
        private void DrawWaitingSection()
        {
            bool steam = SteamTransport.Active;

            GUILayout.BeginHorizontal();
            Say(steam ? SteamTransport.Status : Net.StatusLine,
                StyleFor(steam ? SteamTransport.StatusKind : NoticeKind.Busy));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", _quiet, GUILayout.Width(100f))) Net.Shutdown();
            GUILayout.EndHorizontal();

            if (steam && SteamTransport.IsHost)
                Say("They press Find Games and pick your name from the list.", _small);
        }

        /// <summary>Host a lobby, or go looking for one.</summary>
        private void DrawFindSection()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host Lobby", _primary, GUILayout.Height(40f))) SteamTransport.HostLobby();
            GUILayout.Space(8f);

            GUI.enabled = !SteamTransport.BusySearching;
            if (GUILayout.Button(SteamTransport.BusySearching ? "Searching..." : "Find Games",
                                 _primary, GUILayout.Height(40f)))
                SteamTransport.RefreshLobbies();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            Say(SteamTransport.Status, StyleFor(SteamTransport.StatusKind));

            if (SteamTransport.Lobbies.Count == 0) return;

            GUILayout.Space(6f);
            _lobbyScroll = GUILayout.BeginScrollView(_lobbyScroll, GUILayout.Height(96f));
            foreach (var lobby in SteamTransport.Lobbies)
                if (GUILayout.Button(lobby.Value, _button)) SteamTransport.JoinLobby(lobby.Key);
            GUILayout.EndScrollView();
        }

        /// <summary>
        /// Picking an act and building its deck. On both screens because none of it needs a
        /// peer - it was only reachable once connected, so you couldn't build a deck until
        /// someone was waiting on you.
        /// </summary>
        private void DrawDeckSection()
        {
            GUILayout.Label("ACT", _section);

            // Fixed widths on both rows, so each tally sits under the act it belongs to.
            const float actGap = 8f;
            float actW = (PanelW - Pad * 2f - actGap * 2f) / 3f;
            MatchAct[] acts = { MatchAct.Act1, MatchAct.Act2, MatchAct.Act3 };

            GUILayout.BeginHorizontal();
            foreach (MatchAct act in acts)
            {
                if (act != MatchAct.Act1) GUILayout.Space(actGap);
                bool chosen = ActInfo.Selected == act;

                // The number is the whole reason to prefer one of these over another, and
                // it used to take three clicks to see all three.
                int cards = DeckStore.CountFor(act);
                string label = cards < 0 ? ActInfo.Name(act) : ActInfo.Name(act) + "   " + cards;

                if (GUILayout.Button(label, chosen ? _primary : _button,
                                     GUILayout.Width(actW), GUILayout.Height(38f)))
                    Lobby.ChooseAct(act);
            }
            GUILayout.EndHorizontal();

            // Who wants what. Only meaningful with someone on the other end to disagree.
            if (Lobby.Negotiating)
            {
                GUILayout.BeginHorizontal();
                foreach (MatchAct act in acts)
                {
                    if (act != MatchAct.Act1) GUILayout.Space(actGap);
                    int votes = Lobby.VotesFor(act);
                    string mark = votes == 2 ? "BOTH"
                                : votes == 0 ? ""
                                : Lobby.MyAct == act ? "YOU" : "THEM";
                    GUILayout.Label(mark, votes == 2 ? _voteBoth : _vote, GUILayout.Width(actW));
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(2f);
                if (Lobby.ActAgreed)
                    Say(ActInfo.Name(Lobby.MyAct) + " agreed - 2/2", _good);
                else if (Lobby.PeerAct.HasValue)
                    Say("They want " + ActInfo.Name(Lobby.PeerAct.Value)
                        + " - agree on one act to start.", _warn);
                else
                    Say("Waiting for your opponent to pick an act...", _busy);
            }

            GUILayout.Space(Gap);
            if (GUILayout.Button("Open the card table   ("
                                 + DeckStore.Deck.Count + "/" + DeckStore.MaxCards + ")",
                                 _primary, GUILayout.Height(40f)))
                OpenCardView();

            if (!DeckStore.IsValid)
                Say("Deck needs " + DeckStore.MinCards + "-" + DeckStore.MaxCards + " cards.", _warn);
            if (NativeDeckBuilder.LastError != null)
                Say(NativeDeckBuilder.LastError, _warn);
        }

        // -------------------------------------------------------------------- rules

        /// <summary>
        /// Every dial, on its own screen. It is a screen rather than a section because
        /// nine settings under the act buttons buries the two things you came here to do.
        /// </summary>
        private void DrawRulesScreen()
        {
            bool ours = MatchRules.WeDecide;
            Rules rules = MatchRules.Effective;

            BackRow(ours ? "SET THE RULES" : "THE HOST'S RULES");

            const float labelW = 150f;
            const float arrowW = 34f;
            const float gap = 6f;
            float valueW = PanelW - Pad * 2f - labelW - (arrowW + gap) * 2f;

            foreach (RuleBook.Dial dial in RuleBook.Dials)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(dial.Label, _dim, GUILayout.Width(labelW));

                GUI.enabled = ours;
                if (GUILayout.Button("<", _arrow, GUILayout.Width(arrowW), GUILayout.Height(30f)))
                {
                    MatchRules.Cycle(dial.Key, -1);
                    _explaining = dial.Key;
                }
                GUILayout.Space(gap);

                GUILayout.Label(dial.TextOf(rules), _dialValue,
                                GUILayout.Width(valueW), GUILayout.Height(30f));

                GUILayout.Space(gap);
                if (GUILayout.Button(">", _arrow, GUILayout.Width(arrowW), GUILayout.Height(30f)))
                {
                    MatchRules.Cycle(dial.Key, 1);
                    _explaining = dial.Key;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(2f);
            }

            GUILayout.Space(Gap);

            // One note at a time, for whichever dial was last touched. Nine notes on screen
            // at once is a wall nobody reads.
            RuleBook.Dial shown = _explaining == null ? null : RuleBook.Find(_explaining);
            GUILayout.Label(shown != null ? shown.Note : "Change a rule to read what it does.",
                            _small);

            if (!ours)
            {
                GUILayout.Space(Gap);
                Say("Only the host can change these. You play under whatever they set.", _busy);
                return;
            }

            GUILayout.Space(Gap);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to the game's rules", _quiet, GUILayout.Height(34f)))
            {
                MatchRules.ResetToDefaults();
                _explaining = null;
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("Done", _primary, GUILayout.Width(120f), GUILayout.Height(34f)))
                _page = Page.Home;
            GUILayout.EndHorizontal();

            if (!rules.Custom)
            {
                GUILayout.Space(4f);
                Say("Custom rules are switched off, so the match will use the defaults.", _warn);
            }
        }

        // -------------------------------------------------------------------- lobby

        /// <summary>
        /// Two people in a room deciding what to play. Both seats are on screen with what
        /// each of them has committed to, because "1/2" only means something once you can
        /// see which one of you it is.
        /// </summary>
        private void DrawLobbyScreen()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("LOBBY", _section);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Leave", _quiet, GUILayout.Width(100f)))
            {
                Net.Shutdown();
                _page = Page.Home;
            }
            GUILayout.EndHorizontal();

            if (Net.HandshakeError != null)
            {
                GUILayout.Space(Gap);
                DrawHandshakeError();
                return;   // nothing below this is usable until the versions agree
            }

            GUILayout.Space(8f);
            DrawSeats();

            // The result of the last match doubles as the invitation to play another.
            if (VersusMode.LastResult != null)
            {
                GUILayout.Space(Gap);
                Say(VersusMode.LastResult, _resultText);
            }

            GUILayout.Space(Section);

            string actLine = ActInfo.Name(ActInfo.Selected)
                + (!Lobby.Negotiating ? ""
                   : Lobby.ActAgreed ? "  -  you both agree"
                   : Lobby.PeerAct.HasValue ? "  -  they want " + ActInfo.Name(Lobby.PeerAct.Value)
                   : "  -  waiting on them");

            if (InfoRow("ACT", actLine, "Change")) _page = Page.Deck;
            if (InfoRow("DECK", DeckSummary(), "Edit")) OpenCardView();

            // The row itself stays short so the button always has its place; what actually
            // changed goes underneath, where it can wrap.
            Rules live = MatchRules.Effective;
            if (InfoRow("RULES", live.Custom ? "Custom" : "The game's own rules",
                        MatchRules.WeDecide ? "Change" : "See all"))
            {
                _explaining = null;
                _page = Page.Rules;
            }

            if (live.Custom && !live.IsVanilla)
            {
                GUILayout.Space(-4f);
                Say(live.Summary(), _good);
                GUILayout.Space(8f);
            }

            if (MatchRules.DowngradedForPeer)
                Say("Their build is older and can't be told the rules - this match uses the defaults.",
                    _warn);

            GUILayout.Space(Section);
            DrawReadyButton();

        }

        /// <summary>The two players and where each of them stands.</summary>
        private void DrawSeats()
        {
            Seat(Net.IsHost ? Player.LocalName : Player.PeerName,
                 host: true,
                 ready: Net.IsHost ? Lobby.MyReady : Lobby.PeerReady,
                 you: Net.IsHost);

            Seat(Net.IsHost ? Player.PeerName : Player.LocalName,
                 host: false,
                 ready: Net.IsHost ? Lobby.PeerReady : Lobby.MyReady,
                 you: !Net.IsHost);
        }

        private void Seat(string name, bool host, bool ready, bool you)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label((ready ? "*  " : "-  ") + name + (host ? "   (host)" : "")
                            + (you ? "   - you" : ""),
                            _seat);
            GUILayout.FlexibleSpace();
            GUILayout.Label(ready ? "READY" : "not ready", ready ? _seatReady : _seatState,
                            GUILayout.Width(110f));
            GUILayout.EndHorizontal();
        }

        // --------------------------------------------------------------------- chat

        private const float ChatW = 330f;
        private const float ChatLogH = 132f;

        /// <summary>Folded away until there is something to say. Remembered per session.</summary>
        private bool _chatOpen;

        /// <summary>Whether the chat box had the keyboard last time we drew it.</summary>
        internal static bool ChatHasFocus { get; private set; }

        /// <summary>How many lines had been said when the box was last looked at.</summary>
        private int _chatSeen;

        /// <summary>Set after sending, so the box takes the keyboard back next frame.</summary>
        private bool _refocusChat;

        private bool Unread => Chat.Count > _chatSeen;

        /// <summary>
        /// Its own window in the corner rather than a section of the panel: it belongs to
        /// the whole session, not to one screen, and the panel had enough in it already.
        /// Drawn over the board during a match too, which is the only time it matters.
        /// </summary>
        private void DrawChatWindow()
        {
            if (!Net.Connected) return;

            float scale = FitWidth(ChatW + 60f);
            Matrix4x4 restore = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float vw = Screen.width / scale;
            float vh = Screen.height / scale;

            const float pad = 10f, headH = 30f, rowH = 30f, margin = 16f;
            float bodyH = _chatOpen ? ChatLogH + rowH + pad : 0f;
            float h = headH + bodyH + pad;

            // Folded it is a tab, not an empty panel: a wide blank box in the corner reads
            // as something that failed to draw.
            float w = _chatOpen ? ChatW : 150f;
            var box = new Rect(vw - w - margin, vh - h - margin, w, h);

            GUI.DrawTexture(box, _chipBg);
            Frame(box, Unread && !_chatOpen ? Gold : GoldSoft);

            // The header doubles as the fold: one click, and it says what it is hiding.
            string head = _chatOpen ? "CHAT" : Unread ? "CHAT   " + (Chat.Count - _chatSeen) + " new"
                                                      : "CHAT";
            var headRect = new Rect(box.x + pad, box.y + pad * 0.5f, box.width - pad * 2f, headH);

            if (GUI.Button(headRect, head + (_chatOpen ? "   -" : "   +"), _chatHead))
            {
                _chatOpen = !_chatOpen;
                if (_chatOpen) _chatSeen = Chat.Count;
            }

            if (!_chatOpen) { GUI.matrix = restore; return; }
            _chatSeen = Chat.Count;

            GUILayout.BeginArea(new Rect(box.x + pad, box.y + headH + pad * 0.5f,
                                         box.width - pad * 2f, bodyH));

            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(ChatLogH));
            if (Chat.Count == 0)
            {
                GUILayout.Label(VersusMode.InMatch
                    ? "Keys 1-" + Chat.Emotes.Length + " say something without typing."
                    : "Say hello.", _chatSystem);
            }
            else
            {
                foreach (Chat.Line line in Chat.All)
                {
                    GUIStyle style = line.System ? _chatSystem : line.Mine ? _chatMine : _chatLine;
                    GUILayout.Label(line.System ? line.Text : line.Who + ":  " + line.Text, style);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4f);

            // Caught before the field is drawn, and consumed. A text field handles the
            // event first if you let it, so a check underneath never sees the Return -
            // which is why the button worked and the key did nothing.
            bool entered = Event.current.type == EventType.KeyDown
                           && (Event.current.keyCode == KeyCode.Return
                               || Event.current.keyCode == KeyCode.KeypadEnter)
                           && GUI.GetNameOfFocusedControl() == "mp-chat";

            if (entered) Event.current.Use();

            GUILayout.BeginHorizontal();

            GUI.SetNextControlName("mp-chat");
            _draft = GUILayout.TextField(_draft, Chat.MaxLength, _field);
            GUILayout.Space(6f);
            bool send = GUILayout.Button("Send", _button, GUILayout.Width(72f));

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            ChatHasFocus = GUI.GetNameOfFocusedControl() == "mp-chat";

            if (send || entered)
            {
                if (Chat.Say(_draft))
                {
                    _draft = "";

                    // The field keeps its own copy of the text while it has the keyboard,
                    // so clearing the string alone leaves what you just sent sitting in
                    // the box. Dropping focus makes it read the empty one back.
                    GUIUtility.keyboardControl = 0;
                    _refocusChat = true;
                }
                _chatSeen = Chat.Count;
            }

            // Asked for on the frame after, so you can keep typing without clicking back.
            if (_refocusChat && Event.current.type == EventType.Repaint)
            {
                _refocusChat = false;
                GUI.FocusControl("mp-chat");
            }

            GUI.matrix = restore;
        }

        /// <summary>
        /// Starting is a commitment from both players, not a command one gives the other. The
        /// button records yours and lights up for real once theirs arrives.
        /// </summary>
        private void DrawReadyButton()
        {
            // Loading a scene takes a couple of seconds. Without this the button looked
            // like it had done nothing.
            bool busy = VersusMode.PendingStart || NativeDeckBuilder.PendingOpen;

            if (!Lobby.Negotiating)
            {
                // An older build can't vote or commit, so it keeps the behaviour it knows:
                // either player starts, and both are pulled in.
                string legacy = busy ? "STARTING..."
                              : VersusMode.LastResult != null ? "PLAY AGAIN"
                              : "START MATCH";

                GUI.enabled = DeckStore.IsValid && !busy;
                if (GUILayout.Button(legacy, _primary, GUILayout.Height(52f)))
                    VersusMode.StartAnywhere(this);
                GUI.enabled = true;

                GUILayout.Space(4f);
                Say("Their build is older - either player can start, and you both enter together.",
                    _small);
                return;
            }

            string blocker = Lobby.Blocker;
            string label = busy || Lobby.BothReady ? "STARTING..."
                         : Lobby.MyReady ? "WAITING FOR OPPONENT   " + Lobby.ReadyCount + "/2"
                         : "READY   " + Lobby.ReadyCount + "/2";

            // Backing out of a commitment stays available even when something else has since
            // gone wrong - otherwise a disagreement over acts strands you as permanently ready.
            GUI.enabled = !busy && (Lobby.MyReady || blocker == null);
            if (GUILayout.Button(label, Lobby.MyReady ? _button : _primary, GUILayout.Height(52f)))
                Lobby.ToggleReady();
            GUI.enabled = true;

            GUILayout.Space(4f);
            if (blocker != null) Say(blocker, _warn);
            else if (Lobby.MyReady) Say("Press again to take it back.", _small);
            else Say("The match starts the moment both of you are ready.", _small);
        }

        // -------------------------------------------------------------------- match

        /// <summary>
        /// Mid-match the board is what matters, so this stays to the few things you would
        /// actually open the panel for.
        /// </summary>
        private void DrawMatchScreen()
        {
            GUILayout.Label(TurnOrder.IsMyTurn ? "YOUR TURN" : "Opponent's turn",
                            TurnOrder.IsMyTurn ? _resultText : _label);
            GUILayout.Label(ActInfo.Name(ActInfo.Current) + "  -  " + Net.StatusLine, _small);

            if (!MatchRules.Live.IsVanilla)
            {
                GUILayout.Space(4f);
                GUILayout.Label(MatchRules.Live.Summary(), _good);
            }

            if (VersusMode.Suspended)
            {
                GUILayout.Space(Gap);
                GUILayout.Label("OPPONENT DISCONNECTED", _section);
                GUILayout.Label("Holding the match for "
                                + Mathf.CeilToInt(VersusMode.SuspendedSecondsLeft) + "s.", _small);
                GUILayout.Label("They can relaunch and rejoin - nothing is lost.", _small);
            }


            GUILayout.Space(Gap);
            if (GUILayout.Button(VersusMode.Suspended ? "Give Up Waiting" : "Abort Match",
                                 _button, GUILayout.Height(34f)))
                VersusMode.Abort(this);
        }

        private void DrawHandshakeError()
        {
            GUILayout.Label("INCOMPATIBLE VERSIONS", _section);
            GUILayout.Label(Net.HandshakeError, _warn);
            GUILayout.Label("Both players need the same build of the mod.", _small);
        }

        /// <summary>Opens the chat box and puts the cursor in it. Used by the autopilot.</summary>
        public static void OpenChat()
        {
            if (_instance == null) return;
            _instance._chatOpen = true;
            _instance._refocusChat = true;
        }

        /// <summary>Opens the panel straight onto the rules. Used by the autopilot.</summary>
        public static void OpenRules()
        {
            if (_instance == null) return;
            _instance._open = true;
            _instance._page = Page.Rules;
        }

        /// <summary>Opens the deck table. Used by the autopilot to photograph it.</summary>
        public static void OpenDeckTable()
        {
            if (_instance == null) return;
            _instance.OpenCardView();
        }

        /// <summary>Opens the panel from the title screen's multiplayer card.</summary>
        public static void OpenFromMenuCard()
        {
            if (_instance == null)
            {
                Trace.Warn("[menu] no panel instance to open");
                return;
            }
            _instance._open = true;
        }

        private static MpMenu _instance;

        private void Awake() => _instance = this;
        private void OnDestroy() { if (_instance == this) _instance = null; }

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
