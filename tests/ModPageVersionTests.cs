using System;
using Assets.Scripts;

/// <summary>Offline tests of reading the version from a mod's page.</summary>
public static class ModPageVersionTests
{
    public static void Run(Action<string, bool> check)
    {
        const string page = "<span class=\"spec-label\">Game Version:</span> <span class=\"spec-value\">1.4.1</span>"
            + "<span class=\"spec-label\">Mod Version:</span>\n  <span class=\"spec-value\">1.12</span>";
        check("reads the mod version, not the game's", new Version(1, 12).Equals(ModPageVersion.Parse(page)));
        check("a page without one gives null", ModPageVersion.Parse("<html>the site's mod list</html>") == null);
        check("no text gives null", ModPageVersion.Parse(null) == null);
        check("a newer version compares as newer", new Version(1, 12).CompareTo(new Version(1, 2)) > 0);
    }
}
