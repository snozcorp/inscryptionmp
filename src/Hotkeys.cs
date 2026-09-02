using UnityEngine;

namespace InscryptionMP
{
    /// <summary>Temporary dev harness: F9 host, F10 join localhost, F11 status.</summary>
    internal class Hotkeys : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))  Net.Host();
            if (Input.GetKeyDown(KeyCode.F10)) Net.Join("127.0.0.1");
            if (Input.GetKeyDown(KeyCode.F11))
                Plugin.Log.LogInfo($"[status] connected={Net.Connected} isHost={Net.IsHost}");
        }
    }
}
