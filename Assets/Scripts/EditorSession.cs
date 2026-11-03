namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Vizzy.UI;
    using UnityEngine;

    /// <summary>One open editor: pauses the game, hides the flight UI, puts the canvas back and closes.</summary>
    public sealed class EditorSession
    {
        // Survives a quickload's scene reload, so the canvas stays put through the tuning loop.
        private static readonly Dictionary<string, EditorView> Views = new Dictionary<string, EditorView>();

        private readonly FlightProgramData _data;
        private readonly string _key;
        private VizzyUIScript _editor;
        private bool _wasPaused;
        private bool _wasUiVisible;
        private string _closingMessage;
        private bool _restoreViewPending;
        private ProgramContainerScript _viewContainer;

        private EditorSession(FlightProgramData data)
        {
            _data = data;
            _key = CraftPrograms.Key(data);
        }

        public bool IsOpen => _editor != null;

        public static EditorSession Open(FlightProgramData data)
        {
            var session = new EditorSession(data);
            session.Open();
            return session;
        }

        /// <summary>Puts the canvas back where it was last time for this part, once its UI exists.</summary>
        public void Update()
        {
            if (!_restoreViewPending || !IsOpen)
            {
                return;
            }

            _viewContainer = UnityEngine.Object.FindObjectOfType<ProgramContainerScript>();
            if (_viewContainer == null || _viewContainer.VizzyUI == null)
            {
                return;
            }

            _restoreViewPending = false;
            if (Views.TryGetValue(_key, out var view))
            {
                // Zoom first: it moves the canvas too.
                _viewContainer.Zoom = view.Zoom;
                _editor.ProgramTransform.localPosition = view.Position;
            }
        }

        /// <summary>The same as the editor's Apply, unless a key was being typed somewhere.</summary>
        public void ApplyAndClose()
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
                EditorLog.Error("Couldn't apply with the shortcut: " + e);
            }

            _editor.Close();
        }

        public void Close()
        {
            if (IsOpen)
            {
                _editor.Close();
            }
        }

        private void Open()
        {
            var ui = Game.Instance.UserInterface;
            var scene = Game.Instance.FlightScene;
            _wasPaused = scene.TimeManager.Paused;
            scene.TimeManager.RequestPauseChange(true, false);
            _wasUiVisible = scene.FlightSceneUI.Visible;
            scene.FlightSceneUI.Visible = false;

            _editor = Game.Instance.ResourceLoader.InstantiatePrefab<VizzyUIScript>("Ui/Vizzy/Vizzy");
            _editor.Initialize(ui.Transform, _data.FlightProgramXml, false, OnApply);
            _editor.Closed += OnEditorClosed;
            _restoreViewPending = true;
            EditorLog.Debug("Editor open for part " + _key + ".");
        }

        private void OnApply(XElement newProgram)
        {
            try
            {
                var before = _data.FlightProgramXml;
                if (ProgramSwapper.Swap(_data, newProgram, out _closingMessage))
                {
                    LatestEdits.Remember(_key, before, newProgram);
                }
            }
            catch (Exception e)
            {
                EditorLog.Error("Apply failed: " + e);
                _closingMessage = "Couldn't apply the Vizzy edit: see Player.log.";
            }
        }

        private void OnEditorClosed(object sender, EventArgs e)
        {
            // Still alive here: Close only destroys the editor at the end of the frame.
            if (!_restoreViewPending && _viewContainer != null && _editor.ProgramTransform != null)
            {
                Views[_key] = new EditorView { Position = _editor.ProgramTransform.localPosition, Zoom = _viewContainer.Zoom };
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
            EditorLog.Debug("Editor closed.");
        }

        private struct EditorView
        {
            public Vector3 Position;
            public float Zoom;
        }
    }
}
