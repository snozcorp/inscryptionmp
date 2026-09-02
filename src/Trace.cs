using System;
using System.IO;

namespace InscryptionMP
{
    /// <summary>
    /// BepInEx's disk logger buffers, which makes it useless for watching a live
    /// session from outside the game. This mirrors every message into
    /// BepInEx/mp-trace.log with an immediate flush.
    /// </summary>
    internal static class Trace
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath ?? ".", "");
                _path = Path.Combine(dir, "mp-trace.log");
                File.AppendAllText(_path, $"--- InscryptionMP trace pid={System.Diagnostics.Process.GetCurrentProcess().Id} {DateTime.Now:HH:mm:ss} ---{Environment.NewLine}");
            }
            catch { _path = null; }
        }

        public static void Info(string msg)  { Plugin.Log.LogInfo(msg);    Write("INFO ", msg); }
        public static void Warn(string msg)  { Plugin.Log.LogWarning(msg); Write("WARN ", msg); }
        public static void Error(string msg) { Plugin.Log.LogError(msg);   Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            if (_path == null) return;
            try
            {
                lock (Gate)
                {
                    using (var w = new StreamWriter(_path, append: true))
                    {
                        w.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{System.Diagnostics.Process.GetCurrentProcess().Id}] {level} {msg}");
                        w.Flush();
                    }
                }
            }
            catch { /* never let logging kill the game */ }
        }
    }
}
