using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Assets.Scripts.Exporter
{
    /// <summary>
    /// Finds which global variables a captured snippet touches, and locates their real
    /// definitions in the source program so they can be recreated in a target file that
    /// doesn't have them yet.
    ///
    /// Schema confirmed against kroryan/VizzyCode's VizzyXmlConverter.cs, a published
    /// bidirectional converter for this exact game's Vizzy XML format:
    /// - A variable *reference* inside an instruction/expression tree looks like
    ///   &lt;Variable list="false" local="false" variableName="X" /&gt;.
    /// - A variable *definition* under the program's &lt;Variables&gt; container looks like
    ///   &lt;Variable name="X" number="0" /&gt; (or a nested &lt;Constant text="_"/&gt; for
    ///   strings, style/bool attributes for booleans, or a nested &lt;Items/&gt; for lists).
    /// The two forms are easy to tell apart even without that context: only a definition
    /// carries a "name" attribute, only a reference carries "variableName".
    /// </summary>
    internal static class VariableDependencyResolver
    {
        /// <summary>
        /// Distinct global variable names referenced anywhere in the snippet's nodes.
        /// Local variables (local="true" - a For loop counter, a SetLocalVariable block,
        /// a custom instruction's own parameter) are deliberately excluded: they're created
        /// by the instruction that owns them wherever the snippet is pasted, not by a
        /// persistent global variable list.
        /// </summary>
        public static List<string> FindReferencedGlobalVariableNames(IEnumerable<XElement> nodes)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal); // Vizzy is case-sensitive

            foreach (XElement node in nodes)
            {
                IEnumerable<XElement> candidates = new[] { node }.Concat(node.Descendants());
                foreach (XElement variableRef in candidates.Where(e => e.Name.LocalName == "Variable"))
                {
                    string variableName = (string)variableRef.Attribute("variableName");
                    if (string.IsNullOrEmpty(variableName))
                        continue; // no variableName attribute -> this is a definition shape, not a reference

                    bool isLocal = (string)variableRef.Attribute("local") == "true";
                    if (isLocal)
                        continue;

                    names.Add(variableName);
                }
            }

            return names.ToList();
        }

        /// <summary>
        /// Looks up each referenced global variable's real definition XElement in the
        /// source program's full serialized XML (see SerializerBridge.TrySerializeFlightProgram).
        /// Returns one entry per name that was actually found - callers should treat any
        /// requested name missing from the result as "couldn't determine, skip it" rather
        /// than synthesizing a guessed default, since a wrong-typed variable can be worse
        /// than a missing one.
        /// </summary>
        public static List<XElement> ResolveDefinitions(IEnumerable<string> variableNames, XElement sourceProgramXml)
        {
            List<XElement> definitions = new List<XElement>();
            if (sourceProgramXml == null)
                return definitions;

            XElement sourceVariables = sourceProgramXml.Element("Variables");
            if (sourceVariables == null)
                return definitions;

            foreach (string name in variableNames)
            {
                XElement match = sourceVariables.Elements("Variable")
                    .FirstOrDefault(v => (string)v.Attribute("name") == name);

                if (match != null)
                    definitions.Add(match);
            }

            return definitions;
        }
    }
}
