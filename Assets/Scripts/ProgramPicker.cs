namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using ModApi.Ui.Inspector;
    using UnityEngine;

    /// <summary>The choice between a craft's programs, in the game's own inspector panel.</summary>
    public static class ProgramPicker
    {
        private const int MinWidth = 300;
        private const int WidthPerCharacter = 12;
        private const int LabelMargin = 70;
        private const float MaxScreenHeight = 0.9f;

        private static IInspectorPanel _panel;
        private static Action _onDismissed;
        private static readonly List<TextButtonModel> Buttons = new List<TextButtonModel>();
        private static readonly List<Action> Picks = new List<Action>();
        private static readonly InspectorRowPaint RowPaint = new InspectorRowPaint();
        private static int _selected = -1;

        public static bool IsOpen => _panel != null;

        /// <summary>
        /// Calls onDismissed instead of onPicked if the panel closes without a choice. The program with
        /// the selected key starts out blue, for Enter to open.
        /// </summary>
        public static void Show(IList<FlightProgramData> programs, Action<FlightProgramData> onPicked, Action onDismissed, string selectedKey = null)
        {
            Close();
            _onDismissed = onDismissed;
            Buttons.Clear();
            RowPaint.Clear();
            Picks.Clear();

            var model = new InspectorModel("VizzyStudioPicker", Text.Get("VizzyStudio.Picker.Title"));
            foreach (var data in programs)
            {
                Action pick = () =>
                {
                    _onDismissed = null;
                    Close();
                    onPicked(data);
                };
                Picks.Add(pick);
                Buttons.Add(model.Add(new TextButtonModel(Label(data), _ => pick())));
            }

            var info = new InspectorPanelCreationInfo
            {
                StartPosition = InspectorPanelCreationInfo.InspectorStartPosition.Center,
                Resizable = false,

                // Wide enough for the longest label on one line, and as tall as the list needs.
                PanelWidth = Mathf.Max(MinWidth, (programs.Max(data => Label(data).Length) * WidthPerCharacter) + LabelMargin),
                PanelMaxHeight = MaxScreenHeight,
            };
            _panel = Game.Instance.UserInterface.CreateInspectorPanel(model, info);
            _panel.Closed += OnPanelClosed;

            _selected = -1;
            for (var i = 0; i < programs.Count; i++)
            {
                if (CraftPrograms.Key(programs[i]) == selectedKey)
                {
                    Select(i);
                }
            }
        }

        /// <summary>Each frame: Up and Down move the selection, Enter opens the selected program.</summary>
        public static void HandleKeys()
        {
            if (_panel == null)
            {
                return;
            }

            // The panel can build its rows after it opens, and building can reset their colors.
            if (_selected >= 0)
            {
                RowPaint.Paint(Buttons[_selected], true);
            }

            if (Game.Instance.UserInterface.IsTextInputFocused)
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.DownArrow))
            {
                Select((_selected + 1) % Buttons.Count);
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.UpArrow))
            {
                Select(_selected <= 0 ? Buttons.Count - 1 : _selected - 1);
            }
            else if (_selected >= 0 && (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                Picks[_selected]();
            }
        }

        public static void Close()
        {
            var panel = _panel;
            _panel = null;
            panel?.Close();
            Dismissed();
        }

        private static void Select(int index)
        {
            if (_selected >= 0)
            {
                RowPaint.Paint(Buttons[_selected], false);
            }

            RowPaint.Paint(Buttons[index], true);
            _selected = index;
        }

        private static string Label(FlightProgramData data)
        {
            return data.Part.DisplayName;
        }

        private static void OnPanelClosed(object sender)
        {
            _panel = null;
            Dismissed();
        }

        private static void Dismissed()
        {
            var onDismissed = _onDismissed;
            _onDismissed = null;
            onDismissed?.Invoke();
        }
    }
}
