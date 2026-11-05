namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using Assets.Scripts.Vizzy.Craft;
    using HarmonyLib;

    /// <summary>
    /// Debug only: names the Funk text the game fails to parse. Its own "Error running flight program"
    /// says only which kind of block failed. Leaves the error as it was, and logs each text once.
    /// </summary>
    [HarmonyPatch(typeof(CraftService), nameof(CraftService.GetInputExpression))]
    public static class FunkErrorLogPatch
    {
        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static Exception Finalizer(Exception __exception, string text)
        {
            try
            {
                if (__exception != null && ModSettings.Instance.DebugLogging.Value && Logged.Add(text ?? string.Empty))
                {
                    EditorLog.Debug("Funk text that failed (" + __exception.GetType().Name + "): '" + text + "'");
                }
            }
            catch (Exception)
            {
                // Logging must never change the game's own error.
            }

            return __exception;
        }
    }
}
