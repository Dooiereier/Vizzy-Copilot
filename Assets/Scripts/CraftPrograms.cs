namespace Assets.Scripts
{
    using System.Collections.Generic;
    using System.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Craft.Parts.Modifiers.Mfd;
    using ModApi.Craft;

    /// <summary>Which programs a craft has, and the key that identifies one across a quickload.</summary>
    public static class CraftPrograms
    {
        /// <summary>Every part with a program, including custom MFD programs. The game reloads stock MFD programs in flight.</summary>
        public static List<FlightProgramData> All(ICraftScript craft)
        {
            return craft.Data.Assembly.Parts
                .Select(part => part.GetModifier<FlightProgramData>())
                .Where(data => data != null && data.FlightProgramXml != null && !IsStockMfdProgram(data))
                .ToList();
        }

        /// <summary>The programs that can be edited now, the active pod's first.</summary>
        public static List<FlightProgramData> Editable(ICraftScript craft)
        {
            var active = craft.ActiveCommandPod?.Part?.GetModifier<FlightProgramData>();
            return All(craft)
                .Where(data => data.Script != null && !data.HideEditButton)
                .OrderBy(data => data == active ? 0 : 1)
                .ToList();
        }

        /// <summary>The selected part's program if it can be edited, otherwise null.</summary>
        public static FlightProgramData Selected(ModApi.Flight.IFlightScene scene)
        {
            var data = scene.ViewManager.GameView.SelectedPart?.Data?.GetModifier<FlightProgramData>();
            return data != null && data.FlightProgramXml != null && data.Script != null && !data.HideEditButton
                && !IsStockMfdProgram(data) ? data : null;
        }

        public static bool IsMfd(FlightProgramData data)
        {
            return data.Part.GetModifier<MfdData>() != null;
        }

        private static bool IsStockMfdProgram(FlightProgramData data)
        {
            return IsMfd(data) && !data.SaveFlightProgram;
        }

        public static string Key(FlightProgramData data)
        {
            // Not the craft node: a part keeps its id when a separation or docking changes the node.
            return data.Part.Id.ToString();
        }
    }
}
