namespace Assets.Scripts
{
    using System;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using UnityEngine;

    /// <summary>
    /// Lives for one flight scene: reads the keys, opens the editor, and after a load puts each
    /// program's latest edit back. Uses Update, not the game loop: the editor opens while paused.
    /// </summary>
    public class FlightEditorController : MonoBehaviour
    {
        private string _restoreMessage;
        private bool _restoreChecked;
        private int _openRequestFrame = -1;

        /// <summary>Gets the controller of the current flight scene, or null outside flight.</summary>
        public static FlightEditorController Current { get; private set; }

        public EditorSession Session { get; private set; }

        private void Awake()
        {
            Current = this;
            FlightPause.Reset();
            RestoreLatestEdits();
        }

        private void Update()
        {
            if (!_restoreChecked)
            {
                RestoreLatestEdits();
            }

            if (_restoreMessage != null)
            {
                var message = _restoreMessage;
                _restoreMessage = null;
                RunGuarded("Couldn't show a message: ", () => Game.Instance.FlightScene.FlightSceneUI.ShowMessage(message));
            }

            FlightPause.Enforce();
            this.Session?.Update();

            if (_openRequestFrame >= 0 && Time.frameCount > _openRequestFrame)
            {
                _openRequestFrame = -1;
                RunGuarded("Couldn't open the editor: ", Open);
            }

            var settings = ModSettings.Instance;
            var shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if ((shift || !settings.RequireShift.Value) && UnityEngine.Input.GetKeyDown(settings.KeyCode)
                && !Game.Instance.UserInterface.IgnoreKeyboardInputs)
            {
                var alt = UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
                if (this.Session?.IsOpen == true)
                {
                    this.Session.ApplyFromShortcut();
                }
                else if (alt)
                {
                    RunGuarded("Couldn't revert: ", RevertToLoadedProgram);
                }
                else
                {
                    RunGuarded("Couldn't open the editor: ", Open);
                }
            }
        }

        private static void RunGuarded(string failure, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                EditorLog.Error(failure + e);
            }
        }

        /// <summary>From the flight menu: next frame, so closing the menu has finished un-pausing.</summary>
        public void RequestOpen()
        {
            _openRequestFrame = Time.frameCount;
        }

        private void OnDestroy()
        {
            RunGuarded("Couldn't close the picker: ", ProgramPicker.Close);
            RunGuarded("Couldn't close the editor: ", () => this.Session?.Close());
            FlightPause.Reset();
            if (Current == this)
            {
                Current = null;
            }
        }

        private void Open()
        {
            var ui = Game.Instance.UserInterface;
            if (ui.AnyDialogsOpen || ui.IsTextInputFocused || this.Session?.IsOpen == true)
            {
                return;
            }

            if (ProgramPicker.IsOpen)
            {
                ProgramPicker.Close();
                return;
            }

            var scene = Game.Instance.FlightScene;
            if (scene.TimeManager.CurrentMode.WarpMode)
            {
                scene.FlightSceneUI.ShowMessage("Leave time warp before editing a Vizzy program.");
                return;
            }

            var programs = CraftPrograms.Editable(scene.CraftNode.CraftScript);
            if (programs.Count == 0)
            {
                scene.FlightSceneUI.ShowMessage("No editable Vizzy program on this craft.");
            }
            else if (programs.Count == 1)
            {
                OpenProgram(programs[0]);
            }
            else
            {
                FlightPause.Hold();
                ProgramPicker.Show(programs, data => RunGuarded("Couldn't open the editor: ", () => OpenProgram(data)), FlightPause.Release);
            }
        }

        private void OpenProgram(FlightProgramData data)
        {
            if (data.Script == null)
            {
                FlightPause.Release();
                Game.Instance.FlightScene.FlightSceneUI.ShowMessage("The part with this Vizzy program is gone.");
            }
            else
            {
                this.Session = EditorSession.Open(data);
            }
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
                EditorLog.Error("Couldn't keep the latest edit: " + e);
            }
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
            foreach (var data in CraftPrograms.All(craft))
            {
                var key = CraftPrograms.Key(data);
                var latest = LatestEdits.ProgramToRestore(key, data.FlightProgramXml, data.ProcessXml);
                EditorLog.Debug("Load check for part " + key + ": saved threads " + (data.ProcessXml != null) + ", restoring latest edit " + (latest != null));
                if (latest == null)
                {
                    continue;
                }

                string message;
                if (data.Script?.FlightProgram != null)
                {
                    ProgramSwapper.Swap(data, latest, out message);
                }
                else
                {
                    message = ProgramSwapper.Prepare(data, latest);
                }

                _restoreMessage = message ?? "Kept your latest Vizzy edit. Alt+" + ModSettings.Instance.ShortcutText + " goes back to the quicksave's program.";
            }
        }

        private void RevertToLoadedProgram()
        {
            var scene = Game.Instance.FlightScene;
            if (Game.Instance.UserInterface.AnyDialogsOpen)
            {
                return;
            }

            if (scene.TimeManager.CurrentMode.WarpMode)
            {
                scene.FlightSceneUI.ShowMessage("Leave time warp before changing a Vizzy program.");
                return;
            }

            var reverted = 0;
            string message = null;
            foreach (var data in CraftPrograms.Editable(scene.CraftNode.CraftScript))
            {
                var key = CraftPrograms.Key(data);
                var loaded = LatestEdits.LoadedProgram(key);
                if (loaded != null)
                {
                    if (ProgramSwapper.Swap(data, new XElement(loaded), out var swapMessage))
                    {
                        LatestEdits.Forget(key);
                        reverted++;
                    }

                    message = message ?? swapMessage;
                }
            }

            scene.FlightSceneUI.ShowMessage(message
                ?? (reverted == 0 ? "No kept Vizzy edit to undo: load a quicksave first." : "Back to the quicksave's Vizzy program."));
        }
    }
}
