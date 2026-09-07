using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Steamworks;

namespace InscryptionMP
{
    /// <summary>Steam lobby + P2P transport.</summary>
    public static class SteamTransport
    {
        private const string LobbyKey = "inscryption_mp";
        private const string LobbyValue = "1";
        private const string LobbyHostKey = "host_name";
        private const string LobbyActKey = "act";

        private static PropertyInfoCache _initCache;

        /// <summary>
        /// The game's SteamManager is internal, so its Initialized flag is read
        /// reflectively rather than by referencing the type directly.
        /// </summary>
        public static bool Available
        {
            get
            {
                try
                {
                    if (_initCache == null) _initCache = new PropertyInfoCache();
                    return _initCache.Read();
                }
                catch (Exception e)
                {
                    Trace.Warn("[steam] availability check failed: " + e.Message);
                    return false;
                }
            }
        }

        private class PropertyInfoCache
        {
            private readonly System.Reflection.PropertyInfo _prop;

            public PropertyInfoCache()
            {
                Type t = AccessTools.TypeByName("SteamManager");
                _prop = t == null ? null : AccessTools.Property(t, "Initialized");
                if (_prop == null) Trace.Warn("[steam] SteamManager.Initialized not found");
            }

            public bool Read()
            {
                if (_prop == null) return false;
                object v = _prop.GetValue(null, null);
                return v is bool b && b;
            }
        }
        public static bool Active { get; private set; }
        public static bool Connected { get; private set; }
        public static bool IsHost { get; private set; }
        public static string Status { get; private set; } = "idle";

        /// <summary>
        /// How the status line should read - in progress, worked, failed. The panel draws
        /// Status once and colours it with this, rather than also raising a notice that says
        /// the same thing again two rows further down.
        /// </summary>
        public static NoticeKind StatusKind { get; private set; } = NoticeKind.Info;

        private static void SetStatus(string text, NoticeKind kind = NoticeKind.Info)
        {
            Status = text;
            StatusKind = kind;
        }

        private static CSteamID _lobby;
        private static CSteamID _peer;

        private static Callback<LobbyCreated_t> _cbCreated;
        private static Callback<LobbyEnter_t> _cbEntered;
        private static Callback<P2PSessionRequest_t> _cbSession;
        private static Callback<LobbyChatUpdate_t> _cbChatUpdate;
        private static CallResult<LobbyMatchList_t> _crList;

        /// <summary>True between requesting a lobby list and the result arriving.</summary>
        public static bool Searching { get; private set; }

        /// <summary>
        /// Whether the current search is the background sweep rather than something the
        /// player asked for. A quiet one says nothing until it has an answer.
        /// </summary>
        private static bool _quietSearch;

        /// <summary>A search worth showing a spinner for.</summary>
        public static bool BusySearching => Searching && !_quietSearch;

        private static int _lastSearchAt;
        private const int AutoRefreshMs = 5000;

        /// <summary>
        /// When the pending search started. Steam should always answer, but if it ever doesn't,
        /// a stuck flag would disable Find Games for the rest of the session - so a stale
        /// search is allowed to be replaced.
        /// </summary>
        private static int _searchStartedAt;
        private const int SearchTimeoutMs = 10000;

        private static readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();
        private static readonly StringBuilder RecvBuffer = new StringBuilder();

        /// <summary>Lobbies found by the last refresh: (id, display name).</summary>
        public static readonly List<KeyValuePair<CSteamID, string>> Lobbies =
            new List<KeyValuePair<CSteamID, string>>();

        private static void EnsureCallbacks()
        {
            if (_cbCreated != null) return;
            _cbCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _cbEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _cbSession = Callback<P2PSessionRequest_t>.Create(OnSessionRequest);
            _cbChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
            _crList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);
        }

        public static void HostLobby()
        {
            if (!Available) { Trace.Warn("[steam] not initialised"); return; }

            // Pressing Host again while already hosting used to create a second lobby and
            // abandon the first, leaving one advertised that nobody was actually watching.
            if (Active && IsHost) { Trace.Info("[steam] already hosting"); return; }

            Net.ShutdownTcp();   // one transport at a time
            EnsureCallbacks();
            Reset();
            Lobbies.Clear();
            Active = true;
            IsHost = true;
            SetStatus("creating lobby...", NoticeKind.Busy);
            Trace.Info("[steam] creating lobby");
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 2);
        }

        /// <summary>
        /// Re-advertises the act while hosting. The picker can change after the lobby is
        /// created, and a lobby claiming the wrong act is worse than one claiming none.
        /// </summary>
        public static void PublishSelectedAct()
        {
            if (!IsHost || !_lobby.IsValid() || !Available) return;
            SteamMatchmaking.SetLobbyData(_lobby, LobbyActKey, ((int)ActInfo.Selected).ToString());
        }

        public static void RefreshLobbies() => Search(quiet: false);

        /// <summary>
        /// A sweep while the find screen is on show. A list that only changes when you press
        /// a button is a snapshot of the instant you pressed it, and a lobby hosted a second
        /// later stays invisible until you think to press again.
        /// </summary>
        public static void AutoRefresh()
        {
            if (!Available || Active || Searching) return;
            if (unchecked(Environment.TickCount - _lastSearchAt) < AutoRefreshMs) return;
            Search(quiet: true);
        }

        private static void Search(bool quiet)
        {
            if (!Available) { SetStatus("Steam not initialised", NoticeKind.Bad); Trace.Warn("[steam] not initialised"); return; }
            EnsureCallbacks();

            // One CallResult at a time: setting a new handle abandons the pending one, so
            // repeated presses used to cancel the search each time and look like nothing
            // was happening at all.
            if (Searching && Environment.TickCount - _searchStartedAt < SearchTimeoutMs)
            {
                Trace.Info("[steam] already searching");
                return;
            }

            // Deliberately not clearing the list here: the results replace it when they
            // arrive, and emptying it up front made the panel blink and resize on every
            // background sweep.
            Searching = true;
            _quietSearch = quiet;
            _searchStartedAt = Environment.TickCount;
            _lastSearchAt = _searchStartedAt;
            if (!quiet) SetStatus("searching...", NoticeKind.Busy);
            Trace.Info("[steam] requesting lobby list" + (quiet ? " (background)" : ""));
            SteamMatchmaking.AddRequestLobbyListStringFilter(
                LobbyKey, LobbyValue, ELobbyComparison.k_ELobbyComparisonEqual);

            // A lobby seats two and a match needs both, so one that has already paired up is
            // not something a third player should be offered.
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
            _crList.Set(call);
        }

        public static void JoinLobby(CSteamID lobby)
        {
            if (!Available) return;
            Net.ShutdownTcp();   // one transport at a time
            EnsureCallbacks();
            Reset();
            Lobbies.Clear();
            Active = true;
            IsHost = false;

            // You picked the lobby that said Act 3 because you wanted Act 3. Opening your
            // vote on whatever was selected before makes the first thing you see on arrival
            // a disagreement you did not have.
            string actRaw = SteamMatchmaking.GetLobbyData(lobby, LobbyActKey);
            if (int.TryParse(actRaw, out int actNum) && actNum >= 1 && actNum <= 3)
                Lobby.ChooseAct((MatchAct)actNum);

            SetStatus("joining...", NoticeKind.Busy);
            Trace.Info("[steam] joining lobby " + lobby);
            SteamMatchmaking.JoinLobby(lobby);
        }

        private static void OnLobbyCreated(LobbyCreated_t e)
        {
            if (e.m_eResult != EResult.k_EResultOK)
            {
                SetStatus("couldn't create a lobby (" + e.m_eResult + ")", NoticeKind.Bad);
                Trace.Error("[steam] lobby creation failed: " + e.m_eResult);
                Active = false;
                return;
            }

            _lobby = new CSteamID(e.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_lobby, LobbyKey, LobbyValue);
            SteamMatchmaking.SetLobbyData(_lobby, LobbyHostKey, SteamFriends.GetPersonaName());

            // The host's act decides the table for both players, so say which one before
            // anyone commits to joining.
            SteamMatchmaking.SetLobbyData(_lobby, LobbyActKey, ((int)ActInfo.Selected).ToString());
            SetStatus("waiting for an opponent to join", NoticeKind.Busy);
            Trace.Info("[steam] lobby created - waiting for opponent");
        }

        private static void OnLobbyList(LobbyMatchList_t e, bool failed)
        {
            Searching = false;
            bool quiet = _quietSearch;
            _quietSearch = false;

            if (failed)
            {
                // A background sweep failing is not news, and wiping a good list over it
                // would be worse than showing one a few seconds stale.
                if (quiet) { Trace.Warn("[steam] background lobby search failed"); return; }
                Lobbies.Clear();
                SetStatus("search failed - try again", NoticeKind.Bad);
                return;
            }

            Lobbies.Clear();

            CSteamID me = SteamUser.GetSteamID();
            for (int i = 0; i < e.m_nLobbiesMatching; i++)
            {
                CSteamID id = SteamMatchmaking.GetLobbyByIndex(i);

                // Your own lobby comes back in the search results. Joining it does
                // nothing, so don't offer it.
                if (SteamMatchmaking.GetLobbyOwner(id) == me) continue;

                // Belt and braces alongside the slots filter: a lobby that filled up between
                // the request and the reply is no longer somewhere a third player can go.
                if (SteamMatchmaking.GetNumLobbyMembers(id) >= 2) continue;

                string name = SteamMatchmaking.GetLobbyData(id, LobbyHostKey);
                if (string.IsNullOrEmpty(name)) name = id.ToString();

                string actRaw = SteamMatchmaking.GetLobbyData(id, LobbyActKey);
                if (int.TryParse(actRaw, out int actNum) && actNum >= 1 && actNum <= 3)
                    name += "   -   " + ActInfo.Name((MatchAct)actNum);

                Lobbies.Add(new KeyValuePair<CSteamID, string>(id, name));
            }
            // Say plainly that a search happened and came back empty. Drawing nothing at
            // all is indistinguishable from the button not working.
            if (Lobbies.Count == 0)
                SetStatus("no open games - someone has to Host Lobby first");
            else
                SetStatus(Lobbies.Count + (Lobbies.Count == 1 ? " open game" : " open games")
                          + " - pick one to join", NoticeKind.Good);
            Trace.Info("[steam] found " + Lobbies.Count + " lobbies");
        }

        private static void OnLobbyEntered(LobbyEnter_t e)
        {
            _lobby = new CSteamID(e.m_ulSteamIDLobby);
            Active = true;

            CSteamID owner = SteamMatchmaking.GetLobbyOwner(_lobby);
            CSteamID me = SteamUser.GetSteamID();
            IsHost = owner == me;

            TryFindPeer(me);

            if (_peer.IsValid()) NotePeerArrived(_peer);
            else
            {
                SetStatus("waiting for an opponent to join", NoticeKind.Busy);
                Trace.Info("[steam] in lobby, waiting for opponent");
            }
        }

        /// <summary>
        /// One place for "we have an opponent". It used to be written out at each of the four
        /// points a peer can turn up, and closing the lobby to newcomers was missing from
        /// every one of them.
        /// </summary>
        private static void NotePeerArrived(CSteamID peer)
        {
            _peer = peer;
            Connected = true;
            SetStatus("connected to " + SteamFriends.GetFriendPersonaName(peer), NoticeKind.Good);
            Trace.Info("[steam] " + Status);

            // Both seats are taken. Steam hides a full lobby from a filtered search anyway,
            // but only the owner can say so the moment the second player arrives.
            SetJoinable(false);
            Net.SendHello();
        }

        private static void NotePeerLost()
        {
            if (_peer.IsValid()) SteamNetworking.CloseP2PSessionWithUser(_peer);
            _peer = CSteamID.Nil;
            Connected = false;
            SetStatus("opponent disconnected", NoticeKind.Bad);
            SetJoinable(true);
        }

        /// <summary>Whether anyone else may still walk into our lobby.</summary>
        private static void SetJoinable(bool joinable)
        {
            if (!IsHost || !_lobby.IsValid() || !Available) return;
            SteamMatchmaking.SetLobbyJoinable(_lobby, joinable);
            Trace.Info("[steam] lobby is now " + (joinable ? "open" : "closed") + " to new players");
        }

        private static void TryFindPeer(CSteamID me)
        {
            if (!_lobby.IsValid()) return;
            int members = SteamMatchmaking.GetNumLobbyMembers(_lobby);
            for (int i = 0; i < members; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(_lobby, i);
                if (member != me) { _peer = member; return; }
            }
        }

        /// <summary>
        /// Steam has no "peer dropped" signal on the P2P channel, so a peer whose game
        /// died left us waiting on a turn forever. Lobby membership changes are the signal.
        /// </summary>
        private static void OnLobbyChatUpdate(LobbyChatUpdate_t e)
        {
            var who = new CSteamID(e.m_ulSteamIDUserChanged);
            const uint left = (uint)(EChatMemberStateChange.k_EChatMemberStateChangeLeft
                                     | EChatMemberStateChange.k_EChatMemberStateChangeDisconnected
                                     | EChatMemberStateChange.k_EChatMemberStateChangeKicked);

            if ((e.m_rgfChatMemberStateChange & left) != 0 && who == _peer)
            {
                Trace.Warn("[steam] opponent left the lobby");
                NotePeerLost();
                return;
            }

            if ((e.m_rgfChatMemberStateChange
                 & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0
                && who != SteamUser.GetSteamID())
            {
                Trace.Info("[steam] opponent (re)joined the lobby");
                NotePeerArrived(who);
            }
        }

        private static void OnSessionRequest(P2PSessionRequest_t e)
        {
            Trace.Info("[steam] accepting P2P session from " + e.m_steamIDRemote);
            SteamNetworking.AcceptP2PSessionWithUser(e.m_steamIDRemote);
            if (!_peer.IsValid()) NotePeerArrived(e.m_steamIDRemote);
        }

        public static void Send(string msg)
        {
            if (!Connected || !_peer.IsValid())
            {
                Trace.Warn("[steam] dropped (no peer): " + msg);
                return;
            }
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            bool ok = SteamNetworking.SendP2PPacket(_peer, data, (uint)data.Length,
                                                   EP2PSend.k_EP2PSendReliable);
            if (ok) Trace.Info("[steam] -> " + msg);
            else Trace.Error("[steam] send failed: " + msg);
        }

        /// <summary>Pumped from the Unity main thread; Steam's API is not thread safe.</summary>
        public static void Poll()
        {
            if (!Active || !Available) return;

            if (!Connected && IsHost && _lobby.IsValid())
            {
                CSteamID me = SteamUser.GetSteamID();
                TryFindPeer(me);
                if (_peer.IsValid())
                {
                    Trace.Info("[steam] opponent joined");
                    NotePeerArrived(_peer);
                }
            }

            uint size;
            while (SteamNetworking.IsP2PPacketAvailable(out size))
            {
                byte[] buf = new byte[size];
                uint read;
                CSteamID from;
                if (!SteamNetworking.ReadP2PPacket(buf, size, out read, out from)) break;
                RecvBuffer.Append(Encoding.UTF8.GetString(buf, 0, (int)read));
            }

            string all = RecvBuffer.ToString();
            int nl;
            while ((nl = all.IndexOf('\n')) >= 0)
            {
                string line = all.Substring(0, nl).Trim();
                all = all.Substring(nl + 1);
                if (line.Length == 0) continue;
                Trace.Info("[steam] <- " + line);
                if (Net.CaptureResult(line)) continue;   // results bypass the inbox
                Inbox.Enqueue(line);
            }
            RecvBuffer.Length = 0;
            RecvBuffer.Append(all);
        }

        public static bool TryDequeue(out string msg)
        {
            return Inbox.TryDequeue(out msg);
        }

        /// <summary>Drops queued gameplay messages without touching the connection.</summary>
        public static int FlushInbox()
        {
            int dropped = 0;
            string ignored;
            while (Inbox.TryDequeue(out ignored)) dropped++;
            return dropped;
        }

        public static void Reset()
        {
            Searching = false;
            string ignored;
            while (Inbox.TryDequeue(out ignored)) { }
            RecvBuffer.Length = 0;
            Connected = false;
            _peer = CSteamID.Nil;
        }

        public static void Shutdown()
        {
            SetJoinable(true);   // leave nothing half-closed behind us
            if (_peer.IsValid()) SteamNetworking.CloseP2PSessionWithUser(_peer);
            if (_lobby.IsValid() && Available) SteamMatchmaking.LeaveLobby(_lobby);
            _lobby = CSteamID.Nil;
            Reset();
            Active = false;
            IsHost = false;
            Lobbies.Clear();
            SetStatus("idle");
            Trace.Info("[steam] shut down");
        }
    }
}
