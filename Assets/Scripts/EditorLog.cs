namespace Assets.Scripts
{
    /// <summary>
    /// Logging with the mod's prefix. Debug lines only appear with "Debug Logging" switched on;
    /// the rest is always written.
    /// </summary>
    public static class EditorLog
    {
        private const string Prefix = "[VizzyCopilot] ";

        public static void Info(string message) => UnityEngine.Debug.Log(Prefix + message);

        public static void Debug(string message)
        {
            if (ModSettings.Instance.DebugLogging.Value)
            {
                UnityEngine.Debug.Log(Prefix + message);
            }
        }

        public static void Warning(string message) => UnityEngine.Debug.LogWarning(Prefix + message);

        public static void Error(string message) => UnityEngine.Debug.LogError(Prefix + message);
    }
}
