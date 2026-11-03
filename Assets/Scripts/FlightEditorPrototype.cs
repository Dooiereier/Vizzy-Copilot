namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Craft.Parts.Modifiers.Mfd;
    using Assets.Scripts.Vizzy.UI;
    using UnityEngine;

    /// <summary>
    /// Throwaway test of milestone 0: Shift+O opens the stock Vizzy editor for the active
    /// pod's program. Apply never restarts the program: ThreadRemapper carries the running threads
    /// over to the edit. After a quickload the latest edit goes back in (LatestEdits), and
    /// Alt+Shift+O returns to the quicksave's program. Shift+O in the editor applies and closes, and
    /// the canvas reopens where it was. Everything worth knowing goes to Player.log.
    /// </summary>
    public class FlightEditorPrototype : MonoBehaviour
    {
        private const string LogPrefix = "[VizzyFlightEditor] ";

        // Survives a quickload's scene reload, so the canvas stays put through the tuning loop.
        private static readonly Dictionary<string, EditorView> Views = new Dictionary<string, EditorView>();

        private VizzyUIScript _editor;
        private FlightProgramData _data;
        private bool _wasPaused;
        private bool _wasUiVisible;
        private string _closingMessage;
        private string _restoreMessage;
        private bool _restoreChecked;
        private bool _restoreViewPending;
        private ProgramContainerScript _viewContainer;

        private void Awake()
        {
            RestoreLatestEdits();
        }

        private void RestoreLatestEdits()
        {
            try
            {
                TryRestoreLatestEdits();
            }
            catch (Exception e)
            {
                _restoreChecked = true;
                Debug.LogError(LogPrefix + "Couldn't keep the latest edit: " + e);
            }
        }

        private void Update()
        {
            if (!_restoreChecked)
            {
                RestoreLatestEdits();
            }

            if (_restoreMessage != null)
            {
                Game.Instance.FlightScene.FlightSceneUI.ShowMessage(_restoreMessage);
                _restoreMessage = null;
            }

            if (_restoreViewPending)
            {
                RestoreView();
            }

            var shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (shift && UnityEngine.Input.GetKeyDown(KeyCode.O) && _editor != null)
            {
                ApplyAndClose();
            }
            else if (shift && UnityEngine.Input.GetKeyDown(KeyCode.O))
            {
                var alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
                try
                {
                    if (alt)
                    {
                        RevertToLoadedProgram();
                    }
                    else
                    {
                        Open();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(LogPrefix + (alt ? "Couldn't revert: " : "Couldn't open the editor: ") + e);
                }
            }
        }

        /// <summary>The shortcut's second use: same as Apply, unless the O is being typed somewhere.</summary>
        private void ApplyAndClose()
        {
            var ui = Game.Instance.UserInterface;
            if (ui.AnyDialogsOpen || ui.IsTextInputFocused)
            {
                return;
            }

            try
            {
                _editor.ApplyChangesToCraft();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "Couldn't apply with the shortcut: " + e);
            }

            _editor.Close();
        }

        /// <summary>Puts the canvas back where it was last time for this part, once its UI exists.</summary>
        private void RestoreView()
        {
            _viewContainer = FindObjectOfType<ProgramContainerScript>();
            if (_editor == null || _viewContainer == null || _viewContainer.VizzyUI == null)
            {
                return;
            }

            _restoreViewPending = false;
            if (Views.TryGetValue(PartKey(_data), out var view))
            {
                // Zoom first: it moves the canvas too.
                _viewContainer.Zoom = view.Zoom;
                _editor.ProgramTransform.localPosition = view.Position;
            }
        }

        private void OnDestroy()
        {
            if (_editor != null)
            {
                Debug.Log(LogPrefix + "Scene ending with the editor open: closing it.");
                _editor.Close();
            }
        }

        private void Open()
        {
            var ui = Game.Instance.UserInterface;
            if (ui.AnyDialogsOpen || ui.IsTextInputFocused)
            {
                return;
            }

            var scene = Game.Instance.FlightScene;
            if (scene.TimeManager.CurrentMode.WarpMode)
            {
                scene.FlightSceneUI.ShowMessage("Leave time warp before editing a Vizzy program.");
                return;
            }

            _data = FindProgram(scene.CraftNode.CraftScript);
            if (_data == null)
            {
                scene.FlightSceneUI.ShowMessage("No editable Vizzy program on this craft.");
                return;
            }

            _wasPaused = scene.TimeManager.Paused;
            scene.TimeManager.RequestPauseChange(true, false);
            _wasUiVisible = scene.FlightSceneUI.Visible;
            scene.FlightSceneUI.Visible = false;

            _editor = Game.Instance.ResourceLoader.InstantiatePrefab<VizzyUIScript>("Ui/Vizzy/Vizzy");
            _editor.Initialize(ui.Transform, _data.FlightProgramXml, false, OnApply);
            _editor.Closed += OnEditorClosed;
            _restoreViewPending = true;
            Debug.Log(LogPrefix + "Editor open. Paused before: " + _wasPaused + ", paused now: " + scene.TimeManager.Paused);
        }

        private static List<FlightProgramData> AllPrograms(ModApi.Craft.ICraftScript craft)
        {
            return craft.Data.Assembly.Parts
                .Where(part => part.GetModifier<MfdData>() == null)
                .Select(part => part.GetModifier<FlightProgramData>())
                .Where(data => data != null && data.FlightProgramXml != null)
                .ToList();
        }

        private static FlightProgramData FindProgram(ModApi.Craft.ICraftScript craft)
        {
            var programs = AllPrograms(craft).Where(data => data.Script != null).ToList();
            Debug.Log(LogPrefix + "Programs on this craft: " + programs.Count);

            var active = craft.ActiveCommandPod?.Part?.GetModifier<FlightProgramData>();
            return programs.Contains(active) ? active : programs.FirstOrDefault();
        }

        private void OnApply(XElement newProgram)
        {
            try
            {
                var before = _data.FlightProgramXml;
                if (SwapProgram(_data, newProgram, out _closingMessage))
                {
                    LatestEdits.Remember(PartKey(_data), before, newProgram);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "Apply failed: " + e);
                _closingMessage = "Couldn't apply the Vizzy edit: see Player.log.";
            }
        }

        /// <summary>Puts a program in place on a running part, carrying its threads over.</summary>
        private static bool SwapProgram(FlightProgramData data, XElement newProgram, out string message)
        {
            message = null;
            var script = data.Script;
            var oldProgram = data.FlightProgramXml;
            script.UpdateXml();
            var oldProcess = data.ProcessXml == null ? null : new XElement(data.ProcessXml);
            LogThreads("Before", oldProcess, oldProgram);

            // Remap before changing anything, so a failure here leaves the program running as it was.
            var remapped = oldProcess == null ? null : ThreadRemapper.Remap(oldProgram, newProgram, oldProcess);
            message = LogRemap(remapped);

            data.FlightProgramXml = newProgram;
            data.ProcessXml = remapped == null ? null : new XElement(remapped.Process);
            script.StartProgram();

            if (script.FlightProgram == null)
            {
                Debug.LogError(LogPrefix + "The new program didn't load: restoring the old one.");
                message = "The edited Vizzy program didn't load: the old one is still running.";
                data.FlightProgramXml = oldProgram;
                data.ProcessXml = oldProcess;
                script.StartProgram();
                return false;
            }

            // Serializing straight after restoring shows whether every thread came back as remapped.
            script.UpdateXml();
            LogThreads("After", data.ProcessXml, newProgram);
            if (remapped != null)
            {
                Debug.Log(LogPrefix + "Resume check: running state " + (XNode.DeepEquals(remapped.Process, data.ProcessXml) ? "as expected" : "DIFFERENT"));
            }

            return true;
        }

        private static string LogRemap(ThreadRemapper.Result remapped)
        {
            if (remapped == null)
            {
                return null;
            }

            foreach (var note in remapped.Notes)
            {
                Debug.Log(LogPrefix + note);
            }

            return remapped.ThreadsEnded > 0 ? remapped.ThreadsEnded + " Vizzy thread(s) ended: their blocks were deleted." : null;
        }

        /// <summary>After a load: the quicksave brought its own program, so put the latest edit back.</summary>
        private void TryRestoreLatestEdits()
        {
            var craft = Game.Instance.FlightScene?.CraftNode?.CraftScript;
            if (craft == null || craft.Data?.Assembly == null)
            {
                return;
            }

            _restoreChecked = true;
            foreach (var data in AllPrograms(craft))
            {
                var key = PartKey(data);
                var latest = LatestEdits.ProgramToRestore(key, data.FlightProgramXml, data.ProcessXml);
                Debug.Log(LogPrefix + "Load check for part " + key + ": saved threads " + (data.ProcessXml != null) + ", restoring latest edit " + (latest != null));
                if (latest == null)
                {
                    continue;
                }

                string message;
                if (data.Script?.FlightProgram != null)
                {
                    Debug.Log(LogPrefix + "Keeping the latest edit: program already running.");
                    SwapProgram(data, latest, out message);
                }
                else
                {
                    // Not started yet: the game starts it from these with no frame on the old program.
                    Debug.Log(LogPrefix + "Keeping the latest edit: program not started yet.");
                    var remapped = ThreadRemapper.Remap(data.FlightProgramXml, latest, data.ProcessXml);
                    message = LogRemap(remapped);
                    data.FlightProgramXml = latest;
                    data.ProcessXml = new XElement(remapped.Process);
                }

                _restoreMessage = message ?? "Kept your latest Vizzy edit. Alt+Shift+O goes back to the quicksave's program.";
            }
        }

        private void RevertToLoadedProgram()
        {
            var scene = Game.Instance.FlightScene;
            var data = FindProgram(scene.CraftNode.CraftScript);
            var loaded = data == null ? null : LatestEdits.LoadedProgram(PartKey(data));
            if (loaded == null)
            {
                scene.FlightSceneUI.ShowMessage("No kept Vizzy edit to undo: load a quicksave first.");
                return;
            }

            SwapProgram(data, new XElement(loaded), out var message);
            LatestEdits.Forget(PartKey(data));
            scene.FlightSceneUI.ShowMessage(message ?? "Back to the quicksave's Vizzy program.");
        }

        private static string PartKey(FlightProgramData data)
        {
            return Game.Instance.FlightScene.CraftNode.NodeId + "/" + data.Part.Id;
        }

        private static Dictionary<int, string> BlocksById(XElement program)
        {
            var blocks = new Dictionary<int, string>();
            foreach (var element in program.Descendants())
            {
                var id = element.Attribute("id");
                if (id != null && int.TryParse(id.Value, out var value))
                {
                    blocks[value] = element.Name.LocalName;
                }
            }

            return blocks;
        }

        private static void LogThreads(string label, XElement process, XElement program)
        {
            var blocks = BlocksById(program);
            var threads = process?.Elements("Thread").ToList() ?? new List<XElement>();
            Debug.Log(LogPrefix + label + ": " + threads.Count + " threads, " + (process?.Element("Variables")?.Elements().Count() ?? 0) + " variables.");
            foreach (var thread in threads)
            {
                var next = (string)thread.Attribute("nextInstruction");
                var name = next != null && int.TryParse(next, out var id) && blocks.TryGetValue(id, out var block) ? block : "?";
                Debug.Log(LogPrefix + "  thread at " + next + " (" + name + "), stack frames: " + (thread.Element("CallStack")?.Elements().Count() ?? 0));
            }
        }

        private void OnEditorClosed(object sender, EventArgs e)
        {
            // Still alive here: Close only destroys the editor at the end of the frame.
            if (!_restoreViewPending && _viewContainer != null && _editor.ProgramTransform != null)
            {
                Views[PartKey(_data)] = new EditorView { Position = _editor.ProgramTransform.localPosition, Zoom = _viewContainer.Zoom };
            }

            _restoreViewPending = false;
            _viewContainer = null;
            var scene = Game.Instance.FlightScene;
            if (scene != null)
            {
                scene.FlightSceneUI.Visible = _wasUiVisible;
                if (!_wasPaused)
                {
                    scene.TimeManager.RequestPauseChange(false, false);
                }

                if (_closingMessage != null)
                {
                    scene.FlightSceneUI.ShowMessage(_closingMessage);
                }
            }

            _editor = null;
            _data = null;
            _closingMessage = null;
            Debug.Log(LogPrefix + "Editor closed.");
        }

        private struct EditorView
        {
            public Vector3 Position;
            public float Zoom;
        }
    }
}
