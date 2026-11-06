using Assets.Scripts.Vizzy.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Assets.Scripts.Exporter
{
    /// <summary>How many of each dependency kind TryExport actually created in the target file.</summary>
    public struct ExportCounts
    {
        public int VariablesCreated;
        public int CustomExpressionsCreated;
        public int CustomInstructionsCreated;
    }

    /// <summary>
    /// Lists existing Vizzy program files and writes a captured snippet into one of them.
    /// This never touches the running game's in-memory state - it edits the target .xml
    /// file directly on disk, the same file format ProgramSerializer itself reads and
    /// writes, so nothing here needs to know how to build on-screen blocks.
    /// </summary>
    public static class VizzyProgramFileExporter
    {
        /// <summary>
        /// Where the game keeps saved/importable Vizzy programs. Confirmed field - Vizzy
        /// Studio's own Mod.cs reads this same static path to back up program files.
        /// </summary>
        public static string ProgramsFolder => VizzyUIScript.FlightProgramsFolderPath;

        public static List<string> ListProgramFiles()
        {
            try
            {
                if (string.IsNullOrEmpty(ProgramsFolder) || !Directory.Exists(ProgramsFolder))
                {
                    Debug.LogWarning($"[VizzyStudio] Programs folder not found: {ProgramsFolder}");
                    return new List<string>();
                }

                return Directory.GetFiles(ProgramsFolder, "*.xml")
                    .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VizzyStudio] Couldn't list program files: {ex.Message}");
                return new List<string>();
            }
        }

        // A block's on-canvas position is a "pos" attribute holding "X,Y" plain integers,
        // and it only ever appears on the *first* element of a top-level block - confirmed
        // against kroryan/VizzyCode's VizzyXmlConverter.cs (EmitTopLevelBlockMetadata reads
        // firstEl.Attribute("pos"); AssignNodeIdsRecursive/EmitVariableDeclarations-adjacent
        // layout code only ever writes "pos" onto the first instruction of each block, or
        // onto each top-level CustomExpression). The same source also confirms the program
        // root holds *multiple* sibling <Instructions> elements - one per independent
        // top-level column/script (`program.Elements("Instructions")`, plural) - each
        // containing its own flat, ordered chain of instruction siblings, versus a *single*
        // shared <Expressions> element (`program.Element("Expressions")`, singular) holding
        // all standalone expression-family entries as flat siblings.
        //
        // That mattered for a real bug: an earlier version of this method merged an
        // exported instruction chain into the target's *existing* first <Instructions>
        // block, which would have spliced the pasted blocks into whatever column already
        // lived there instead of creating an independent new one. Instruction exports now
        // always get their own brand new <Instructions> sibling.
        private const string PosAttributeName = "pos";

        /// <summary>
        /// Writes a captured snippet into the target file as a new top-level block
        /// (a whole new column for instructions; a new flat entry in the shared Expressions
        /// container for a lone expression), positions it near the middle of the target's
        /// existing content so it lands somewhere visible instead of off in a stale
        /// coordinate copied from the source program, creates any global variables and
        /// custom expressions/instructions it references (transitively) that the target
        /// doesn't already have, and saves the file.
        /// </summary>
        public static bool TryExport(VizzyExportSnippet snippet, string targetFilePath, out ExportCounts counts, out string error)
        {
            error = null;
            counts = default;

            if (snippet == null || snippet.Nodes.Count == 0)
            {
                error = Text.Get("VizzyStudio.Export.Nothing");
                return false;
            }

            try
            {
                if (!File.Exists(targetFilePath))
                {
                    error = Text.Get("VizzyStudio.Export.FileGone");
                    return false;
                }

                XDocument doc = XDocument.Load(targetFilePath);
                XElement root = doc.Root;
                if (root == null)
                {
                    error = Text.Get("VizzyStudio.Export.NotAProgram");
                    return false;
                }

                string customName = (string)snippet.Nodes[0].Attribute("name");
                if (IsCustomDefinitionIn(root, snippet.Nodes[0].Name.LocalName, customName))
                {
                    error = Text.Get("VizzyStudio.Export.NameTaken", customName);
                    return false;
                }

                // Deep-copy every node before touching it - the source XElements may already
                // be parented (e.g. under the throwaway scratch element SerializerBridge
                // uses), and an XElement can't be added as a child in two places at once.
                List<XElement> copiedNodes = snippet.Nodes.Select(n => new XElement(n)).ToList();
                foreach (XElement node in copiedNodes)
                {
                    StripIds(node); // avoid colliding with node ids already used in the target file
                }

                (int anchorX, int anchorY) = ComputeHomeAnchor(root);
                (int mainX, int mainY) = NextStaircasePosition(root, anchorX, anchorY);
                SetPosition(copiedNodes[0], mainX, mainY);

                if (snippet.Kind == VizzyNodeKind.Instruction)
                {
                    // A whole new independent column - never merged into an existing
                    // <Instructions> block (see the note above on why that would be wrong).
                    XElement newBlock = new XElement("Instructions");
                    foreach (XElement node in copiedNodes)
                        newBlock.Add(node);
                    root.Add(newBlock);
                }
                else
                {
                    XElement expressions = root.Element("Expressions");
                    if (expressions == null)
                    {
                        expressions = new XElement("Expressions");
                        root.Add(expressions);
                    }

                    foreach (XElement node in copiedNodes)
                        expressions.Add(node);
                }

                counts = new ExportCounts
                {
                    VariablesCreated = AddMissingVariables(root, snippet.ReferencedGlobalVariableDefinitions),
                    CustomExpressionsCreated = AddMissingCustomExpressions(root, snippet.ReferencedCustomExpressionDefinitions, anchorX, anchorY),
                    CustomInstructionsCreated = AddMissingCustomInstructions(root, snippet.ReferencedCustomInstructionBlocks, anchorX, anchorY),
                };

                AssignMissingIds(root);

                // Only replaces the file once the new one is complete.
                string temporaryPath = targetFilePath + ".tmp";
                try
                {
                    doc.Save(temporaryPath);
                    File.Replace(temporaryPath, targetFilePath, null);
                }
                finally
                {
                    File.Delete(temporaryPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = Text.Get("VizzyStudio.Export.WriteFailed", ex.Message);
                Debug.LogError($"[VizzyStudio] {error}\n{ex}");
                return false;
            }
        }

        /// <summary>
        /// Approximates "where you land when you open this program" - there's no way to read
        /// a program's actual camera/scroll state from outside the running editor (the
        /// &lt;Program&gt; root stores nothing like it, confirmed by inspecting real files -
        /// just "name" and occasionally "requiresMfd"), so this anchors on the FlightStart
        /// ("on start") event's position instead, falling back to the plain origin if none is
        /// found.
        ///
        /// This replaced an earlier version that averaged the position of *every* positioned
        /// top-level block. That broke down on real, heavily-used programs: inspecting one
        /// (2900+ lines) turned up only 3 blocks anywhere with a "pos" attribute at all out of
        /// several dozen top-level blocks - everything else had apparently never been dragged
        /// since creation and simply had none. Averaging such a small, arbitrary sample skews
        /// toward wherever *those specific* blocks happen to sit, not toward where the user
        /// actually views/works - which is exactly what was reported ("I can't find it").
        /// FlightStart is present in essentially every program and, by strong convention
        /// (confirmed by the same file inspection), sits close to the origin - closer to a
        /// real "home" anchor than an unweighted average ever was.
        ///
        /// Every newly-added top-level element in this export (the main block, and each
        /// custom expression/instruction pulled in with it) gets its own step away from this
        /// anchor via NextStaircasePosition, so nothing lands on top of anything else.
        /// </summary>
        private static (int x, int y) ComputeHomeAnchor(XElement root)
        {
            XElement flightStartHead = root.Elements("Instructions")
                .Select(block => block.Elements().FirstOrDefault())
                .FirstOrDefault(head => head != null
                    && head.Name.LocalName == "Event"
                    && (string)head.Attribute("event") == "FlightStart");

            if (flightStartHead != null && TryGetPosition(flightStartHead, out int fsX, out int fsY))
                return (fsX, fsY);

            return (0, 0);
        }

        private const string ExportOffsetAttributeName = "vizzyExporterOffset";
        private const int StepX = 150;

        // Vizzy's canvas Y axis increases upward (confirmed empirically - a positive Y step
        // rendered *above* the previous export, not below), so "down" means a negative step.
        private const int StepY = -100;

        /// <summary>
        /// Each new top-level element written into a given file - across every export, and
        /// every custom expression/instruction pulled in with each one - lands one step
        /// right-and-down from the last, instead of stacking on top of it. The running offset
        /// is stored on the &lt;Program&gt; root as a bookkeeping-only attribute so it
        /// persists across exports (and across game sessions); it means nothing to the game
        /// itself.
        /// </summary>
        private static bool IsCustomDefinitionIn(XElement root, string kind, string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            IEnumerable<XElement> existing =
                kind == "CustomInstruction" ? root.Elements("Instructions").Select(block => block.Elements().FirstOrDefault())
                : kind == "CustomExpression" ? root.Elements("Expressions").Elements()
                : Enumerable.Empty<XElement>();
            return existing.Any(e => e != null && e.Name.LocalName == kind && (string)e.Attribute("name") == name);
        }

        private static (int x, int y) NextStaircasePosition(XElement root, int anchorX, int anchorY)
        {
            int x = 0, y = 0;
            string existing = (string)root.Attribute(ExportOffsetAttributeName);
            if (!string.IsNullOrEmpty(existing))
            {
                string[] parts = existing.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y))
                    x = y = 0;
            }

            // Saving a program in the editor drops the offset, so step past earlier exports.
            var taken = new HashSet<string>(root.Elements("Instructions").Select(block => block.Elements().FirstOrDefault())
                .Concat(root.Elements("Expressions").Elements())
                .Select(e => (string)e?.Attribute(PosAttributeName))
                .Where(pos => pos != null));
            while (taken.Contains($"{anchorX + x},{anchorY + y}"))
            {
                x += StepX;
                y += StepY;
            }

            root.SetAttributeValue(ExportOffsetAttributeName, $"{x + StepX},{y + StepY}");
            return (anchorX + x, anchorY + y);
        }

        private static bool TryGetPosition(XElement element, out int x, out int y)
        {
            x = 0;
            y = 0;

            string value = (string)element?.Attribute(PosAttributeName);
            if (string.IsNullOrEmpty(value))
                return false;

            string[] parts = value.Split(',');
            return parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y);
        }

        private static void SetPosition(XElement element, int x, int y)
        {
            element.SetAttributeValue(PosAttributeName, $"{x},{y}");
        }

        /// <summary>
        /// Removes "id" attributes from a node and everything nested inside it. Ids appear
        /// to be assigned per-file (kroryan/VizzyCode's own XML generator hands out fresh
        /// sequential ids and explicitly avoids colliding with whatever ids already exist),
        /// so carrying a source file's ids into a different target file risks colliding with
        /// unrelated nodes that happen to reuse the same numbers there. Stripping them is
        /// the conservative choice - a freshly-dragged-in toolbox block presumably doesn't
        /// arrive with a pre-assigned id either, so the game should be able to assign new
        /// ones on its own next load/save.
        /// </summary>
        /// <summary>Exported blocks get ids unused in the target, so the file has the same form as one the game saved.</summary>
        private static void AssignMissingIds(XElement root)
        {
            int next = root.Descendants().Select(e => (int?)e.Attribute("id") ?? -1).DefaultIfEmpty(-1).Max() + 1;
            foreach (XElement block in root.Descendants("Instructions").Elements().Where(e => e.Attribute("id") == null))
            {
                block.SetAttributeValue("id", next++);
            }
        }

        private static void StripIds(XElement node)
        {
            node.Attribute("id")?.Remove();
            foreach (XElement descendant in node.Descendants())
            {
                descendant.Attribute("id")?.Remove();
            }
        }

        /// <summary>
        /// Adds a copy of each given variable definition to the target's Variables
        /// container, skipping any name the target already defines (never overwrites an
        /// existing variable - a same-named variable already in the target might be a
        /// different type/value on purpose, and blindly replacing it could break other
        /// blocks there that rely on it). Returns how many were actually added.
        /// </summary>
        private static int AddMissingVariables(XElement root, List<XElement> definitions)
        {
            if (definitions == null || definitions.Count == 0)
                return 0;

            XElement variables = root.Element("Variables");
            if (variables == null)
            {
                variables = new XElement("Variables");
                root.Add(variables);
            }

            HashSet<string> existingNames = new HashSet<string>(
                variables.Elements("Variable").Select(v => (string)v.Attribute("name")).Where(n => n != null),
                StringComparer.Ordinal); // Vizzy is case-sensitive

            int added = 0;
            foreach (XElement definition in definitions)
            {
                string name = (string)definition.Attribute("name");
                if (string.IsNullOrEmpty(name) || existingNames.Contains(name))
                    continue;

                variables.Add(new XElement(definition));
                existingNames.Add(name);
                added++;
            }

            return added;
        }

        /// <summary>
        /// Adds a copy of each given CustomExpression definition (with its body) to the
        /// target's shared Expressions container, skipping any name the target already
        /// defines - same never-overwrite policy as variables, for the same reason (a
        /// same-named custom expression already there might be deliberately different, and
        /// other blocks in the target could already depend on it). Each added definition
        /// gets its own staircased position - many source programs never bother positioning
        /// custom expressions at all, which without this would land every copy on top of
        /// each other in the target.
        /// </summary>
        private static int AddMissingCustomExpressions(XElement root, List<XElement> definitions, int anchorX, int anchorY)
        {
            if (definitions == null || definitions.Count == 0)
                return 0;

            XElement expressions = root.Element("Expressions");
            if (expressions == null)
            {
                expressions = new XElement("Expressions");
                root.Add(expressions);
            }

            HashSet<string> existingNames = new HashSet<string>(
                expressions.Elements("CustomExpression").Select(e => (string)e.Attribute("name")).Where(n => n != null),
                StringComparer.Ordinal);

            int added = 0;
            foreach (XElement definition in definitions)
            {
                string name = (string)definition.Attribute("name");
                if (string.IsNullOrEmpty(name) || existingNames.Contains(name))
                    continue;

                XElement copy = new XElement(definition);
                StripIds(copy);
                (int x, int y) = NextStaircasePosition(root, anchorX, anchorY);
                SetPosition(copy, x, y);
                expressions.Add(copy);
                existingNames.Add(name);
                added++;
            }

            return added;
        }

        /// <summary>
        /// Adds a copy of each given CustomInstruction's whole containing Instructions block
        /// (head element + body chain) as a new top-level block, skipping any name the target
        /// already defines - same never-overwrite policy as variables/custom expressions.
        /// Each added block gets its own staircased position for the same reason as custom
        /// expressions above.
        /// </summary>
        private static int AddMissingCustomInstructions(XElement root, List<XElement> instructionBlocks, int anchorX, int anchorY)
        {
            if (instructionBlocks == null || instructionBlocks.Count == 0)
                return 0;

            HashSet<string> existingNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement block in root.Elements("Instructions"))
            {
                XElement head = block.Elements().FirstOrDefault();
                if (head != null && head.Name.LocalName == "CustomInstruction")
                {
                    string existingName = (string)head.Attribute("name");
                    if (existingName != null)
                        existingNames.Add(existingName);
                }
            }

            int added = 0;
            foreach (XElement block in instructionBlocks)
            {
                XElement head = block.Elements().FirstOrDefault();
                string name = head != null && head.Name.LocalName == "CustomInstruction" ? (string)head.Attribute("name") : null;
                if (string.IsNullOrEmpty(name) || existingNames.Contains(name))
                    continue;

                XElement copy = new XElement(block);
                StripIds(copy);
                XElement copyHead = copy.Elements().FirstOrDefault();
                if (copyHead != null)
                {
                    (int x, int y) = NextStaircasePosition(root, anchorX, anchorY);
                    SetPosition(copyHead, x, y);
                }
                root.Add(copy);
                existingNames.Add(name);
                added++;
            }

            return added;
        }
    }
}
