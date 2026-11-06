namespace Assets.Scripts.Mfd
{
    using System.Collections.Generic;
    using System.Linq;
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

        /// <summary>
        /// Every live widget but the screen: the canvas's own children, then theirs, as the game's own
        /// "destroy all widgets" block finds them. A child that isn't a widget, such as the outline, is null.
        /// </summary>
        public static List<IMfdWidget> Widgets(MfdScript mfd)
        {
            var found = new List<IMfdWidget>();
            var seen = new HashSet<string>();
            var next = new Queue<IMfdWidget>(mfd.GetMfdChildWidgets(null).Where(item => item != null));
            while (next.Count > 0)
            {
                var widget = next.Dequeue();
                if (widget.Transform == null || !seen.Add(widget.Name))
                {
                    continue;
                }

                found.Add(widget);
                foreach (var child in mfd.GetMfdChildWidgets(widget.Name).Where(item => item != null))
                {
                    next.Enqueue(child);
                }
            }

            return found;
        }

        public static string Question(FlightProgramData data)
        {
            return Text.Get("VizzyStudio.Mfd.RestartQuestion", data.Part.DisplayName);
        }

        /// <summary>Returns false if the part has no MFD or the program doesn't load.</summary>
        public static bool Run(FlightProgramData data)
        {
            var mfd = data.Part.PartScript.GetModifier<MfdScript>();
            if (mfd == null)
            {
                return false;
            }

            // As the game's own "destroy all widgets" block: the screen's Destroy does nothing, and a widget's takes its children.
            foreach (var widget in mfd.GetMfdChildWidgets(null).Where(item => item != null).ToList())
            {
                widget.Destroy();
            }

            return ProgramSwapper.Restart(data);
        }
    }
}
