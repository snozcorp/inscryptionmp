using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace InscryptionMP
{
    /// <summary>
    /// Dead-simple newline-delimited TCP transport. Turn-based game: latency is irrelevant,
    /// ordering is guaranteed, TCP is correct.
    /// </summary>
    public static class Net
    {
        public const int DefaultPort = 27333;

        private static TcpListener _listener;
        private static TcpClient _client;
        private static StreamWriter _writer;
        private static Thread _thread;
        private static volatile bool _running;

        private static readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();

        private static bool TcpIsHost { get; set; }

        public static bool IsHost => UseSteam ? SteamTransport.IsHost : TcpIsHost;
        /// <summary>TCP-specific connection state. Prefer <see cref="Connected"/>.</summary>
        public static bool TcpConnected { get; private set; }

        /// <summary>True while a host listener or join attempt is alive.</summary>
        public static bool Running => _running || SteamTransport.Active;

        /// <summary>True when either transport has a live peer.</summary>
        public static bool Connected => SteamTransport.Connected || TcpConnected;

        /// <summary>Which transport messages should go through.</summary>
        private static bool UseSteam => SteamTransport.Connected
                                        || (SteamTransport.Active && !TcpConnected);

        /// <summary>Tears down TCP without touching Steam. Used when switching transport.</summary>
        internal static void ShutdownTcp()
        {
            _running = false;
            TcpConnected = false;
            try { _writer?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }
            _writer = null; _client = null; _listener = null;
            while (Inbox.TryDequeue(out _)) { }
        }

        public static void Host(int port = DefaultPort)
        {
            if (SteamTransport.Active) { Trace.Info("[net] leaving Steam lobby to host directly"); SteamTransport.Shutdown(); }
            if (_running)
            {
                Trace.Info($"[net] already {(Connected ? "connected" : "hosting")} - ignoring");
                return;
            }
            Shutdown();
            TcpIsHost = true;
            _running = true;
            Notice.Busy($"Waiting for someone to connect on port {port}...");
            _thread = new Thread(() => HostLoop(port)) { IsBackground = true, Name = "InscryptionMP-Host" };
            _thread.Start();
            Trace.Info($"[net] hosting on port {port}, waiting for peer...");
        }

        public static void Join(string host, int port = DefaultPort)
        {
            if (SteamTransport.Active) { Trace.Info("[net] leaving Steam lobby to join directly"); SteamTransport.Shutdown(); }
            if (_running)
            {
                Trace.Info($"[net] already {(Connected ? "connected" : "connecting")} - ignoring");
                return;
            }
            Shutdown();
            TcpIsHost = false;
            _running = true;
            _thread = new Thread(() => JoinLoop(host, port)) { IsBackground = true, Name = "InscryptionMP-Client" };
            _thread.Start();
            Notice.Busy($"Connecting to {host}:{port}...");
            Trace.Info($"[net] connecting to {host}:{port}...");
        }

        private static void HostLoop(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();

                // Keep listening after a drop so a peer can come back mid-match.
                while (_running)
                {
                    _client = _listener.AcceptTcpClient();
                    Notice.Good("Opponent connected.");
                    Trace.Info("[net] peer connected.");
                    Pump();
                    TcpConnected = false;
                    if (!_running) break;
                    Trace.Warn("[net] peer dropped - listening again");
                }
            }
            catch (Exception e) { if (_running) Trace.Error($"[net] host error: {e.Message}"); }
            finally { TcpConnected = false; _running = false; }
        }

        private static void JoinLoop(string host, int port)
        {
            try
            {
                // Retry so a client can rejoin a host that is still waiting.
                while (_running)
                {
                    try
                    {
                        _client = new TcpClient();
                        _client.Connect(host, port);
                        Notice.Good("Connected.");
                        Trace.Info("[net] connected to host.");
                        Pump();
                        TcpConnected = false;
                    }
                    catch (Exception e)
                    {
                        if (!_running) break;

                        // Only worth saying when the player is waiting on a connection.
                        if (!Reconnecting)
                            Notice.Bad($"Couldn't reach {host}:{port} - {e.Message}");

                        Trace.Warn($"[net] connect failed ({e.Message}) - retrying");
                    }

                    if (!_running || !Reconnecting) break;
                    Thread.Sleep(2000);
                }
            }
            catch (Exception e) { if (_running) Trace.Error($"[net] join error: {e.Message}"); }
            finally { TcpConnected = false; _running = false; }
        }

        private static void Pump()
        {
            var stream = _client.GetStream();
            _writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
            TcpConnected = true;
            SendHello();   // TCP never greeted the peer at all
            using (var reader = new StreamReader(stream))
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    Trace.Info($"[net] <- {line}");
                    if (CaptureResult(line)) continue;
                    Inbox.Enqueue(line);
                }
            }
        }

        public static void Send(string msg)
        {
            if (UseSteam) { SteamTransport.Send(msg); return; }
            if (!TcpConnected || _writer == null) { Trace.Warn($"[net] dropped (not connected): {msg}"); return; }
            try { _writer.WriteLine(msg); Trace.Info($"[net] -> {msg}"); }
            catch (Exception e) { Trace.Error($"[net] send failed: {e.Message}"); }
        }

        /// <summary>Match results bypass the inbox.</summary>
        public static bool? PendingResult { get; set; }

        /// <summary>Returns true if the message was a result and has been captured.</summary>
        /// <summary>Set when a peer greets us with an incompatible protocol version.</summary>
        public static string HandshakeError { get; set; }

        /// <summary>True once the peer has greeted us with a compatible version.</summary>
        public static bool PeerVerified { get; private set; }

        /// <summary>
        /// The protocol the peer greeted us with. Everything we send is written to the
        /// lower of this and ours, so a newer build never speaks over an older one.
        /// </summary>
        public static int PeerProtocol { get; private set; } = Protocol.Version;

        /// <summary>Whether the peer understands the richer protocol 3 messages.</summary>
        public static bool PeerSpeaksV3 => PeerProtocol >= 3;

        /// <summary>Set when the peer asks us to start a match.</summary>
        public static MatchAct? PendingStartRequest { get; set; }

        /// <summary>
        /// Sniper aims the peer has sent, keyed by the slot their card sits in.
        /// </summary>
        private static readonly ConcurrentDictionary<int, int[]> Aims = new ConcurrentDictionary<int, int[]>();

        public static bool TryTakeAim(int attackerSlot, out int[] targets)
        {
            return Aims.TryRemove(attackerSlot, out targets);
        }

        /// <summary>Drops aims nobody consumed.</summary>
        public static void ClearAims()
        {
            int stale = Aims.Count;
            Aims.Clear();
            if (stale > 0) Trace.Info($"[sniper] discarded {stale} unused aim(s)");

            ClearLatches();
        }

        /// <summary>
        /// Latch targets the peer has sent. A queue because two latchers can die in one combat
        /// and both clients resolve them in the same order.
        /// </summary>
        private static readonly ConcurrentQueue<string> Latches = new ConcurrentQueue<string>();

        public static bool TryTakeLatch(out bool sendersOwnSide, out int slotIndex)
        {
            sendersOwnSide = false;
            slotIndex = -1;

            string raw;
            if (!Latches.TryDequeue(out raw)) return false;
            return Protocol.TryParseLatch(raw, out sendersOwnSide, out slotIndex);
        }

        /// <summary>Drops choices nobody consumed, so the next latcher can't inherit one.</summary>
        public static void ClearLatches()
        {
            int stale = Latches.Count;
            string ignored;
            while (Latches.TryDequeue(out ignored)) { }
            if (stale > 0) Trace.Info($"[latch] discarded {stale} unused target(s)");
        }

        /// <summary>Returns true if the message was handled out-of-band.</summary>
        public static bool CaptureResult(string line)
        {
            if (Protocol.TryParseAim(line, out int aimSlot, out int[] aimTargets))
            {
                Aims[aimSlot] = aimTargets;
                return true;
            }

            if (Protocol.TryParseLatch(line, out bool _, out int _))
            {
                Latches.Enqueue(line);
                return true;
            }

            if (Protocol.TryParseStart(line, out MatchAct startAct))
            {
                PendingStartRequest = startAct;
                return true;
            }

            if (line == Protocol.Won)  { PendingResult = false; return true; }   // peer won, so we lost
            if (line == Protocol.Lost) { PendingResult = true;  return true; }

            if (Protocol.TryParseHello(line, out int proto, out string modVersion))
            {
                PeerProtocol = proto;

                if (proto < Protocol.MinCompatible)
                {
                    HandshakeError =
                        "their build is too old - you have mod " + Plugin.Version +
                        " (protocol " + Protocol.Version + "), they have " + modVersion +
                        " (protocol " + proto + ")";
                    Trace.Error("[net] " + HandshakeError);
                    Notice.Bad("Opponent's mod is too old to play against.");
                    PeerVerified = false;
                }
                else
                {
                    PeerVerified = true;
                    HandshakeError = null;

                    if (proto < Protocol.Version)
                    {
                        // Play their dialect rather than refusing them. Everything the
                        // newer protocol added is an extra, not a requirement.
                        Trace.Info($"[net] peer speaks protocol {proto}; dropping to it");
                        Notice.Say($"Opponent is on mod {modVersion} - playing without the newer extras.");
                    }
                    else
                    {
                        Trace.Info("[net] peer verified - mod " + modVersion + ", protocol " + proto);
                    }
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// While true, a dropped TCP client keeps retrying instead of giving up. Set for
        /// the duration of a match so a crash or brief network blip isn't an instant loss.
        /// </summary>
        public static bool Reconnecting { get; set; }

        /// <summary>Greets the peer. Called by whichever transport just connected.</summary>
        public static void SendHello()
        {
            HandshakeError = null;
            PeerVerified = false;
            Send(Protocol.Hello);
        }

        /// <summary>
        /// Drops gameplay messages left over from a previous match, keeping the connection.
        /// </summary>
        public static void FlushInbox()
        {
            int dropped = 0;
            while (Inbox.TryDequeue(out _)) dropped++;
            dropped += SteamTransport.FlushInbox();
            ClearAims();

            if (dropped > 0) Trace.Warn($"[net] dropped {dropped} stale message(s) from the last match");
        }

        public static bool TryDequeue(out string msg)
        {
            if (UseSteam) return SteamTransport.TryDequeue(out msg);
            return Inbox.TryDequeue(out msg);
        }

        public static string StatusLine
        {
            get
            {
                if (UseSteam) return "steam: " + SteamTransport.Status;
                if (TcpConnected) return TcpIsHost ? "connected (host)" : "connected (client)";
                if (_running)     return TcpIsHost ? "hosting - waiting for peer" : "connecting...";
                return "offline";
            }
        }

        public static void Shutdown()
        {
            if (SteamTransport.Active) SteamTransport.Shutdown();
            _running = false;
            TcpConnected = false;
            try { _writer?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }
            while (Inbox.TryDequeue(out _)) { }
            PendingResult = null;
            PendingStartRequest = null;
            HandshakeError = null;
            PeerVerified = false;
            PeerProtocol = Protocol.Version;
            _writer = null; _client = null; _listener = null;
        }
    }
}
