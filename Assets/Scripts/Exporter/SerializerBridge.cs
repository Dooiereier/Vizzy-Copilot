using ModApi.Craft.Program;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Assets.Scripts.Exporter
{
    /// <summary>
    /// The one piece of this mod that used to reach a non-public game member. Confirmed
    /// directly against the game's own assembly (see docs/IMPLEMENTATION_NOTES.md) - the
    /// real method is public, just named/shaped differently than joaofarias's Vizzy Studio mod's source
    /// (written against an older game version) suggested.
    /// </summary>
    internal static class SerializerBridge
    {
        /// <summary>
        /// Serializes a single node - with its own arguments/expressions, but not the rest
        /// of the block chain below it - into an XElement, exactly like the game does when
        /// saving a program to disk. Used for lone-expression exports, which have no chain.
        /// </summary>
        public static XElement SerializeNode(ProgramNode node)
        {
            List<XElement> results = SerializeInternal(node, cloneChain: false);
            XElement result = results.FirstOrDefault();
            if (result == null)
            {
                throw new InvalidOperationException(
                    "ProgramSerializer.SerializeProgramNodes produced no output for this node.");
            }

            return result;
        }

        /// <summary>
        /// Serializes a node together with the rest of its instruction chain - the blocks
        /// connected below it - AND every nested body chain (a While/If/Repeat/For body is
        /// itself a chain, one level down), exactly like the game does when saving a program
        /// to disk. cloneChain turned out to govern *all* chain-walking, not just the
        /// top-level "next block in the column" one: with it false, every nested control-flow
        /// body came out empty (confirmed by inspecting real exported XML - every While/If
        /// element had only its condition expression, no nested Instructions at all, at any
        /// depth). So the whole chain - top-level and nested - is captured in one call here
        /// instead of VizzyExportSnippet walking NextInstruction itself node-by-node.
        /// </summary>
        public static List<XElement> SerializeNodeChain(ProgramNode headNode)
        {
            List<XElement> results = SerializeInternal(headNode, cloneChain: true);
            if (results.Count == 0)
            {
                throw new InvalidOperationException(
                    "ProgramSerializer.SerializeProgramNodes produced no output for this chain.");
            }

            return results;
        }

        private static List<XElement> SerializeInternal(ProgramNode node, bool cloneChain)
        {
            XElement scratchParent = new XElement("Scratch");

            // instructionId is a ref counter the game uses to hand out unique ids across a
            // serialization run. Resetting it to 0 per call (rather than threading one
            // counter across every capture in a session) is fine here: every id gets
            // stripped right back off by VizzyProgramFileExporter.StripIds before anything
            // is written into the target file, since ids only need to be unique within that
            // file.
            int instructionId = 0;
            ProgramSerializer.SerializeProgramNodes(node, scratchParent, ref instructionId, cloneChain);
            return scratchParent.Elements().ToList();
        }

        /// <summary>
        /// Serializes the *entire current program* (all instructions, expressions, and -
        /// what we actually want it for - the live, accurate list of global variable
        /// definitions) exactly as the game would when saving. Returns null rather than
        /// throwing if this isn't available, since callers use it for a nice-to-have
        /// (auto-creating missing variables), not for the core export path.
        ///
        /// IProgramSerializer.SerializeFlightProgram is an instance method, and the only
        /// implementation (ProgramSerializer) has just a private parameterless constructor
        /// and no singleton accessor anywhere in the game's API - so a throwaway instance is
        /// constructed via reflection each call. It's stateless, so this is safe.
        /// </summary>
        public static XElement TrySerializeFlightProgram(FlightProgram program)
        {
            if (program == null)
                return null;

            try
            {
                IProgramSerializer serializer =
                    (IProgramSerializer)Activator.CreateInstance(typeof(ProgramSerializer), nonPublic: true);
                return serializer.SerializeFlightProgram(program);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    $"[VizzyStudio] Couldn't serialize the current program to look up variable definitions " +
                    $"(missing global variables won't be auto-created in the target file this time): {ex.Message}");
                return null;
            }
        }
    }
}
