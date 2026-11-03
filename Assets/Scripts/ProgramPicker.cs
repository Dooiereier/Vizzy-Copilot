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
        private const int MaxScreenHeightPercent = 90;

        private static IInspectorPanel _panel;
        private static Action _onDismissed;

        public static bool IsOpen => _panel != null;

        /// <summary>Calls onDismissed instead of onPicked if the panel closes without a choice.</summary>
        public static void Show(IList<FlightProgramData> programs, Action<FlightProgramData> onPicked, Action onDismissed)
        {
            Close();
            _onDismissed = onDismissed;

            var model = new InspectorModel("VizzyStudioPicker", "Edit Vizzy");
            foreach (var data in programs)
            {
                model.Add(new TextButtonModel(Label(data), _ =>
                {
                    _onDismissed = null;
                    Close();
                    onPicked(data);
                }));
            }

            var info = new InspectorPanelCreationInfo
            {
                StartPosition = InspectorPanelCreationInfo.InspectorStartPosition.Center,
                Resizable = false,

                // Wide enough for the longest label on one line, and as tall as the list needs.
                PanelWidth = Mathf.Max(MinWidth, (programs.Max(data => Label(data).Length) * WidthPerCharacter) + LabelMargin),
                PanelMaxHeight = MaxScreenHeightPercent,
            };
            _panel = Game.Instance.UserInterface.CreateInspectorPanel(model, info);
            _panel.Closed += OnPanelClosed;
        }

        public static void Close()
        {
            var panel = _panel;
            _panel = null;
            panel?.Close();
            Dismissed();
        }

        private static string Label(FlightProgramData data)
        {
            var program = (string)data.FlightProgramXml.Attribute("name");
            return string.IsNullOrEmpty(program) ? data.Part.DisplayName : data.Part.DisplayName + " – " + program;
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
