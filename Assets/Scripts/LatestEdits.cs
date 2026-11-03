namespace Assets.Scripts
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;

    /// <summary>
    /// Remembers the latest program applied to each part for the rest of this flight, so a quickload
    /// restores the craft and thread positions but keeps the latest edit. Memory only: leaving the
    /// flight forgets everything, and only quicksaving writes an edit into a save.
    /// </summary>
    public static class LatestEdits
    {
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

        /// <summary>Records an applied edit. Both versions now count as part of this edit history.</summary>
        public static void Remember(string part, XElement before, XElement applied)
        {
            if (!Entries.TryGetValue(part, out var entry))
            {
                entry = new Entry();
                Entries[part] = entry;
            }

            entry.Latest = new XElement(applied);
            entry.Known.Add(new XElement(before));
            entry.Known.Add(new XElement(applied));
        }

        /// <summary>
        /// The program to put back after a load, or null to keep what was loaded. Saved threads show
        /// this is a resumed flight rather than a new launch, and the loaded program has to be one
        /// from this part's edit history.
        /// </summary>
        public static XElement ProgramToRestore(string part, XElement loaded, XElement loadedProcess)
        {
            if (loadedProcess == null || !Entries.TryGetValue(part, out var entry)
                || XNode.DeepEquals(loaded, entry.Latest)
                || !entry.Known.Any(program => XNode.DeepEquals(program, loaded)))
            {
                return null;
            }

            entry.Loaded = new XElement(loaded);
            return new XElement(entry.Latest);
        }

        /// <summary>The program the last load brought before the latest edit replaced it, or null.</summary>
        public static XElement LoadedProgram(string part)
        {
            return Entries.TryGetValue(part, out var entry) ? entry.Loaded : null;
        }

        public static void Forget(string part)
        {
            Entries.Remove(part);
        }

        public static void Clear()
        {
            Entries.Clear();
        }

        private sealed class Entry
        {
            public List<XElement> Known { get; } = new List<XElement>();

            public XElement Latest { get; set; }

            public XElement Loaded { get; set; }
        }
    }
}
