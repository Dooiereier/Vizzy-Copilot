namespace Assets.Scripts
{
    using System;
    using System.Linq;
    using System.Xml.Linq;
    using UnityEngine;

    /// <summary>
    /// Lives for one flight scene: reads the keys, opens the editor, and after a load puts each
    /// program's latest edit back. Uses Update, not the game loop: the editor opens while paused.
    /// </summary>
    public class FlightEditorController : MonoBehaviour
    {
        private EditorSession _session;
        private string _restoreMessage;
        private bool _restoreChecked;

        private void Awake()
        {
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
                Game.Instance.FlightScene.FlightSceneUI.ShowMessage(_restoreMessage);
                _restoreMessage = null;
            }

            _session?.Update();

            var shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (shift && UnityEngine.Input.GetKeyDown(KeyCode.O) && _session?.IsOpen == true)
            {
                _session.ApplyAndClose();
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
                    EditorLog.Error((alt ? "Couldn't revert: " : "Couldn't open the editor: ") + e);
                }
            }
        }

        private void OnDestroy()
        {
            _session?.Close();
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

            var data = CraftPrograms.Editable(scene.CraftNode.CraftScript).FirstOrDefault();
            if (data == null)
            {
                scene.FlightSceneUI.ShowMessage("No editable Vizzy program on this craft.");
                return;
            }

            _session = EditorSession.Open(data);
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

                _restoreMessage = message ?? "Kept your latest Vizzy edit. Alt+Shift+O goes back to the quicksave's program.";
            }
        }

        private void RevertToLoadedProgram()
        {
            var scene = Game.Instance.FlightScene;
            var reverted = 0;
            string message = null;
            foreach (var data in CraftPrograms.Editable(scene.CraftNode.CraftScript))
            {
                var key = CraftPrograms.Key(data);
                var loaded = LatestEdits.LoadedProgram(key);
                if (loaded != null)
                {
                    ProgramSwapper.Swap(data, new XElement(loaded), out var swapMessage);
                    LatestEdits.Forget(key);
                    message = message ?? swapMessage;
                    reverted++;
                }
            }

            scene.FlightSceneUI.ShowMessage(reverted == 0
                ? "No kept Vizzy edit to undo: load a quicksave first."
                : message ?? "Back to the quicksave's Vizzy program.");
        }
    }
}
