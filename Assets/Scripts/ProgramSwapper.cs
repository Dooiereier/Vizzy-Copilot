namespace Assets.Scripts
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Craft.Parts.Modifiers.Mfd;
    using ModApi.Craft.Program.Craft;

    /// <summary>Puts a program in place on a part, carrying its running threads over.</summary>
    public static class ProgramSwapper
    {
        private static readonly FieldInfo CraftServiceField = typeof(FlightProgramScript).GetField("_craftService", BindingFlags.Instance | BindingFlags.NonPublic);

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

            SilenceBeep(script);
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

            PrimeMfd(script);

            // Serializing straight after restoring shows whether every thread came back as remapped.
            script.UpdateXml();
            LogThreads("After", data.ProcessXml, newProgram);
            if (remapped != null && !XNode.DeepEquals(remapped.Process, data.ProcessXml))
            {
                // Not always a fault: a variable holding "#499c57" comes back as the color's numbers.
                EditorLog.Debug("Resume check: the running state isn't exactly what was restored.");
                LogFirstDifference(remapped.Process, data.ProcessXml);
            }

            return true;
        }

        /// <summary>Starts a running part's program from the top, which fires "on start" again. Its program stays.</summary>
        public static bool Restart(FlightProgramData data)
        {
            var script = data.Script;
            SilenceBeep(script);
            data.ProcessXml = null;
            script.StartProgram();
            PrimeMfd(script);
            return script.FlightProgram != null;
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

        /// <summary>
        /// The new craft service only finds its MFD in GetMfdWidget, while GetMfdChildWidgets, behind "destroy
        /// all widgets", uses that field directly. A program that starts normally creates a widget first, but a
        /// resumed thread can reach "destroy all" first, which throws. Private: the script doesn't expose its service.
        /// </summary>
        private static void PrimeMfd(FlightProgramScript script)
        {
            if (script.PartScript.GetModifier<MfdScript>() == null)
            {
                return;
            }

            var service = CraftServiceField?.GetValue(script) as ICraftService;
            if (service == null)
            {
                EditorLog.Warning("Couldn't reach the MFD program's craft service: its 'destroy all widgets' block may fail.");
                return;
            }

            service.GetMfdWidget("_Screen");
        }

        private static void LogFirstDifference(XElement restored, XElement running)
        {
            var before = restored.DescendantsAndSelf().ToList();
            var after = running.DescendantsAndSelf().ToList();
            for (var i = 0; i < before.Count && i < after.Count; i++)
            {
                var a = before[i];
                var b = after[i];
                var sameAttributes = a.Attributes().Select(item => item.ToString()).SequenceEqual(b.Attributes().Select(item => item.ToString()));
                if (a.Name != b.Name || !sameAttributes)
                {
                    EditorLog.Debug("Restored: <" + a.Name + " " + string.Join(" ", a.Attributes()) + ">");
                    EditorLog.Debug("Running: <" + b.Name + " " + string.Join(" ", b.Attributes()) + ">");
                    return;
                }
            }

            EditorLog.Debug("Restored has " + before.Count + " elements, running has " + after.Count + ".");
        }

        /// <summary>A beep's looping sound is only stopped by the craft service that StartProgram replaces.</summary>
        private static void SilenceBeep(FlightProgramScript script)
        {
            var beep = script.GetComponent<UnityEngine.AudioSource>();
            if (beep != null)
            {
                beep.volume = 0f;
            }
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
