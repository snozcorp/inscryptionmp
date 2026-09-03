using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace InscryptionMP
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dev.snoz.inscryptionmp";
        public const string Name = "InscryptionMP";
        public const string Version = "1.0.2";

        internal static ManualLogSource Log;

        /// <summary>Persistent DontDestroyOnLoad host for coroutines that must survive scene loads.</summary>
        internal static MonoBehaviour Runner;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Runner = this;
            Trace.Init();

            // Unity swallows coroutine exceptions into its own log, which BepInEx buffers.
            // Route them into the flushed trace so a hang is diagnosable from outside.
            Application.logMessageReceived += (condition, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error)
                    Trace.Error($"[unity] {type}: {condition} || {stack}");
            };
            Log.LogInfo("=====================================");
            Log.LogInfo($"{Name} v{Version} loaded.");
            Log.LogInfo($"Unity: {Application.unityVersion}");
            Log.LogInfo("=====================================");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Harmony patches applied.");

            gameObject.AddComponent<MpMenu>();
            Log.LogInfo("Press F7 for the multiplayer menu.  F8 = start match, F12 = abort.");

            var experimental = Config.Bind("Dev", "ExperimentalActs", false,
                "Expose Act 2 and Act 3 matches. These are being brought up and are not "
                + "expected to work yet.");
            ActInfo.Experimental = experimental.Value;
            if (ActInfo.Experimental) Trace.Info("[boot] experimental acts enabled");

            var autoHost = Config.Bind("Dev", "AutoHost", true,
                "Start hosting automatically on launch. Convenient while iterating.");
            if (autoHost.Value)
            {
                Trace.Info("[boot] AutoHost enabled - hosting immediately.");
                Net.Host();
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
