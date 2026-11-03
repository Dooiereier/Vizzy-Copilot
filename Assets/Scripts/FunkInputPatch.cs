namespace Assets.Scripts
{
    using System;
    using HarmonyLib;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;

    /// <summary>
    /// Takes Up, Down, Enter and Esc from a text field while the auto-fill list is open for it. The field
    /// reads queued key events itself, so a mod can't otherwise see them first. Every other field and
    /// every other moment runs stock.
    /// </summary>
    [HarmonyPatch(typeof(TMP_InputField), nameof(TMP_InputField.OnUpdateSelected))]
    public static class FunkInputPatch
    {
        private static readonly Event Dropped = new Event();

        /// <summary>Gets or sets a value indicating whether the patch is in place: auto-fill is off without it.</summary>
        public static bool Installed { get; set; }

        private static bool Prefix(TMP_InputField __instance, BaseEventData eventData)
        {
            try
            {
                var autoFill = FunkAutoFill.Current;
                if (autoFill == null || !autoFill.TakeKeys(__instance))
                {
                    return true;
                }

                while (Event.PopEvent(Dropped))
                {
                }

                eventData.Use();
                return false;
            }
            catch (Exception e)
            {
                EditorLog.Error("Funk auto-fill key handling failed: " + e);
                return true;
            }
        }
    }
}
