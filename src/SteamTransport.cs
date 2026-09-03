using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Steamworks;

namespace InscryptionMP
{
    /// <summary>
    /// Steam lobby + P2P transport.
    ///
    /// Inscryption compiles Steamworks.NET into its own assembly and its SteamManager
    /// already calls SteamAPI.RunCallbacks() every frame, so this needs no bundled
    /// dependencies and no initialisation of its own. Players get friend invites, NAT
    /// traversal and a lobby list without anyone running a master server.
    /// </summary>
    public static class SteamTransport
    {
        private const string LobbyKey = "inscryption_mp";
        private const string LobbyValue = "1";
        private const string LobbyHostKey = "host_name";

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
        /// When the pending search started. Steam should always answer, but if it ever
        /// doesn't, a stuck flag would disable Find Games for the rest of the session -
        /// so a stale search is allowed to be replaced.
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
            Net.ShutdownTcp();   // one transport at a time
            if (!Available) { Trace.Warn("[steam] not initialised"); return; }
            EnsureCallbacks();
            Reset();
            Active = true;
            IsHost = true;
            Status = "creating lobby...";
            Trace.Info("[steam] creating lobby");
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 2);
        }

        public static void RefreshLobbies()
        {
            if (!Available) { Status = "Steam not initialised"; Trace.Warn("[steam] not initialised"); return; }
            EnsureCallbacks();

            // One CallResult at a time: setting a new handle abandons the pending one, so
            // repeated presses used to cancel the search each time and look like nothing
            // was happening at all.
            if (Searching && Environment.TickCount - _searchStartedAt < SearchTimeoutMs)
            {
                Trace.Info("[steam] already searching");
                return;
            }

            Lobbies.Clear();
            Searching = true;
            _searchStartedAt = Environment.TickCount;
            Status = "searching...";
            Trace.Info("[steam] requesting lobby list");
            SteamMatchmaking.AddRequestLobbyListStringFilter(
                LobbyKey, LobbyValue, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
            _crList.Set(call);
        }

        public static void JoinLobby(CSteamID lobby)
        {
            Net.ShutdownTcp();   // one transport at a time
            if (!Available) return;
            EnsureCallbacks();
            Reset();
            Active = true;
            IsHost = false;
            Status = "joining...";
            Trace.Info("[steam] joining lobby " + lobby);
            SteamMatchmaking.JoinLobby(lobby);
        }

        private static void OnLobbyCreated(LobbyCreated_t e)
        {
            if (e.m_eResult != EResult.k_EResultOK)
            {
                Status = "lobby failed (" + e.m_eResult + ")";
                Trace.Error("[steam] lobby creation failed: " + e.m_eResult);
                Active = false;
                return;
            }

            _lobby = new CSteamID(e.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(_lobby, LobbyKey, LobbyValue);
            SteamMatchmaking.SetLobbyData(_lobby, LobbyHostKey, SteamFriends.GetPersonaName());
            Status = "waiting for opponent";
            Trace.Info("[steam] lobby created - waiting for opponent");
        }

        private static void OnLobbyList(LobbyMatchList_t e, bool failed)
        {
            Searching = false;
            Lobbies.Clear();
            if (failed) { Status = "search failed - try again"; return; }

            CSteamID me = SteamUser.GetSteamID();
            for (int i = 0; i < e.m_nLobbiesMatching; i++)
            {
                CSteamID id = SteamMatchmaking.GetLobbyByIndex(i);

                // Your own lobby comes back in the search results. Joining it does
                // nothing, so don't offer it.
                if (SteamMatchmaking.GetLobbyOwner(id) == me) continue;

                string name = SteamMatchmaking.GetLobbyData(id, LobbyHostKey);
                if (string.IsNullOrEmpty(name)) name = id.ToString();
                Lobbies.Add(new KeyValuePair<CSteamID, string>(id, name));
            }
            // Say plainly that a search happened and came back empty. Drawing nothing at
            // all is indistinguishable from the button not working.
            Status = Lobbies.Count == 0
                ? "no games found - someone has to Host Lobby first"
                : Lobbies.Count + (Lobbies.Count == 1 ? " game found" : " games found");
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

            if (_peer.IsValid())
            {
                Connected = true;
                Status = "connected to " + SteamFriends.GetFriendPersonaName(_peer);
                Trace.Info("[steam] " + Status);
                Net.SendHello();
            }
            else
            {
                Status = "waiting for opponent";
                Trace.Info("[steam] in lobby, waiting for opponent");
            }
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
                SteamNetworking.CloseP2PSessionWithUser(_peer);
                _peer = CSteamID.Nil;
                Connected = false;
                Status = "opponent disconnected";
                return;
            }

            if ((e.m_rgfChatMemberStateChange
                 & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0
                && who != SteamUser.GetSteamID())
            {
                Trace.Info("[steam] opponent (re)joined the lobby");
                _peer = who;
                Connected = true;
                Status = "connected to " + SteamFriends.GetFriendPersonaName(_peer);
                Net.SendHello();
            }
        }

        private static void OnSessionRequest(P2PSessionRequest_t e)
        {
            Trace.Info("[steam] accepting P2P session from " + e.m_steamIDRemote);
            SteamNetworking.AcceptP2PSessionWithUser(e.m_steamIDRemote);
            if (!_peer.IsValid())
            {
                _peer = e.m_steamIDRemote;
                Connected = true;
                Status = "connected to " + SteamFriends.GetFriendPersonaName(_peer);
            }
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
                    Connected = true;
                    Status = "connected to " + SteamFriends.GetFriendPersonaName(_peer);
                    Trace.Info("[steam] opponent joined");
                    Net.SendHello();
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
            if (_peer.IsValid()) SteamNetworking.CloseP2PSessionWithUser(_peer);
            if (_lobby.IsValid() && Available) SteamMatchmaking.LeaveLobby(_lobby);
            _lobby = CSteamID.Nil;
            Reset();
            Active = false;
            IsHost = false;
            Status = "idle";
            Trace.Info("[steam] shut down");
        }
    }
}
