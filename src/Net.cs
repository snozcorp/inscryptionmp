using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace InscryptionMP
{
    /// <summary>
    /// Dead-simple newline-delimited TCP transport.
    /// Turn-based game: latency is irrelevant, ordering is guaranteed, TCP is correct.
    /// All socket work happens off the Unity main thread; messages are drained via TryDequeue.
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

        public static bool IsHost { get; private set; }
        public static bool Connected { get; private set; }

        /// <summary>True while a host listener or join attempt is alive.</summary>
        public static bool Running => _running;

        public static void Host(int port = DefaultPort)
        {
            if (_running)
            {
                Trace.Info($"[net] already {(Connected ? "connected" : "hosting")} - ignoring");
                return;
            }
            Shutdown();
            IsHost = true;
            _running = true;
            _thread = new Thread(() => HostLoop(port)) { IsBackground = true, Name = "InscryptionMP-Host" };
            _thread.Start();
            Trace.Info($"[net] hosting on port {port}, waiting for peer...");
        }

        public static void Join(string host, int port = DefaultPort)
        {
            if (_running)
            {
                Trace.Info($"[net] already {(Connected ? "connected" : "connecting")} - ignoring");
                return;
            }
            Shutdown();
            IsHost = false;
            _running = true;
            _thread = new Thread(() => JoinLoop(host, port)) { IsBackground = true, Name = "InscryptionMP-Client" };
            _thread.Start();
            Trace.Info($"[net] connecting to {host}:{port}...");
        }

        private static void HostLoop(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                _client = _listener.AcceptTcpClient();
                Trace.Info("[net] peer connected.");
                Pump();
            }
            catch (Exception e) { if (_running) Trace.Error($"[net] host error: {e.Message}"); }
            finally { Connected = false; _running = false; }
        }

        private static void JoinLoop(string host, int port)
        {
            try
            {
                _client = new TcpClient();
                _client.Connect(host, port);
                Trace.Info("[net] connected to host.");
                Pump();
            }
            catch (Exception e) { if (_running) Trace.Error($"[net] join error: {e.Message}"); }
            finally { Connected = false; _running = false; }
        }

        private static void Pump()
        {
            var stream = _client.GetStream();
            _writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
            Connected = true;
            using (var reader = new StreamReader(stream))
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    Trace.Info($"[net] <- {line}");
                    Inbox.Enqueue(line);
                }
            }
        }

        public static void Send(string msg)
        {
            if (!Connected || _writer == null) { Trace.Warn($"[net] dropped (not connected): {msg}"); return; }
            try { _writer.WriteLine(msg); Trace.Info($"[net] -> {msg}"); }
            catch (Exception e) { Trace.Error($"[net] send failed: {e.Message}"); }
        }

        public static bool TryDequeue(out string msg) => Inbox.TryDequeue(out msg);

        public static string StatusLine
        {
            get
            {
                if (Connected) return IsHost ? "connected (host)" : "connected (client)";
                if (_running)  return IsHost ? "hosting :27333 - waiting for peer" : "connecting...";
                return "offline  [F9] host  [F10] join localhost";
            }
        }

        public static void Shutdown()
        {
            _running = false;
            Connected = false;
            try { _writer?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            try { _listener?.Stop(); } catch { }
            while (Inbox.TryDequeue(out _)) { }
            _writer = null; _client = null; _listener = null;
        }
    }
}
