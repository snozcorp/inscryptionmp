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
        public const string Version = "1.1.1";

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

            var autoHost = Config.Bind("Dev", "AutoHost", false,
                "Open a direct-connect listener as soon as the game starts. Off by default: "
                + "hosting should be a deliberate act from the menu, not something every "
                + "launch does. Convenient while developing.");
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
