namespace Assets.Scripts
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;

    /// <summary>Puts a program in place on a part, carrying its running threads over.</summary>
    public static class ProgramSwapper
    {
        /// <summary>For a running part. Returns false if the new program didn't load and the old one is back.</summary>
        public static bool Swap(FlightProgramData data, XElement newProgram, out string message)
        {
            var script = data.Script;
            var oldProgram = data.FlightProgramXml;
            script.UpdateXml();
            var oldProcess = data.ProcessXml == null ? null : new XElement(data.ProcessXml);
            LogThreads("Before", oldProcess, oldProgram);

            // Remap before changing anything, so a failure here leaves the program running as it was.
            var remapped = oldProcess == null ? null : ThreadRemapper.Remap(oldProgram, newProgram, oldProcess);
            message = LogRemap(remapped);

            data.FlightProgramXml = newProgram;
            data.ProcessXml = remapped == null ? null : new XElement(remapped.Process);
            script.StartProgram();

            if (script.FlightProgram == null)
            {
                EditorLog.Error("The new program didn't load: restoring the old one.");
                message = "The edited Vizzy program didn't load: the old one is still running.";
                data.FlightProgramXml = oldProgram;
                data.ProcessXml = oldProcess;
                script.StartProgram();
                return false;
            }

            // Serializing straight after restoring shows whether every thread came back as remapped.
            script.UpdateXml();
            LogThreads("After", data.ProcessXml, newProgram);
            if (remapped != null)
            {
                EditorLog.Debug("Resume check: running state " + (XNode.DeepEquals(remapped.Process, data.ProcessXml) ? "as expected" : "DIFFERENT"));
            }

            return true;
        }

        /// <summary>For a part the game hasn't started yet: it starts from these, with no frame on the old program.</summary>
        public static string Prepare(FlightProgramData data, XElement newProgram)
        {
            var remapped = ThreadRemapper.Remap(data.FlightProgramXml, newProgram, data.ProcessXml);
            var message = LogRemap(remapped);
            data.FlightProgramXml = newProgram;
            data.ProcessXml = new XElement(remapped.Process);
            return message;
        }

        private static string LogRemap(ThreadRemapper.Result remapped)
        {
            if (remapped == null)
            {
                return null;
            }

            foreach (var note in remapped.Notes)
            {
                EditorLog.Debug(note);
            }

            return remapped.ThreadsEnded > 0 ? remapped.ThreadsEnded + " Vizzy thread(s) ended: their blocks were deleted." : null;
        }

        private static Dictionary<int, string> BlocksById(XElement program)
        {
            var blocks = new Dictionary<int, string>();
            foreach (var element in program.Descendants())
            {
                var id = element.Attribute("id");
                if (id != null && int.TryParse(id.Value, out var value))
                {
                    blocks[value] = element.Name.LocalName;
                }
            }

            return blocks;
        }

        private static void LogThreads(string label, XElement process, XElement program)
        {
            var blocks = BlocksById(program);
            var threads = process?.Elements("Thread").ToList() ?? new List<XElement>();
            EditorLog.Debug(label + ": " + threads.Count + " threads, " + (process?.Element("Variables")?.Elements().Count() ?? 0) + " variables.");
            foreach (var thread in threads)
            {
                var next = (string)thread.Attribute("nextInstruction");
                var name = next != null && int.TryParse(next, out var id) && blocks.TryGetValue(id, out var block) ? block : "?";
                EditorLog.Debug("  thread at " + next + " (" + name + "), stack frames: " + (thread.Element("CallStack")?.Elements().Count() ?? 0));
            }
        }
    }
}
