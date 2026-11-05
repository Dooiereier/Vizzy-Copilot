namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>The groups of search results, in the order they are shown.</summary>
    public enum BlockGroup
    {
        Block,
        Variable,
        CustomExpression,
        CustomInstruction,
        List,
    }

    /// <summary>One thing the block search can offer: a toolbox block, with a dropdown choice if it has one.</summary>
    public sealed class BlockEntry
    {
        public BlockEntry(string title, string styleId, string category, object source, BlockGroup group = BlockGroup.Block)
        {
            Group = group;
            Title = BlockSearch.PlainText(title);
            Terms = (Title + " " + styleId + " " + category + " " + BlockSearch.AliasesFor(styleId)).ToLowerInvariant();
            Source = source;
        }

        public BlockGroup Group { get; }

        public string Title { get; }

        public string Terms { get; }

        /// <summary>Gets what the panel needs to build the block. Plain text logic stays unaware of it.</summary>
        public object Source { get; }
    }

    /// <summary>What matches a search. Plain text logic, tested offline.</summary>
    public static class BlockSearch
    {
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            ["op-add"] = "plus add",
            ["op-sub"] = "minus subtract",
            ["op-mul"] = "times multiply",
            ["op-div"] = "divide",
            ["op-exp"] = "power exponent",
            ["op-mod"] = "modulus remainder",
            ["evaluate-expression"] = "funk",
        };

        /// <summary>Block text without rich text tags: the Funk block's name is colored letter by letter.</summary>
        public static string PlainText(string text)
        {
            return Regex.Replace(text ?? string.Empty, "<[^>]*>", string.Empty).Trim();
        }

        public static string AliasesFor(string styleId)
        {
            return styleId != null && Aliases.TryGetValue(styleId, out var aliases) ? aliases : string.Empty;
        }

        /// <summary>Every word has to match. Results go by group, closer matches first within a group, then toolbox order.</summary>
        public static List<BlockEntry> Find(string query, IEnumerable<BlockEntry> entries, int limit)
        {
            var words = (query ?? string.Empty).ToLowerInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return new List<BlockEntry>();
            }

            var whole = string.Join(" ", words);
            return entries
                .Where(entry => words.All(entry.Terms.Contains))
                .OrderBy(entry => entry.Group)
                .ThenBy(entry => Rank(entry.Title.ToLowerInvariant(), whole))
                .Take(limit)
                .ToList();
        }

        /// <summary>The result an arrow key moves to. A single press wraps around, a held key stops at the end.</summary>
        public static int MoveSelection(int current, int delta, int count, bool wrap)
        {
            if (count <= 0)
            {
                return -1;
            }

            var next = current + delta;
            if (wrap)
            {
                return (next + count) % count;
            }

            return next < 0 ? 0 : next >= count ? count - 1 : next;
        }

        private static int Rank(string title, string whole)
        {
            if (title == whole)
            {
                return 0;
            }

            if (title.StartsWith(whole, StringComparison.Ordinal))
            {
                return 1;
            }

            if (title.Contains(" " + whole))
            {
                return 2;
            }

            return title.Contains(whole) ? 3 : 4;
        }
    }
}
