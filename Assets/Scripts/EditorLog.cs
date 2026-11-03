namespace Assets.Scripts
{
    /// <summary>Logging with the mod's prefix.</summary>
    public static class EditorLog
    {
        private const string Prefix = "[VizzyFlightEditor] ";

        public static void Info(string message) => UnityEngine.Debug.Log(Prefix + message);

        public static void Debug(string message) => UnityEngine.Debug.Log(Prefix + message);

        public static void Warning(string message) => UnityEngine.Debug.LogWarning(Prefix + message);

        public static void Error(string message) => UnityEngine.Debug.LogError(Prefix + message);
    }
}
