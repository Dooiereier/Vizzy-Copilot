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
        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>Records an applied edit. Both versions now count as part of this edit history.</summary>
        public static void Remember(string part, XElement before, XElement applied)
        {
            var entry = Entries.FirstOrDefault(item => item.Part == part) ?? Entries.FirstOrDefault(item => item.Knows(before));
            if (entry == null)
            {
                entry = new Entry();
                Entries.Add(entry);
            }

            entry.Part = part;
            entry.Latest = new XElement(applied);
            entry.Known.Add(new XElement(before));
            entry.Known.Add(new XElement(applied));
        }

        /// <summary>
        /// The program to put back after a load, or null to keep what was loaded. Saved threads show
        /// this is a resumed flight; without them it's a new launch, which ends that program's edit
        /// history. Histories are found by program, because a part's id changes when a separation or
        /// docking moves it to another craft. The id only settles which history when several know it.
        /// </summary>
        public static XElement ProgramToRestore(string part, XElement loaded, XElement loadedProcess)
        {
            if (loadedProcess == null)
            {
                Entries.RemoveAll(item => item.Knows(loaded));
                return null;
            }

            var matches = Entries.Where(item => item.Knows(loaded)).ToList();
            var entry = matches.FirstOrDefault(item => item.Part == part) ?? (matches.Count == 1 ? matches[0] : null);
            if (entry == null)
            {
                return null;
            }

            entry.Part = part;
            if (XNode.DeepEquals(loaded, entry.Latest))
            {
                entry.Loaded = null;
                return null;
            }

            entry.Loaded = new XElement(loaded);
            return new XElement(entry.Latest);
        }

        /// <summary>The program the last load brought before the latest edit replaced it, or null.</summary>
        public static XElement LoadedProgram(string part)
        {
            return Entries.FirstOrDefault(item => item.Part == part)?.Loaded;
        }

        public static void Forget(string part)
        {
            Entries.RemoveAll(item => item.Part == part);
        }

        public static void Clear()
        {
            Entries.Clear();
        }

        private sealed class Entry
        {
            public string Part { get; set; }

            public List<XElement> Known { get; } = new List<XElement>();

            public XElement Latest { get; set; }

            public XElement Loaded { get; set; }

            public bool Knows(XElement program)
            {
                return Known.Any(known => XNode.DeepEquals(known, program));
            }
        }
    }
}
