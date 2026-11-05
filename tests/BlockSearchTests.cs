using System;
using System.Linq;
using Assets.Scripts;

/// <summary>Offline tests of what the block search finds.</summary>
public static class BlockSearchTests
{
    public static void Run(Action<string, bool> check)
    {
        var entries = new[]
        {
            new BlockEntry("wait until", "wait-until", "Program Flow", null),
            new BlockEntry("wait seconds", "wait-seconds", "Program Flow", null),
            new BlockEntry("+", "op-add", "Operators", null),
            new BlockEntry("-", "op-sub", "Operators", null),
            new BlockEntry("altitude AGL", "prop-altitude", "Craft Information", null),
            new BlockEntry("altitude ASL", "prop-altitude", "Craft Information", null),
            new BlockEntry("await signal", "other", "Events", null),
        };

        string[] Titles(string query, int limit = 30) => BlockSearch.Find(query, entries, limit).Select(entry => entry.Title).ToArray();

        check("a symbol finds its block", Titles("+").SequenceEqual(new[] { "+" }));
        check("wait finds both blocks first", Titles("wait").Take(2).SequenceEqual(new[] { "wait until", "wait seconds" }));
        check("start of name beats inside a word", Titles("wait").Last() == "await signal");
        check("an alias finds the block", Titles("plus").SequenceEqual(new[] { "+" }));
        check("a dropdown choice is found", Titles("agl").SequenceEqual(new[] { "altitude AGL" }));
        check("all words have to match", Titles("altitude asl").SequenceEqual(new[] { "altitude ASL" }));
        check("case doesn't matter", Titles("WAIT UNTIL").SequenceEqual(new[] { "wait until" }));
        check("nothing typed shows nothing", Titles("  ").Length == 0);
        check("no match shows nothing", Titles("xyzzy").Length == 0);
        check("the limit holds", Titles("a", 2).Length == 2);
    }
}
