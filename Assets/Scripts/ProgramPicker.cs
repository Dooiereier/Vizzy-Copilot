namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using ModApi.Ui.Inspector;

    /// <summary>The choice between a craft's programs, in the game's own inspector panel.</summary>
    public static class ProgramPicker
    {
        private static IInspectorPanel _panel;

        public static bool IsOpen => _panel != null;

        public static void Show(IList<FlightProgramData> programs, Action<FlightProgramData> onPicked)
        {
            Close();

            var model = new InspectorModel("VizzyFlightEditorPicker", "Edit Vizzy");
            foreach (var data in programs)
            {
                model.Add(new TextButtonModel(Label(data), _ =>
                {
                    Close();
                    onPicked(data);
                }));
            }

            var info = new InspectorPanelCreationInfo
            {
                StartPosition = InspectorPanelCreationInfo.InspectorStartPosition.Center,
                Resizable = false,
            };
            _panel = Game.Instance.UserInterface.CreateInspectorPanel(model, info);
            _panel.Closed += OnPanelClosed;
        }

        public static void Close()
        {
            var panel = _panel;
            _panel = null;
            panel?.Close();
        }

        private static string Label(FlightProgramData data)
        {
            var program = (string)data.FlightProgramXml.Attribute("name");
            return string.IsNullOrEmpty(program) ? data.Part.DisplayName : data.Part.DisplayName + " – " + program;
        }

        private static void OnPanelClosed(object sender)
        {
            _panel = null;
        }
    }
}
