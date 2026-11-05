namespace Assets.Scripts
{
    using System;
    using System.Linq;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Mfd;
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

        private MfdEditorSession _mfdSession;

        private void Awake()
        {
            Current = this;
            FlightPause.Reset();
            Game.Instance.SceneManager.SceneTransitionStarted += this.OnSceneTransitionStarted;
            RestoreLatestEdits();
        }

        private void Update()
        {
            FlightButtons.ConnectIfNeeded();
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
            RunGuarded("Couldn't handle the picker's keys: ", ProgramPicker.HandleKeys);
            this.Session?.Update();
            RunGuarded("Couldn't update the MFD panel: ", () => _mfdSession?.Update());

            if (_openRequestFrame >= 0 && Time.frameCount > _openRequestFrame)
            {
                _openRequestFrame = -1;
                RunGuarded("Couldn't open the editor: ", Open);
            }

            var settings = ModSettings.Instance;
            var shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            if (shift && UnityEngine.Input.GetKeyDown(settings.KeyCode)
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

        /// <summary>While the flight still exists: by OnDestroy the panel and the widgets are being torn down.</summary>
        private void OnSceneTransitionStarted(object sender, ModApi.Scenes.Events.SceneTransitionEventArgs e)
        {
            RunGuarded("Couldn't close the MFD panel: ", () => _mfdSession?.Close());
        }

        private void OnDestroy()
        {
            Game.Instance.SceneManager.SceneTransitionStarted -= this.OnSceneTransitionStarted;
            RunGuarded("Couldn't close the picker: ", ProgramPicker.Close);
            RunGuarded("Couldn't close the editor: ", () => this.Session?.Close());
            RunGuarded("Couldn't close the MFD panel: ", () => _mfdSession?.Close());
            FlightPause.Reset();
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>The Edit Vizzy button: the selected part's program, otherwise the same as the shortcut.</summary>
        public void EditSelected()
        {
            RunGuarded("Couldn't open the editor: ", () =>
            {
                var scene = Game.Instance.FlightScene;
                var selected = CraftPrograms.Selected(scene);
                if (selected == null)
                {
                    Open();
                }
                else if (CanStart())
                {
                    ProgramPicker.Close();
                    FlightPause.Hold();
                    OpenProgram(selected);
                }
            });
        }

        /// <summary>The Arrange MFD button: the selected part's MFD, otherwise the craft's only one or a choice of them.</summary>
        public void ArrangeMfd()
        {
            RunGuarded("Couldn't open the MFD panel: ", () =>
            {
                ProgramPicker.Close();
                if (!CanStart())
                {
                    return;
                }

                var scene = Game.Instance.FlightScene;
                var selected = CraftPrograms.Selected(scene);
                if (selected != null && CraftPrograms.IsMfd(selected))
                {
                    this.OpenMfd(selected);
                    return;
                }

                var programs = CraftPrograms.Editable(scene.CraftNode.CraftScript).Where(CraftPrograms.IsMfd).ToList();
                if (programs.Count == 0)
                {
                    scene.FlightSceneUI.ShowMessage("No custom MFD program on this craft.");
                }
                else if (programs.Count == 1)
                {
                    this.OpenMfd(programs[0]);
                }
                else
                {
                    FlightPause.Hold();
                    ProgramPicker.Show(programs, data => RunGuarded("Couldn't open the MFD panel: ", () => this.OpenMfd(data)), FlightPause.Release);
                }
            });
        }

        /// <summary>The picker holds the flight; a panel that doesn't open must let it go again.</summary>
        private void OpenMfd(FlightProgramData data)
        {
            _mfdSession = new MfdEditorSession(data);
            try
            {
                _mfdSession.Open();
            }
            finally
            {
                if (!_mfdSession.IsOpen)
                {
                    FlightPause.Release();
                }
            }
        }

        /// <summary>The Restart Vizzy button: the selected part's program, otherwise a choice of them, MFDs included. Always asks first.</summary>
        public void RestartSelected()
        {
            RunGuarded("Couldn't restart the program: ", () =>
            {
                ProgramPicker.Close();
                if (!CanStart())
                {
                    return;
                }

                var scene = Game.Instance.FlightScene;
                var selected = CraftPrograms.Selected(scene);
                if (selected != null)
                {
                    AskToRestart(selected);
                    return;
                }

                var programs = CraftPrograms.Editable(scene.CraftNode.CraftScript);
                if (programs.Count == 0)
                {
                    scene.FlightSceneUI.ShowMessage("No Vizzy program to restart on this craft.");
                }
                else if (programs.Count == 1)
                {
                    AskToRestart(programs[0]);
                }
                else
                {
                    FlightPause.Hold();
                    ProgramPicker.Show(programs, data => RunGuarded("Couldn't restart the program: ", () => AskToRestart(data)), FlightPause.Release, _lastOpenedKey);
                }
            });
        }

        private void AskToRestart(FlightProgramData data)
        {
            var scene = Game.Instance.FlightScene;

            if (data.Script == null)
            {
                FlightPause.Release();
                scene.FlightSceneUI.ShowMessage("The part with this Vizzy program is gone.");
                return;
            }

            FlightPause.Hold();
            var dialog = Game.Instance.UserInterface.CreateMessageDialog(ModApi.Ui.MessageDialogType.OkayCancel, null, true);
            dialog.UseDangerButtonStyle = true;
            dialog.MessageText = CraftPrograms.IsMfd(data)
                ? MfdRestart.Question(data)
                : "Restart " + data.Part.DisplayName + "'s Vizzy program from the top? Its threads and variables reset. Other programs keep running.";
            dialog.Closed += _ => FlightPause.Release();
            dialog.OkayClicked += _ =>
            {
                dialog.Close();
                RunGuarded("Couldn't restart the program: ", () =>
                {
                    if (!(CraftPrograms.IsMfd(data) ? MfdRestart.Run(data) : ProgramSwapper.Restart(data)))
                    {
                        scene.FlightSceneUI.ShowMessage("The Vizzy program didn't load: see Player.log.");
                    }
                });
            };
        }

        /// <summary>No dialog in the way, no editor open, no warp.</summary>
        private bool CanStart()
        {
            var ui = Game.Instance.UserInterface;
            if (ui.AnyDialogsOpen || ui.IsTextInputFocused || this.Session?.IsOpen == true || _mfdSession?.IsOpen == true)
            {
                return false;
            }

            var scene = Game.Instance.FlightScene;
            if (scene.TimeManager.CurrentMode.WarpMode)
            {
                scene.FlightSceneUI.ShowMessage("Leave time warp before changing a Vizzy program.");
                return false;
            }

            return true;
        }

        private void Open()
        {
            if (ProgramPicker.IsOpen)
            {
                ProgramPicker.Close();
                return;
            }

            if (!CanStart())
            {
                return;
            }

            var scene = Game.Instance.FlightScene;
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
                ProgramPicker.Show(programs, data => RunGuarded("Couldn't open the editor: ", () => OpenProgram(data)), FlightPause.Release, _lastOpenedKey);
            }
        }

        // Survives a quickload, for the tuning loop: load, open the same program, fly.
        private static string _lastOpenedKey;

        private void OpenProgram(FlightProgramData data)
        {
            if (data.Script == null)
            {
                FlightPause.Release();
                Game.Instance.FlightScene.FlightSceneUI.ShowMessage("The part with this Vizzy program is gone.");
            }
            else
            {
                _lastOpenedKey = CraftPrograms.Key(data);
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
