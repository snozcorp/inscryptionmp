namespace InscryptionMP
{
    /// <summary>
    /// The real Plugin is a BepInEx MonoBehaviour, and the wire format only wants its
    /// version string. The version here is checked against src/Plugin.cs by the tests, so
    /// it cannot quietly drift.
    /// </summary>
    internal static class Plugin
    {
        public const string Version = "1.5.0";
    }
}
