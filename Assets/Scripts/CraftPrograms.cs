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
        /// <summary>Every part with a program, without MFDs: the game reloads their programs in flight.</summary>
        public static List<FlightProgramData> All(ICraftScript craft)
        {
            return craft.Data.Assembly.Parts
                .Where(part => part.GetModifier<MfdData>() == null)
                .Select(part => part.GetModifier<FlightProgramData>())
                .Where(data => data != null && data.FlightProgramXml != null)
                .ToList();
        }

        /// <summary>The programs that can be edited now, the active pod's first.</summary>
        public static List<FlightProgramData> Editable(ICraftScript craft)
        {
            var active = craft.ActiveCommandPod?.Part?.GetModifier<FlightProgramData>();
            return All(craft)
                .Where(data => data.Script != null)
                .OrderBy(data => data == active ? 0 : 1)
                .ToList();
        }

        public static string Key(FlightProgramData data)
        {
            return Game.Instance.FlightScene.CraftNode.NodeId + "/" + data.Part.Id;
        }
    }
}
