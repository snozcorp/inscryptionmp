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
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Trace.Init();
            Log.LogInfo("=====================================");
            Log.LogInfo($"{Name} v{Version} loaded.");
            Log.LogInfo($"Unity: {Application.unityVersion}");
            Log.LogInfo("=====================================");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Harmony patches applied.");

            gameObject.AddComponent<Hotkeys>();
            Log.LogInfo("Hotkeys: F9=host, F10=join localhost, F11=status");

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
