namespace Assets.Scripts.Mfd
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Craft.Parts.Modifiers.Mfd;
    using ModApi.Craft.Program.Craft;

    /// <summary>
    /// Restarts an MFD's program from an empty screen. Widgets outlive the program, so without this
    /// they'd stay and each creation block would log an error.
    /// </summary>
    public static class MfdRestart
    {
        public const string ScreenWidgetName = "_Screen";

        // Private: the game only offers lookups by name, and no list of a screen's widgets.
        private static readonly FieldInfo WidgetsField = typeof(MfdScript).GetField("_widgets", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>The live widgets by name, or null if the game no longer has this field.</summary>
        public static IDictionary<string, IMfdWidget> Widgets(MfdScript mfd)
        {
            return WidgetsField?.GetValue(mfd) as IDictionary<string, IMfdWidget>;
        }

        public static string Question(FlightProgramData data)
        {
            return "Restart " + data.Part.DisplayName + "'s MFD program from the top? Its widgets are removed and its threads and variables reset. Other programs keep running.";
        }

        /// <summary>Returns false, leaving the screen as it is, if the widgets can't be read or the program doesn't load.</summary>
        public static bool Run(FlightProgramData data)
        {
            var mfd = data.Part.PartScript.GetModifier<MfdScript>();
            var widgets = mfd == null ? null : Widgets(mfd);
            if (widgets == null)
            {
                EditorLog.Error("Couldn't read the MFD's widgets: MfdScript._widgets has changed.");
                return false;
            }

            // Destroying a widget takes its children out of the dictionary too, so each name is checked again.
            foreach (var name in widgets.Keys.Where(name => name != ScreenWidgetName).ToList())
            {
                if (widgets.TryGetValue(name, out var widget))
                {
                    widget.Destroy();
                }
            }

            return ProgramSwapper.Restart(data);
        }
    }
}
