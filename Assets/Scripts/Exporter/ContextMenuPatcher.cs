using Assets.Scripts.Vizzy.UI.Elements;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.Exporter
{
    /// <summary>
    /// Hooks clicks on Vizzy blocks to remember which one Export Block should use.
    ///
    /// This patches manually at runtime instead of via [HarmonyPatch] attributes, because
    /// we're not 100% sure which class actually declares OnPointerClick for a plain
    /// instruction/base block - only that VariableElementScript does (joaofarias's Vizzy Studio mod's own
    /// VariableElementScriptPatches.cs patches it, for its rename-on-click feature). An
    /// attribute-based patch throws at startup if its target method doesn't exist; doing
    /// it in code lets us try several candidate classes and just skip the ones that don't
    /// declare their own OnPointerClick, so a wrong guess can't crash the whole mod.
    /// </summary>
    internal static class ContextMenuPatcher
    {
        private const float MaxClickMovement = 5f;

        public static void Apply(Harmony harmony)
        {
            var hooked = 0;
            // Confirmed: joaofarias's Vizzy Studio mod patches this exact method on this exact class.
            hooked += TryPatchOnPointerClick(harmony, typeof(VariableElementScript));

            // Best-effort guesses for where a right-click on a statement/base block lands.
            // Whichever of these actually declares its own OnPointerClick gets hooked; the
            // DeclaringType check in TryPatchOnPointerClick skips inherited duplicates so we
            // never end up patching the same underlying method twice.
            hooked += TryPatchOnPointerClick(harmony, typeof(InstructionElementScript));
            hooked += TryPatchOnPointerClick(harmony, typeof(BlockElementScript));
            if (hooked == 0)
            {
                throw new InvalidOperationException("No block click handler could be hooked.");
            }
        }

        private static int TryPatchOnPointerClick(Harmony harmony, Type targetType)
        {
            try
            {
                MethodInfo method = AccessTools.Method(targetType, "OnPointerClick", new[] { typeof(PointerEventData) });
                if (method == null)
                {
                    Debug.Log($"[VizzyStudio] {targetType.Name} has no OnPointerClick - skipping.");
                    return 0;
                }

                if (method.DeclaringType != targetType)
                {
                    // Inherited from a type we've already tried (or will try) - patching it
                    // again here would fire our postfix twice per click.
                    return 0;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ContextMenuPatcher), nameof(OnPointerClickPostfix)));
                Debug.Log($"[VizzyStudio] Hooked block clicks via {targetType.Name}.OnPointerClick.");
                return 1;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VizzyStudio] Couldn't hook block clicks on {targetType.Name}: {ex.Message}");
                return 0;
            }
        }

        private static void OnPointerClickPostfix(object __instance, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left
                || (eventData.position - eventData.pressPosition).magnitude > MaxClickMovement)
                return;

            if (__instance is BlockElementScript block && ModSettings.Instance.ExportMenu.Value)
            {
                ExportBlock.Select(block);
            }
        }
    }
}
