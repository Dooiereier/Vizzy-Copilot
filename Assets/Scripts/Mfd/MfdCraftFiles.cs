namespace Assets.Scripts.Mfd
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;

    /// <summary>
    /// Makes a saved MFD change permanent in the flying craft's files: the working copy the designer and
    /// a relaunch read, and the design saved under the craft's name. All or none, each backed up first.
    /// </summary>
    public static class MfdCraftFiles
    {
        /// <summary>Returns what happened, for the player. Nothing is written if a file disagrees with the running program.</summary>
        public static string Write(FlightProgramData data, IList<MfdChange> changes, IList<MfdLine> expected, double screenWidth, double screenHeight, bool allowShared)
        {
            var designs = Game.Instance.CraftDesigns;
            var craftName = data.Part.PartScript.CraftScript.Data.Name;
            var programName = (string)data.FlightProgramXml.Attribute("name");
            var partId = data.Part.Id;
            var ids = new[] { CraftDesigns.EditorCraftId, craftName }.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();

            var updates = new List<(string Id, XElement Craft)>();
            foreach (var id in ids)
            {
                if (!designs.HasCraft(id) || CraftDesigns.IsStock(id))
                {
                    continue;
                }

                // A copy: the game keeps the working copy's XML in memory.
                var craft = new XElement(designs.GetCraftDesign(id));

                // The working copy may hold another craft, when this flight didn't start from the designer.
                if ((string)craft.Attribute("name") != craftName)
                {
                    EditorLog.Debug("MFD save: " + id + " holds another craft.");
                    continue;
                }

                var program = FindProgram(craft, programName, partId);
                if (program == null)
                {
                    continue;
                }

                var lines = MfdFormulaWriter.Apply(program, changes, screenWidth, screenHeight, allowShared);
                if (!Same(lines, expected))
                {
                    return "Saved for this flight only: the craft file " + id + " has a different program, for example after a change in the designer or Edit Vizzy. No file was changed.";
                }

                updates.Add((id, craft));
            }

            if (updates.Count == 0)
            {
                return "Saved for this flight only: no craft file with this program was found.";
            }

            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                foreach (var update in updates)
                {
                    var file = designs.GetCraftFile(update.Id);
                    File.Copy(file.FullName, file.FullName + ".bak-" + stamp, true);
                }

                foreach (var update in updates)
                {
                    designs.SaveCraft(update.Id, update.Craft);
                }
            }
            catch (Exception e)
            {
                EditorLog.Error("Couldn't write the craft files: " + e);
                return "Saved for this flight, but writing the craft files failed: see Player.log.";
            }

            EditorLog.Debug("MFD save wrote: " + string.Join(", ", updates.Select(update => update.Id)));
            return updates.Any(update => update.Id == craftName)
                ? "Saved to the running program and the craft " + craftName + ", with a backup."
                : "Saved to the running program and the designer's working copy, with a backup. This craft has no saved design: save it in the designer to keep it.";
        }

        /// <summary>By name; the part's id only settles it when several parts have that program.</summary>
        private static XElement FindProgram(XElement craft, string name, int partId)
        {
            var programs = craft.Descendants("Program").Where(item => (string)item.Attribute("name") == name).ToList();
            if (programs.Count <= 1)
            {
                return programs.FirstOrDefault();
            }

            return programs.FirstOrDefault(item => (string)item.Ancestors("Part").FirstOrDefault()?.Attribute("id") == partId.ToString());
        }

        private static bool Same(IList<MfdLine> a, IList<MfdLine> b)
        {
            return a.Count == b.Count && a.Zip(b, (x, y) => x.Applied == y.Applied && x.Detail == y.Detail).All(same => same);
        }
    }
}
