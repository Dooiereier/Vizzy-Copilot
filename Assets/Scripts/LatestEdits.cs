namespace Assets.Scripts
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;

    /// <summary>
    /// Remembers the latest program applied to each part for the rest of this flight, so a quickload
    /// restores the craft and thread positions but keeps the latest edit. Memory only: leaving the
    /// flight or a new launch forgets it. Saving a state stores whatever program is then running.
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
        /// this is a resumed flight; without them it's a new launch, which ends this part's edit
        /// history. The loaded program has to be one from that history.
        /// </summary>
        public static XElement ProgramToRestore(string part, XElement loaded, XElement loadedProcess)
        {
            if (loadedProcess == null)
            {
                Entries.Remove(part);
                return null;
            }

            if (!Entries.TryGetValue(part, out var entry))
            {
                return null;
            }

            if (XNode.DeepEquals(loaded, entry.Latest))
            {
                entry.Loaded = null;
                return null;
            }

            if (!entry.Known.Any(program => XNode.DeepEquals(program, loaded)))
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
