namespace Assets.Scripts
{
    using System;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using ModApi.Scenes.Events;
    using UnityEngine;

    public class Mod : ModApi.Mods.GameMod
    {
        private static bool _patched;

        private Mod() : base()
        {
        }

        public static Mod Instance { get; } = GetModInstance<Mod>();

        // Programs stay ordinary stock XML, so crafts and saves never need this mod.
        public override bool IsModRequiredForCraft(ModApi.Craft.CraftData craftData) => false;

        protected override void OnModInitialized()
        {
            _ = ModSettings.Instance;
            FlightEditorUi.Register();
            Game.Instance.SceneManager.SceneLoaded += OnSceneLoaded;
            TryPatch();
            Debug.Log("[VizzyCopilot] Started.");
        }

        /// <summary>
        /// Harmony comes from the Juno Harmony mod, which may load after this one, so this runs again at
        /// each scene load. Everything that touches HarmonyLib is in a method only compiled once it's there.
        /// </summary>
        private static void TryPatch()
        {
            if (_patched || !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0Harmony"))
            {
                return;
            }

            _patched = true;
            ApplyPatches();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyPatches()
        {
            try
            {
                new HarmonyLib.Harmony("VizzyCopilot").CreateClassProcessor(typeof(FunkInputPatch)).Patch();
                FunkInputPatch.Installed = true;
                Debug.Log("[VizzyCopilot] Patches: 1 applied, 0 failed.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VizzyCopilot] Patches: 0 applied, 1 failed. Funk auto-fill is off: " + e);
            }
        }

        private void OnSceneLoaded(object sender, SceneEventArgs e)
        {
            TryPatch();
            if (Game.InFlightScene)
            {
                var host = new GameObject("VizzyCopilot.Controller");
                host.AddComponent<FlightEditorController>();
                host.AddComponent<FunkAutoFill>();
                host.AddComponent<FunkCheck>();
            }
            else
            {
                LatestEdits.Clear();
                if (Game.InDesignerScene)
                {
                    new GameObject("VizzyCopilot.AutoFill").AddComponent<FunkAutoFill>();
                }
            }
        }
    }
}
