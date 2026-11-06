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
            var designWithoutProgram = false;
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
                    designWithoutProgram |= id == craftName;
                    continue;
                }

                var lines = MfdFormulaWriter.Apply(program, changes, screenWidth, screenHeight, allowShared);
                if (!Same(lines, expected))
                {
                    return Text.Get("VizzyStudio.MfdSave.FileDiffers", id);
                }

                updates.Add((id, craft));
            }

            if (updates.Count == 0)
            {
                return Text.Get("VizzyStudio.MfdSave.NoFile");
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var written = new List<(string Id, XElement Original)>();
            try
            {
                foreach (var update in updates)
                {
                    var file = designs.GetCraftFile(update.Id);
                    File.Copy(file.FullName, file.FullName + ".bak-" + stamp, true);
                }

                foreach (var update in updates)
                {
                    var original = new XElement(designs.GetCraftDesign(update.Id));
                    designs.SaveCraft(update.Id, update.Craft);
                    written.Add((update.Id, original));
                }
            }
            catch (Exception e)
            {
                EditorLog.Error("Couldn't write the craft files: " + e);

                // All or none: put back what was already written, through the game so its cached copy follows.
                var restored = new List<string>();
                foreach (var done in written)
                {
                    try
                    {
                        designs.SaveCraft(done.Id, done.Original);
                        restored.Add(done.Id);
                    }
                    catch (Exception again)
                    {
                        EditorLog.Error("Couldn't put back " + done.Id + ": " + again);
                    }
                }

                var left = written.Select(done => done.Id).Except(restored).ToList();
                return Text.Get("VizzyStudio.MfdSave.WriteFailed") + " "
                    + (left.Count > 0 ? Text.Get("VizzyStudio.MfdSave.MayHoldChange", stamp, string.Join(", ", left)) : Text.Get("VizzyStudio.MfdSave.NoFileChanged"));
            }

            EditorLog.Debug("MFD save wrote: " + string.Join(", ", updates.Select(update => update.Id)));
            return updates.Any(update => update.Id == craftName)
                ? Text.Get("VizzyStudio.MfdSave.SavedToCraft", craftName)
                : Text.Get("VizzyStudio.MfdSave.SavedToWorkingCopy") + " "
                    + (designWithoutProgram
                        ? Text.Get("VizzyStudio.MfdSave.DesignLacksProgram", craftName)
                        : Text.Get("VizzyStudio.MfdSave.NoDesign"));
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
