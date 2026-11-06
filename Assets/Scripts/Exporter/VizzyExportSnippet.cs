using Assets.Scripts.Vizzy.UI.Elements;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace Assets.Scripts.Exporter
{
    /// <summary>Which family of Vizzy node a captured snippet holds.</summary>
    public enum VizzyNodeKind
    {
        Instruction,
        Expression
    }

    /// <summary>
    /// A block (or, for instructions, the whole column of blocks chained below it) captured
    /// from the editor, ready to be written into another program file. Nodes are kept as
    /// XElements in memory - there's no clipboard/round-trip step, since export happens in
    /// one right-click-to-file-pick motion.
    /// </summary>
    public class VizzyExportSnippet
    {
        public VizzyNodeKind Kind;
        public List<XElement> Nodes = new List<XElement>();

        /// <summary>
        /// Real definitions (from the source program's own live Variables list) for every
        /// global variable this snippet - or any custom expression/instruction pulled in
        /// below - references, best-effort. A name that isn't resolved here (source program
        /// couldn't be serialized, or the variable genuinely wasn't found) is simply absent -
        /// see VariableDependencyResolver for why we don't guess a replacement.
        /// </summary>
        public List<XElement> ReferencedGlobalVariableDefinitions = new List<XElement>();

        /// <summary>Real CustomExpression definitions (with their body) for every custom expression called anywhere in this snippet, transitively.</summary>
        public List<XElement> ReferencedCustomExpressionDefinitions = new List<XElement>();

        /// <summary>Real containing Instructions blocks (head CustomInstruction + its whole body chain) for every custom instruction called anywhere in this snippet, transitively.</summary>
        public List<XElement> ReferencedCustomInstructionBlocks = new List<XElement>();

        /// <summary>
        /// Captures a right-clicked block. For an instruction block, SerializerBridge is
        /// asked to serialize the right-clicked node's whole chain - the game's own
        /// serializer walks InstructionElementScript.NextInstruction (the rest of the column
        /// below it) itself, and recursively serializes every nested control-flow body along
        /// the way (loop/if bodies are chains too, one level down) - so a right-click on any
        /// block in a column captures that block plus everything below and nested inside it.
        /// </summary>
        public static bool TryCapture(BlockElementScript block, out VizzyExportSnippet snippet, out string error)
        {
            snippet = null;
            error = null;

            if (block == null || block.Node == null)
            {
                error = Text.Get("VizzyStudio.Export.ClickBlockFirst");
                return false;
            }

            try
            {
                List<XElement> nodes;

                if (block is InstructionElementScript)
                {
                    nodes = SerializerBridge.SerializeNodeChain(block.Node);
                    snippet = new VizzyExportSnippet { Kind = VizzyNodeKind.Instruction, Nodes = nodes };
                }
                else
                {
                    nodes = new List<XElement> { SerializerBridge.SerializeNode(block.Node) };
                    snippet = new VizzyExportSnippet { Kind = VizzyNodeKind.Expression, Nodes = nodes };
                }

                CaptureDependencies(block, snippet);

                return true;
            }
            catch (Exception ex)
            {
                error = Text.Get("VizzyStudio.Export.ReadFailed", ex.Message);
                Debug.LogError($"[VizzyStudio] {error}\n{ex}");
                return false;
            }
        }

        /// <summary>
        /// Best-effort: figures out which global variables and custom expressions/
        /// instructions the snippet needs, transitively - a pulled-in custom block's own body
        /// can itself call further custom blocks or variables, so this is a worklist over
        /// "newly pulled-in definitions" until nothing new turns up, not a single pass over
        /// just the snippet's own nodes. Never fails the whole capture - worst case, the
        /// Referenced* lists just come back empty and export proceeds without auto-creating
        /// anything (same as before this feature existed).
        /// </summary>
        private static void CaptureDependencies(BlockElementScript block, VizzyExportSnippet snippet)
        {
            try
            {
                ModApi.Craft.Program.FlightProgram flightProgram = block.VizzyUI?.FlightProgram;
                XElement fullProgramXml = SerializerBridge.TrySerializeFlightProgram(flightProgram);
                if (fullProgramXml == null)
                    return;

                HashSet<string> resolvedVariableNames = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> resolvedExpressionNames = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> resolvedInstructionNames = new HashSet<string>(StringComparer.Ordinal);

                // Starts with the snippet's own nodes; each round's frontier is whatever
                // custom block bodies were newly pulled in last round, so their own
                // references get scanned too. Ends when a round finds nothing new.
                List<XElement> frontier = new List<XElement>(snippet.Nodes);
                int guard = 0;

                while (frontier.Count > 0 && guard++ < 200)
                {
                    foreach (string name in VariableDependencyResolver.FindReferencedGlobalVariableNames(frontier))
                        resolvedVariableNames.Add(name);

                    CustomBlockDependencyResolver.FindReferencedNames(frontier, out List<string> exprNames, out List<string> instrNames);

                    List<XElement> nextFrontier = new List<XElement>();

                    foreach (string name in exprNames)
                    {
                        if (!resolvedExpressionNames.Add(name))
                            continue; // already resolved (or being resolved) this capture

                        XElement definition = CustomBlockDependencyResolver.ResolveCustomExpressionDefinition(name, fullProgramXml);
                        if (definition != null)
                        {
                            snippet.ReferencedCustomExpressionDefinitions.Add(definition);
                            nextFrontier.Add(definition);
                        }
                    }

                    foreach (string name in instrNames)
                    {
                        if (!resolvedInstructionNames.Add(name))
                            continue;

                        XElement instructionBlock = CustomBlockDependencyResolver.ResolveCustomInstructionBlock(name, fullProgramXml);
                        if (instructionBlock != null)
                        {
                            snippet.ReferencedCustomInstructionBlocks.Add(instructionBlock);
                            nextFrontier.Add(instructionBlock);
                        }
                    }

                    frontier = nextFrontier;
                }

                snippet.ReferencedGlobalVariableDefinitions =
                    VariableDependencyResolver.ResolveDefinitions(resolvedVariableNames, fullProgramXml);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VizzyStudio] Couldn't determine referenced variables/custom blocks (export will still proceed): {ex.Message}");
            }
        }
    }
}
