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

        /// <summary>Direct/LAN is a fallback, so it stays folded away until asked for.</summary>
        private bool _showDirect;


        private Rect _rect;
        private Vector2 _lobbyScroll;

        private GUIStyle _chip, _label, _dim, _header, _section, _field, _button, _small;
        private GUIStyle _primary, _quiet, _warn, _resultText, _good, _busy, _close, _pageLabel;
        private GUIStyle _countLabel;
        private GUIStyle _chipState, _chipInfo;
        private Texture2D _panelBg, _chipBg, _accent;

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

            // A drop suspends the match rather than forfeiting it; the transports keep
            // trying to re-establish underneath.
            if (VersusMode.InMatch && !Net.Connected) VersusMode.NoteDisconnected();
            if (VersusMode.InMatch && Net.Connected && VersusMode.Suspended) VersusMode.NoteReconnected();
            VersusMode.TickSuspension(this);
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
                                            _pageLabel, _countLabel })
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
                Matrix4x4 barRestore = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
                DrawCardViewHint();
                GUI.matrix = barRestore;

                DrawCursor();
                return;
            }

            if (!_open)
            {
                Matrix4x4 chipRestore = GUI.matrix;
                GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
                DrawChip();
                GUI.matrix = chipRestore;
                return;
            }

            // Positions are worked out in unscaled space, then the matrix magnifies it.
            float vw = Screen.width / UiScale;
            float vh = Screen.height / UiScale;
            _rect = new Rect((vw - PanelW) * 0.5f,
                             (vh - _measuredH) * 0.5f,
                             PanelW, _measuredH);
            // Deliberately NOT GUI.Window: IMGUI composites windows on top of everything
            // else drawn in OnGUI regardless of call order, which painted over the cursor
            // every frame. We draw our own background anyway, so the window bought nothing.
            Matrix4x4 restore = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));

            GUI.BeginGroup(_rect);
            DrawWindow(0);
            GUI.EndGroup();

            // The cursor is drawn from raw screen coordinates, so it has to be outside
            // the scaled matrix or it lands in the wrong place.
            GUI.matrix = restore;
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
            if (Net.Connected) return "READY";
            if (Net.Running) return Net.IsHost ? "WAITING" : "CONNECTING";
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
            if (Net.Connected) return "Press F7 to start a match";
            if (Net.Running) return Net.IsHost ? "Waiting for someone to join" : "Connecting to host";
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

        /// <summary>The always-on status marker.</summary>
        private void DrawChip()
        {
            string state = StateText();
            string detail = DetailText();
            bool notice = Notice.Visible;

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

        private GUIStyle NoticeStyle()
        {
            switch (Notice.Kind)
            {
                case NoticeKind.Bad:  return _warn;
                case NoticeKind.Good: return _good;
                case NoticeKind.Busy: return _busy;
                default:              return _small;
            }
        }

        /// <summary>The notice, drawn inside the panel so it's visible with the menu open.</summary>
        private void DrawNotice()
        {
            if (!Notice.Visible) return;
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
                ? $"max {DeckStore.MaxAddedSigils} sigils"
                : $"deck {DeckStore.Deck.Count}/{DeckStore.MaxCards}";

            const float w = 860f;
            const float barPad = 14f;

            var bar = new Rect((Screen.width / UiScale - w) * 0.5f, 12f, w, _barH);

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

            GUILayout.BeginArea(new Rect(Pad, Pad, PanelW - Pad * 2f, _measuredH));
            GUILayout.BeginVertical();

            DrawTitle();

            if (VersusMode.InMatch) DrawMatchScreen();
            else if (Net.Connected) DrawReadyScreen();
            else DrawConnectScreen();

            DrawNotice();

            GUILayout.Space(Gap);
            GUILayout.Label("F7 menu     F8 start match     F12 abort", _small);

            GUILayout.EndVertical();

            // Measure what was just laid out, so the next frame sizes and centres the
            // panel around it exactly instead of guessing.
            if (Event.current.type == EventType.Repaint)
                _measuredH = GUILayoutUtility.GetLastRect().yMax + Pad * 2f;

            GUILayout.EndArea();
        }

        /// <summary>How much larger to draw the whole interface.</summary>
        private const float UiScale = 1.6f;

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
            GUILayout.Label("v" + Plugin.Version, _small);

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

        // ------------------------------------------------------------------ connect

        /// <summary>No opponent yet, so this screen is only about getting one.</summary>
        private void DrawConnectScreen()
        {
            GUILayout.Label("Find someone to play", _dim);
            GUILayout.Space(Gap);

            if (Net.HandshakeError != null) DrawHandshakeError();

            if (!SteamTransport.Available)
            {
                GUILayout.Label("Steam not detected - use a direct address.", _small);
                _showDirect = true;
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host Lobby", _primary, GUILayout.Height(40f))) SteamTransport.HostLobby();
                GUILayout.Space(8f);
                if (GUILayout.Button("Find Games", _primary, GUILayout.Height(40f))) SteamTransport.RefreshLobbies();
                GUILayout.EndHorizontal();

                GUILayout.Space(6f);
                GUILayout.Label(SteamTransport.Status, _small);

                if (SteamTransport.Lobbies.Count > 0)
                {
                    GUILayout.Space(6f);
                    _lobbyScroll = GUILayout.BeginScrollView(_lobbyScroll, GUILayout.Height(96f));
                    foreach (var lobby in SteamTransport.Lobbies)
                        if (GUILayout.Button(lobby.Value, _button)) SteamTransport.JoinLobby(lobby.Key);
                    GUILayout.EndScrollView();
                }
            }

            GUILayout.Space(Section);
            DrawDirectSection();
        }

        /// <summary>
        /// Folded away by default. Almost everyone uses Steam, and an IP field plus a port
        /// field permanently on screen made the panel look like a network utility.
        /// </summary>
        private void DrawDirectSection()
        {
            if (SteamTransport.Available)
            {
                string arrow = _showDirect ? "v" : ">";
                if (GUILayout.Button(arrow + "  Direct connect (LAN / non-Steam)", _quiet))
                    _showDirect = !_showDirect;
                if (!_showDirect) return;
                GUILayout.Space(6f);
            }

            GUILayout.BeginHorizontal();
            Ip = GUILayout.TextField(Ip, _field);
            GUILayout.Label(":", _label, GUILayout.Width(8f));
            PortText = GUILayout.TextField(PortText, _field, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host", _button)) Net.Host(ParsedPort);
            GUILayout.Space(8f);
            if (GUILayout.Button("Join", _button)) Net.Join(Ip, ParsedPort);
            GUILayout.EndHorizontal();
        }

        // -------------------------------------------------------------------- ready

        /// <summary>Connected and out of a match: pick an act, check the deck, go.</summary>
        private void DrawReadyScreen()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Net.StatusLine, _label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Disconnect", _quiet, GUILayout.Width(100f))) Net.Shutdown();
            GUILayout.EndHorizontal();

            if (Net.HandshakeError != null)
            {
                GUILayout.Space(Gap);
                DrawHandshakeError();
                return;   // nothing below this is usable until the versions agree
            }

            // The result of the last match doubles as the invitation to play another.
            if (VersusMode.LastResult != null)
            {
                GUILayout.Space(Gap);
                GUILayout.Label("Last match: " + VersusMode.LastResult, _resultText);
            }

            GUILayout.Space(Section);
            GUILayout.Label("ACT", _section);
            GUILayout.BeginHorizontal();
            foreach (MatchAct act in new[] { MatchAct.Act1, MatchAct.Act2, MatchAct.Act3 })
            {
                if (act != MatchAct.Act1) GUILayout.Space(8f);
                bool selected = ActInfo.Selected == act;
                if (GUILayout.Button(ActInfo.Name(act), selected ? _primary : _button, GUILayout.Height(38f)))
                {
                    ActInfo.Selected = act;
                    SteamTransport.PublishSelectedAct();   // keep a hosted lobby honest
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(Gap);
            if (GUILayout.Button("Edit " + ActInfo.Name(ActInfo.Selected) + " deck   ("
                                 + DeckStore.Deck.Count + "/" + DeckStore.MaxCards + ")",
                                 _button, GUILayout.Height(38f)))
                OpenCardView();

            if (!DeckStore.IsValid)
                GUILayout.Label("Deck needs " + DeckStore.MinCards + "-" + DeckStore.MaxCards + " cards.", _warn);
            if (NativeDeckBuilder.LastError != null)
                GUILayout.Label(NativeDeckBuilder.LastError, _warn);

            GUILayout.Space(Section);

            // Loading a scene takes a couple of seconds. Without this the button looked
            // like it had done nothing, which is the bug this whole pass is about.
            bool busy = VersusMode.PendingStart || NativeDeckBuilder.PendingOpen;
            string go = busy ? "STARTING..."
                      : VersusMode.LastResult != null ? "PLAY AGAIN"
                      : "START MATCH";

            GUI.enabled = DeckStore.IsValid && !busy;
            if (GUILayout.Button(go, _primary, GUILayout.Height(52f))) VersusMode.StartAnywhere(this);
            GUI.enabled = true;

            GUILayout.Space(4f);
            GUILayout.Label("Either player can start - you both enter together.", _small);
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
