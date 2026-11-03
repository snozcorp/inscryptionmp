using System;
using System.IO;

namespace InscryptionMP
{
    /// <summary>
    /// BepInEx's disk logger buffers, which makes it useless for watching a live session from
    /// outside the game. This mirrors every message into BepInEx/mp-trace.log with an immediate
    /// flush.
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

        /// <summary>Shows the BepInEx folder, where this log and LogOutput.log live.</summary>
        public static void OpenFolder()
        {
            string dir = BepInEx.Paths.BepInExRootPath;
            if (string.IsNullOrEmpty(dir)) return;
            Info($"[trace] opening {dir}");
            try
            {
                // Explorer copes with spaces in the path; a file:// URL needs them escaped.
                if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer)
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
                else
                    UnityEngine.Application.OpenURL(new Uri(dir).AbsoluteUri);
            }
            catch (Exception e)
            {
                Warn($"[trace] could not open the log folder: {e.Message}");
            }
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
