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
            new BlockEntry("<color=#0f0>f</color><color=#f0f>UN</color><color=#f00>k</color>", "evaluate-expression", "Operators", null),
            new BlockEntry("LaunchPitch", "variable", "Variables", null),
        };

        string[] Titles(string query, int limit = 30) => BlockSearch.Find(query, entries, limit).Select(entry => entry.Title).ToArray();

        check("a symbol finds its block", Titles("+").SequenceEqual(new[] { "+" }));
        check("wait finds both blocks first", Titles("wait").Take(2).SequenceEqual(new[] { "wait until", "wait seconds" }));
        check("start of name beats inside a word", Titles("wait").Last() == "await signal");
        check("an alias finds the block", Titles("plus").SequenceEqual(new[] { "+" }));
        check("a dropdown choice is found", Titles("agl").SequenceEqual(new[] { "altitude AGL" }));
        check("all words have to match", Titles("altitude asl").SequenceEqual(new[] { "altitude ASL" }));
        check("case doesn't matter", Titles("WAIT UNTIL").SequenceEqual(new[] { "wait until" }));
        check("rich text is removed from names", Titles("funk").SequenceEqual(new[] { "fUNk" }));
        check("a variable is found by name", Titles("launchp").SequenceEqual(new[] { "LaunchPitch" }));
        check("nothing typed shows nothing", Titles("  ").Length == 0);
        check("no match shows nothing", Titles("xyzzy").Length == 0);
        check("the limit holds", Titles("a", 2).Length == 2);

        check("down moves to the next result", BlockSearch.MoveSelection(0, 1, 3, true) == 1);
        check("a press wraps from last to first", BlockSearch.MoveSelection(2, 1, 3, true) == 0);
        check("a press wraps from first to last", BlockSearch.MoveSelection(0, -1, 3, true) == 2);
        check("a held key stops at the last", BlockSearch.MoveSelection(2, 1, 3, false) == 2);
        check("a held key stops at the first", BlockSearch.MoveSelection(0, -1, 3, false) == 0);
        check("no results, no selection", BlockSearch.MoveSelection(0, 1, 0, true) == -1);

        var mixed = new[]
        {
            new BlockEntry("Pitch list", "list", "Lists", null, BlockGroup.List),
            new BlockEntry("Pitch step", "custom instruction", "Custom Instructions", null, BlockGroup.CustomInstruction),
            new BlockEntry("Pitch value", "custom expression", "Custom Expressions", null, BlockGroup.CustomExpression),
            new BlockEntry("Pitch", "variable", "Variables", null, BlockGroup.Variable),
            new BlockEntry("my pitch", "variable", "Variables", null, BlockGroup.Variable),
            new BlockEntry("set pitch", "set-pitch", "Craft Instructions", null),
        };
        var order = BlockSearch.Find("pitch", mixed, 30).Select(entry => entry.Title).ToArray();
        check("groups come in order: blocks, variables, custom expressions, custom instructions, lists", order.SequenceEqual(new[] { "set pitch", "Pitch", "my pitch", "Pitch value", "Pitch step", "Pitch list" }));
    }
}
