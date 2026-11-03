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
        private static bool _checked;
        private static bool _started;
        private static bool _debuggerLoaded;
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
        }

        /// <summary>
        /// At the first scene: a mod's OnModLoaded runs as soon as that mod loads, and Juno Harmony and
        /// Vizzy Debugger may only load later.
        /// </summary>
        private static void CheckAndStart()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToList();
            if (!assemblies.Contains("0Harmony"))
            {
                _startupProblem = "Vizzy Studio needs the Juno Harmony mod, so it is switched off. Install and enable Juno Harmony, then restart the game.";
                Debug.LogError("[VizzyStudio] Juno Harmony isn't loaded, so the mod is off.");
                return;
            }

            _debuggerLoaded = assemblies.Contains("VizzyDebugger");
            if (_debuggerLoaded)
            {
                _startupProblem = "Vizzy Studio includes Vizzy Debugger's features. Remove the Vizzy Debugger mod, or both will act on the same fields.";
                Debug.LogWarning("[VizzyStudio] Vizzy Debugger is also loaded.");
            }

            try
            {
                StartFeatures();
                _started = true;
            }
            catch (Exception e)
            {
                _startupProblem = "Vizzy Studio couldn't start with the Harmony version that's loaded, so it is switched off. See Player.log.";
                Debug.LogError("[VizzyStudio] Couldn't start: " + e);
            }
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
                if (!_debuggerLoaded)
                {
                    MatrixBlocks.Register();
                    patches.Add(typeof(MatrixToolboxPatch));
                }
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
            if (!_checked)
            {
                _checked = true;
                CheckAndStart();
            }

            Guarded("show the startup message", ShowStartupProblem);
            if (!_started)
            {
                return;
            }

            // Each step alone: a failure in one must not cost the others, least of all the editor.
            if (Game.InFlightScene)
            {
                Guarded("set up the flight editor", () =>
                {
                    var host = new GameObject("VizzyStudio.Controller");
                    host.AddComponent<FlightEditorController>();
                    host.AddComponent<FunkAutoFill>();
                    if (ModSettings.Instance.RunFunkCheck.Value)
                    {
                        host.AddComponent<FunkCheck>();
                    }
                });
            }
            else
            {
                Guarded("clear the kept edits", LatestEdits.Clear);
                if (Game.InDesignerScene)
                {
                    Guarded("set up auto-fill", () => new GameObject("VizzyStudio.AutoFill").AddComponent<FunkAutoFill>());
                }
            }

            Guarded("clear the coloring's fields", FunkHighlight.ClearScene);
        }

        private static void ShowStartupProblem()
        {
            if (_startupProblem != null && Game.Instance.UserInterface != null)
            {
                var problem = _startupProblem;
                _startupProblem = null;
                var dialog = Game.Instance.UserInterface.CreateMessageDialog(ModApi.Ui.MessageDialogType.Okay, null, true);
                dialog.MessageText = problem;
            }
        }

        private static void Guarded(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError("[VizzyStudio] Couldn't " + what + ": " + e);
            }
        }
    }
}
