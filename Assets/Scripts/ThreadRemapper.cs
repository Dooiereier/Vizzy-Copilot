namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;

    /// <summary>
    /// Carries a running program's saved state (threads, call stacks, block states) over to an edited
    /// version of the program. Saving renumbers every block, so old and new blocks are matched by what
    /// they are and where they sit. Works on XML only, so it can be tested without the game.
    /// </summary>
    public static class ThreadRemapper
    {
        private const int WeightSameBlockAndContents = 1000;
        private const int WeightSameBlock = 100;
        private const int WeightSameContents = 50;
        private const int WeightSameType = 10;

        public sealed class Result
        {
            public XElement Process { get; set; }

            /// <summary>Gets each old block id that still exists, with its new id.</summary>
            public Dictionary<int, int> Ids { get; } = new Dictionary<int, int>();

            /// <summary>Gets one line per thread that moved or ended and per variable added.</summary>
            public List<string> Notes { get; } = new List<string>();

            public int ThreadsEnded { get; set; }
        }

        public static Result Remap(XElement oldProgram, XElement newProgram, XElement process)
        {
            var result = new Result { Process = new XElement(process) };
            var oldBlocks = new Dictionary<int, Block>();
            var newBlocks = new Dictionary<int, Block>();
            var oldStacks = ReadStacks(oldProgram, oldBlocks);
            var newStacks = ReadStacks(newProgram, newBlocks);

            MatchBlocks(oldStacks, newStacks, result.Ids);

            foreach (var thread in result.Process.Elements("Thread").ToList())
            {
                RemapThread(thread, oldBlocks, newBlocks, result);
            }

            AddNewVariables(newProgram, result);
            return result;
        }

        private static void MatchBlocks(List<List<Block>> oldStacks, List<List<Block>> newStacks, Dictionary<int, int> ids)
        {
            var oldLeft = oldStacks.ToList();
            var newLeft = newStacks.ToList();

            // Unchanged stacks first; identical ones are told apart by where they sit on the canvas.
            foreach (var oldStack in oldLeft.ToList())
            {
                var key = ChainKey(oldStack);
                var same = newLeft.Where(stack => ChainKey(stack) == key)
                    .OrderBy(stack => Distance(oldStack[0], stack[0]))
                    .FirstOrDefault();
                if (same != null)
                {
                    Align(oldStack, same, ids);
                    oldLeft.Remove(oldStack);
                    newLeft.Remove(same);
                }
            }

            // Then edited stacks, best match first.
            var candidates =
                from oldStack in oldLeft
                from newStack in newLeft
                where SameStack(oldStack, newStack)
                orderby Score(oldStack, newStack) descending, Distance(oldStack[0], newStack[0])
                select (oldStack, newStack);
            foreach (var (oldStack, newStack) in candidates.ToList())
            {
                if (oldLeft.Contains(oldStack) && newLeft.Contains(newStack))
                {
                    Align(oldStack, newStack, ids);
                    oldLeft.Remove(oldStack);
                    newLeft.Remove(newStack);
                }
            }

            MatchMovedBlocks(oldStacks, newStacks, ids);
        }

        /// <summary>Follows blocks dragged elsewhere, but only when exactly one block could be the one.</summary>
        private static void MatchMovedBlocks(List<List<Block>> oldStacks, List<List<Block>> newStacks, Dictionary<int, int> ids)
        {
            foreach (var key in new Func<Block, string>[] { block => block.Full, block => block.Own })
            {
                var matched = new HashSet<int>(ids.Values);
                var oldLeft = oldStacks.SelectMany(AllBlocks).Where(block => !ids.ContainsKey(block.Id)).ToList();
                var newLeft = newStacks.SelectMany(AllBlocks).Where(block => !matched.Contains(block.Id)).ToList();
                foreach (var group in oldLeft.GroupBy(key).Where(group => group.Count() == 1))
                {
                    var targets = newLeft.Where(block => key(block) == group.Key).ToList();
                    if (targets.Count == 1 && !ids.ContainsKey(group.First().Id) && !ids.ContainsValue(targets[0].Id))
                    {
                        Pair(group.First(), targets[0], ids);
                    }
                }
            }
        }

        private static void Pair(Block oldBlock, Block newBlock, Dictionary<int, int> ids)
        {
            ids[oldBlock.Id] = newBlock.Id;
            for (var i = 0; i < Math.Min(oldBlock.Children.Count, newBlock.Children.Count); i++)
            {
                Align(oldBlock.Children[i], newBlock.Children[i], ids);
            }
        }

        private static void Align(List<Block> oldChain, List<Block> newChain, Dictionary<int, int> ids)
        {
            var scores = ScoreTable(oldChain, newChain);
            var i = oldChain.Count;
            var j = newChain.Count;
            while (i > 0 && j > 0)
            {
                var weight = Weight(oldChain[i - 1], newChain[j - 1]);
                if (weight > 0 && scores[i, j] == scores[i - 1, j - 1] + weight)
                {
                    Pair(oldChain[i - 1], newChain[j - 1], ids);
                    i--;
                    j--;
                }
                else if (scores[i, j] == scores[i - 1, j])
                {
                    i--;
                }
                else
                {
                    j--;
                }
            }
        }

        private static int Score(List<Block> oldChain, List<Block> newChain)
        {
            return ScoreTable(oldChain, newChain)[oldChain.Count, newChain.Count];
        }

        /// <summary>The best total weight of in-order pairs for every pair of chain starts.</summary>
        private static int[,] ScoreTable(List<Block> oldChain, List<Block> newChain)
        {
            var scores = new int[oldChain.Count + 1, newChain.Count + 1];
            for (var i = 1; i <= oldChain.Count; i++)
            {
                for (var j = 1; j <= newChain.Count; j++)
                {
                    var best = Math.Max(scores[i - 1, j], scores[i, j - 1]);
                    var weight = Weight(oldChain[i - 1], newChain[j - 1]);
                    scores[i, j] = weight > 0 ? Math.Max(best, scores[i - 1, j - 1] + weight) : best;
                }
            }

            return scores;
        }

        private static int Weight(Block oldBlock, Block newBlock)
        {
            if (oldBlock.Name != newBlock.Name)
            {
                return 0;
            }

            if (oldBlock.Full == newBlock.Full)
            {
                return WeightSameBlockAndContents;
            }

            var weight = WeightSameType;
            if (oldBlock.Own == newBlock.Own)
            {
                weight += WeightSameBlock;
            }

            if (oldBlock.Children.Count > 0 && oldBlock.ChildrenKey == newBlock.ChildrenKey)
            {
                weight += WeightSameContents;
            }

            return weight;
        }

        /// <summary>Half the blocks unchanged, or the same first block and half the block types in order.</summary>
        private static bool SameStack(List<Block> oldStack, List<Block> newStack)
        {
            var half = oldStack.Count * WeightSameBlock / 2;
            if (Score(oldStack, newStack) >= half)
            {
                return true;
            }

            return oldStack[0].Own == newStack[0].Own && Score(oldStack, newStack) >= oldStack.Count * WeightSameType / 2;
        }

        private static void RemapThread(XElement thread, Dictionary<int, Block> oldBlocks, Dictionary<int, Block> newBlocks, Result result)
        {
            var next = (int?)thread.Attribute("nextInstruction");
            if (next == null)
            {
                return;
            }

            // A thread with no next block is finished at once, so where a block and everything after
            // it are gone, leave its frame as the game would at the end of that chain.
            var frames = thread.Element("CallStack")?.Elements("StackFrame").ToList() ?? new List<XElement>();
            var survivor = Survivor(next, oldBlocks, result.Ids);
            while (survivor == null && frames.Count > 0)
            {
                var top = frames[frames.Count - 1];
                frames.RemoveAt(frames.Count - 1);
                survivor = Survivor((int?)top.Attribute("returnId"), oldBlocks, result.Ids);
                top.Remove();
            }

            if (survivor == null)
            {
                thread.Remove();
                result.ThreadsEnded++;
                result.Notes.Add($"Thread at block {next} ended: its block and everything after it were deleted.");
                return;
            }

            var target = result.Ids[survivor.Id];
            if (target != next)
            {
                result.Notes.Add($"Thread moved from block {next} to block {target}.");
            }

            thread.SetAttributeValue("nextInstruction", target);
            var ownerFrames = RebuildOwnerFrames(thread, frames, survivor, newBlocks[target], result);
            foreach (var frame in frames.Take(frames.Count - ownerFrames))
            {
                // No return block is fine: the game then carries on leaving frames.
                frame.SetAttributeValue("returnId", Survivor((int?)frame.Attribute("returnId"), oldBlocks, result.Ids) is Block block ? result.Ids[block.Id] : (int?)null);
                RemapStates(frame, result.Ids);
            }
        }

        /// <summary>
        /// The top frames belong to the loops and ifs around the thread's block. Gives the new block the
        /// frames of its own surroundings, keeping a frame's variables and states where its owner
        /// survived. Returns how many frames now belong to those owners.
        /// </summary>
        private static int RebuildOwnerFrames(XElement thread, List<XElement> frames, Block oldBlock, Block newBlock, Result result)
        {
            var oldOwners = Owners(oldBlock);
            var newOwners = Owners(newBlock);
            var callers = Math.Max(1, frames.Count - oldOwners.Count);
            var ownerFrames = frames.Skip(callers).ToList();
            oldOwners = oldOwners.Skip(oldOwners.Count - ownerFrames.Count).ToList();

            foreach (var frame in ownerFrames)
            {
                frame.Remove();
            }

            frames.RemoveRange(callers, ownerFrames.Count);
            var callStack = thread.Element("CallStack");
            var kept = 0;
            foreach (var owner in newOwners)
            {
                var index = oldOwners.FindIndex(old => result.Ids.TryGetValue(old.Id, out var id) && id == owner.Id);
                XElement frame;
                if (index >= 0)
                {
                    frame = ownerFrames[index];
                    RemapStates(frame, result.Ids);
                    kept++;
                }
                else
                {
                    frame = new XElement("StackFrame", new XElement("Variables"));
                }

                frame.SetAttributeValue("returnId", owner.ReturnBlockId());
                callStack.Add(frame);
                frames.Add(frame);
            }

            if (kept != oldOwners.Count || kept != newOwners.Count)
            {
                result.Notes.Add($"Thread's surrounding loops or ifs changed: {oldOwners.Count} before, {newOwners.Count} after.");
            }

            return newOwners.Count;
        }

        /// <summary>The loops and ifs around a block in its stack, outermost first.</summary>
        private static List<Block> Owners(Block block)
        {
            var owners = new List<Block>();
            for (var owner = block.Parent; owner != null; owner = owner.Parent)
            {
                owners.Insert(0, owner);
            }

            return owners;
        }

        /// <summary>A block or, if it was deleted, the next surviving block in its chain.</summary>
        private static Block Survivor(int? oldId, Dictionary<int, Block> oldBlocks, Dictionary<int, int> ids)
        {
            if (oldId == null || !oldBlocks.TryGetValue(oldId.Value, out var block))
            {
                return null;
            }

            for (var i = block.Index; i < block.Chain.Count; i++)
            {
                if (ids.ContainsKey(block.Chain[i].Id))
                {
                    return block.Chain[i];
                }
            }

            return null;
        }

        private static void RemapStates(XElement frame, Dictionary<int, int> ids)
        {
            var states = (string)frame.Attribute("states");
            if (states == null)
            {
                return;
            }

            // The game can't load a state for a block that no longer exists.
            var kept = states.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(state => state.Split('='))
                .Where(parts => parts.Length == 2 && int.TryParse(parts[0], out var id) && ids.ContainsKey(id))
                .Select(parts => ids[int.Parse(parts[0])] + "=" + parts[1])
                .ToList();
            frame.SetAttributeValue("states", kept.Count > 0 ? string.Join(",", kept) : null);
        }

        /// <summary>Saved values stay; variables the edit added are created with the program's values.</summary>
        private static void AddNewVariables(XElement newProgram, Result result)
        {
            var saved = result.Process.Element("Variables");
            var added = newProgram.Element("Variables")?.Elements().ToList();
            if (saved == null || added == null)
            {
                return;
            }

            var names = new HashSet<string>(saved.Elements().Select(variable => (string)variable.Attribute("name")));
            foreach (var variable in added.Where(variable => !names.Contains((string)variable.Attribute("name"))))
            {
                saved.Add(new XElement(variable));
                result.Notes.Add($"Variable {(string)variable.Attribute("name")} added.");
            }
        }

        private static List<List<Block>> ReadStacks(XElement program, Dictionary<int, Block> blocks)
        {
            return program.Elements("Instructions")
                .Select(stack => ReadChain(stack, blocks, null))
                .Where(chain => chain.Count > 0)
                .ToList();
        }

        private static List<Block> ReadChain(XElement instructions, Dictionary<int, Block> blocks, Block parent)
        {
            var chain = new List<Block>();
            foreach (var element in instructions.Elements())
            {
                var block = new Block(element, chain, chain.Count, blocks, parent);
                chain.Add(block);
                blocks[block.Id] = block;
            }

            return chain;
        }

        private static IEnumerable<Block> AllBlocks(List<Block> chain)
        {
            return chain.SelectMany(block => new[] { block }.Concat(block.Children.SelectMany(AllBlocks)));
        }

        /// <summary>
        /// A copy without whitespace-only text. A program loaded from a craft file keeps the file's line breaks
        /// and indents, while one from the editor has none, so the same block would otherwise look edited.
        /// </summary>
        public static XElement WithoutWhitespace(XElement element)
        {
            var copy = new XElement(element);
            RemoveWhitespace(copy);
            return copy;
        }

        private static void RemoveWhitespace(XElement element)
        {
            element.DescendantNodes().OfType<XText>().Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList().ForEach(text => text.Remove());
        }

        private static string ChainKey(List<Block> chain)
        {
            return string.Join("\n", chain.Select(block => block.Full));
        }

        private static double Distance(Block a, Block b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        private sealed class Block
        {
            public Block(XElement element, List<Block> chain, int index, Dictionary<int, Block> blocks, Block parent)
            {
                Chain = chain;
                Parent = parent;
                Index = index;
                Id = (int)element.Attribute("id");
                Name = element.Name.LocalName;

                var position = ((string)element.Attribute("pos"))?.Split(',');
                if (position?.Length == 2)
                {
                    double.TryParse(position[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x);
                    double.TryParse(position[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y);
                    X = x;
                    Y = y;
                }

                Children = element.Elements("Instructions").Select(child => ReadChain(child, blocks, this)).ToList();

                var own = new XElement(element);
                own.Elements("Instructions").Remove();
                RemoveWhitespace(own);
                foreach (var part in own.DescendantsAndSelf())
                {
                    part.Attribute("id")?.Remove();
                    part.Attribute("pos")?.Remove();
                }

                Own = own.ToString(SaveOptions.DisableFormatting);
                ChildrenKey = string.Join("\n---\n", Children.Select(ChainKey));
                Full = Own + "\n{" + ChildrenKey + "}";
            }

            public Block Parent { get; }

            public List<Block> Chain { get; }

            public List<List<Block>> Children { get; }

            public string ChildrenKey { get; }

            /// <summary>Gets the block with everything inside it, without ids and canvas positions.</summary>
            public string Full { get; }

            public int Id { get; }

            public int Index { get; }

            public string Name { get; }

            /// <summary>Gets the block itself with its values, without the blocks nested in it.</summary>
            public string Own { get; }

            public double X { get; }

            public double Y { get; }

            /// <summary>Where the game continues after this block's body: loops come back, ifs move on past their else-ifs.</summary>
            public int? ReturnBlockId()
            {
                if (Name != "If" && Name != "ElseIf")
                {
                    return Id;
                }

                var next = Index + 1;
                while (next < Chain.Count && (Chain[next].Name == "ElseIf" || Chain[next].Name == "Comment"))
                {
                    next++;
                }

                return next < Chain.Count ? Chain[next].Id : (int?)null;
            }
        }
    }
}
