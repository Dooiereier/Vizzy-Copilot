using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Assets.Scripts.Exporter
{
    /// <summary>
    /// Finds which custom expressions/instructions a set of nodes calls, and locates their
    /// real definitions in the source program so they can be recreated in a target file that
    /// doesn't have them yet - the same idea as VariableDependencyResolver, for custom blocks.
    ///
    /// Schema confirmed by reading real saved program files directly (not documented in
    /// kroryan/VizzyCode's converter):
    /// - A call looks like &lt;CallCustomExpression call="Name" ...&gt; (args as children) or
    ///   &lt;CallCustomInstruction call="Name" ... /&gt;.
    /// - A custom expression *definition* is a &lt;CustomExpression name="Name" ...&gt; entry
    ///   directly in the program's single shared &lt;Expressions&gt; container, same as any
    ///   other standalone expression - its own expression tree is nested directly inside it
    ///   (a custom expression returns one value from one expression tree).
    /// - A custom instruction *definition* is different: &lt;CustomInstruction name="Name" .../&gt;
    ///   is a self-closing head element - its body is NOT nested inside it. It's the *first*
    ///   child of its own top-level &lt;Instructions&gt; block (a sibling of every other
    ///   top-level block, same as how an &lt;Event&gt; is the head of an "on start" column),
    ///   and everything after it in that same &lt;Instructions&gt; block, flat, is its body -
    ///   exactly like any other instruction chain. So "the definition" for export purposes is
    ///   the whole &lt;Instructions&gt; block, not just the CustomInstruction element itself.
    /// </summary>
    internal static class CustomBlockDependencyResolver
    {
        /// <summary>Distinct custom expression/instruction names called anywhere in the given nodes.</summary>
        public static void FindReferencedNames(IEnumerable<XElement> nodes, out List<string> expressionNames, out List<string> instructionNames)
        {
            HashSet<string> exprNames = new HashSet<string>(StringComparer.Ordinal); // Vizzy is case-sensitive
            HashSet<string> instrNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (XElement node in nodes)
            {
                IEnumerable<XElement> candidates = new[] { node }.Concat(node.Descendants());
                foreach (XElement element in candidates)
                {
                    string name;
                    switch (element.Name.LocalName)
                    {
                        case "CallCustomExpression":
                            name = (string)element.Attribute("call");
                            if (!string.IsNullOrEmpty(name))
                                exprNames.Add(name);
                            break;
                        case "CallCustomInstruction":
                            name = (string)element.Attribute("call");
                            if (!string.IsNullOrEmpty(name))
                                instrNames.Add(name);
                            break;
                    }
                }
            }

            expressionNames = exprNames.ToList();
            instructionNames = instrNames.ToList();
        }

        /// <summary>The CustomExpression's own definition element (with its body nested inside), from the source's Expressions container.</summary>
        public static XElement ResolveCustomExpressionDefinition(string name, XElement sourceProgramXml)
        {
            return sourceProgramXml?.Element("Expressions")?
                .Elements("CustomExpression")
                .FirstOrDefault(e => (string)e.Attribute("name") == name);
        }

        /// <summary>The CustomInstruction's containing top-level Instructions block (head element + its whole body chain), from the source program XML.</summary>
        public static XElement ResolveCustomInstructionBlock(string name, XElement sourceProgramXml)
        {
            if (sourceProgramXml == null)
                return null;

            foreach (XElement block in sourceProgramXml.Elements("Instructions"))
            {
                XElement head = block.Elements().FirstOrDefault();
                if (head != null && head.Name.LocalName == "CustomInstruction" && (string)head.Attribute("name") == name)
                    return block;
            }

            return null;
        }
    }
}
