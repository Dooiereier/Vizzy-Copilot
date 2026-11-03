namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using Assets.Scripts.Vizzy;
    using ModApi.Scenes.Events;
    using UnityEngine;

    public class Mod : ModApi.Mods.GameMod
    {
        private static bool _started;
        private static string _startupProblem;

        private Mod() : base()
        {
        }

        public static Mod Instance { get; } = GetModInstance<Mod>();

        /// <summary>Only a craft with a Matrix block needs the mod: every other program is plain stock XML.</summary>
        public override bool IsModRequiredForCraft(ModApi.Craft.CraftData craftData) => MatrixBlocks.IsUsedBy(craftData);

        public override void OnModLoaded()
        {
            base.OnModLoaded();
            _ = ModSettings.Instance;
            Game.Instance.SceneManager.SceneLoaded += OnSceneLoaded;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToList();
            if (!assemblies.Contains("0Harmony"))
            {
                _startupProblem = "Vizzy Studio needs the Juno Harmony mod, so it is switched off. Install and enable Juno Harmony, then restart the game.";
                Debug.LogError("[VizzyStudio] Juno Harmony isn't loaded, so the mod is off.");
                return;
            }

            if (assemblies.Contains("VizzyDebugger"))
            {
                _startupProblem = "Vizzy Studio includes Vizzy Debugger's features. Remove the Vizzy Debugger mod, or both will act on the same fields.";
                Debug.LogWarning("[VizzyStudio] Vizzy Debugger is also loaded.");
            }

            StartFeatures();
            _started = true;
        }

        /// <summary>Only compiled once Harmony is known to be there. Each patch alone, so one failing leaves the others.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void StartFeatures()
        {
            FlightEditorUi.Register();
            FlightButtons.Register();

            var harmony = new HarmonyLib.Harmony("VizzyStudio");
            var applied = 0;
            var failed = 0;
            var patches = new List<Type> { typeof(FunkInputPatch), typeof(FunkHighlightRegisterPatch) };
            try
            {
                // Before the toolbox patch: a block in the toolbox has to be able to save.
                MatrixBlocks.Register();
                patches.Add(typeof(MatrixToolboxPatch));
            }
            catch (Exception e)
            {
                failed++;
                Debug.LogWarning("[VizzyStudio] Matrix blocks are off: " + e);
            }

            foreach (var patch in patches)
            {
                try
                {
                    RuntimeHelpers.RunClassConstructor(patch.TypeHandle);
                    harmony.CreateClassProcessor(patch).Patch();
                    applied++;
                    if (patch == typeof(FunkInputPatch))
                    {
                        FunkInputPatch.Installed = true;
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogWarning("[VizzyStudio] Patch " + patch.Name + " failed: " + e);
                }
            }

            Debug.Log("[VizzyStudio] Patches: " + applied + " applied, " + failed + " failed.");
        }

        private static void OnSceneLoaded(object sender, SceneEventArgs e)
        {
            if (_startupProblem != null && Game.Instance.UserInterface != null)
            {
                var problem = _startupProblem;
                _startupProblem = null;
                var dialog = Game.Instance.UserInterface.CreateMessageDialog(ModApi.Ui.MessageDialogType.Okay, null, true);
                dialog.MessageText = problem;
            }

            if (!_started)
            {
                return;
            }

            FunkHighlight.ClearScene();
            if (Game.InFlightScene)
            {
                var host = new GameObject("VizzyStudio.Controller");
                host.AddComponent<FlightEditorController>();
                host.AddComponent<FunkAutoFill>();
                host.AddComponent<FunkCheck>();
                FlightButtons.Connect();
            }
            else
            {
                LatestEdits.Clear();
                if (Game.InDesignerScene)
                {
                    new GameObject("VizzyStudio.AutoFill").AddComponent<FunkAutoFill>();
                }
            }
        }
    }
}
