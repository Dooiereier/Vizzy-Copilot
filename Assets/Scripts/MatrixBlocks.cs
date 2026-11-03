namespace Assets.Scripts
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Vizzy;
    using Assets.Scripts.Vizzy.UI;
    using HarmonyLib;
    using ModApi.Craft;
    using ModApi.Craft.Program;
    using UnityEngine;

    /// <summary>
    /// Matrix blocks for Vizzy. A program using one can't load without this mod, because the game treats
    /// an unknown block as an error, so a craft with one requires the mod.
    /// </summary>
    public static class MatrixBlocks
    {
        private static readonly Dictionary<string, (Type, Func<ProgramNode>)> Nodes = new Dictionary<string, (Type, Func<ProgramNode>)>
        {
            ["Matrix3x3Mul"] = (typeof(Matrix3x3MulExpression), () => new Matrix3x3MulExpression()),
            ["Matrix3x3GetElement"] = (typeof(Matrix3x3GetElementExpression), () => new Matrix3x3GetElementExpression()),
            ["Matrix3x3SetElement"] = (typeof(Matrix3x3SetElementExpression), () => new Matrix3x3SetElementExpression()),
            ["Matrix3x3Normalize"] = (typeof(Matrix3x3NormalizeExpression), () => new Matrix3x3NormalizeExpression()),
        };

        /// <summary>Whether any program on the craft uses a Matrix block.</summary>
        public static bool IsUsedBy(CraftData craft)
        {
            try
            {
                return craft?.Assembly != null && craft.Assembly.Parts
                    .Select(part => part.GetModifier<FlightProgramData>()?.FlightProgramXml)
                    .Where(program => program != null)
                    .Any(program => program.Descendants().Any(element => Nodes.ContainsKey(element.Name.LocalName)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VizzyStudio] Couldn't check a craft for Matrix blocks: " + e.Message);
                return false;
            }
        }

        /// <summary>Adds the blocks to the serializer's private lookup tables. Without this they can't load or save.</summary>
        public static void Register()
        {
            var creatorType = AccessTools.Inner(typeof(ProgramSerializer), "ProgramNodeCreator");
            var constructor = creatorType.GetConstructor(new[] { typeof(string), typeof(Type), typeof(Func<ProgramNode>) });
            var typeNames = (IDictionary)AccessTools.Field(typeof(ProgramSerializer), "_typeNameLookup").GetValue(null);
            var xmlNames = (IDictionary)AccessTools.Field(typeof(ProgramSerializer), "_xmlNameLookup").GetValue(null);
            foreach (var node in Nodes)
            {
                var creator = constructor.Invoke(new object[] { node.Key, node.Value.Item1, node.Value.Item2 });
                typeNames[node.Value.Item1.Name] = creator;
                xmlNames[node.Key] = creator;
            }
        }
    }

    /// <summary>Adds the Matrix styles and blocks to the Vizzy toolbox.</summary>
    [HarmonyPatch(typeof(VizzyToolbox))]
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof(XElement), typeof(bool) })]
    public static class MatrixToolboxPatch
    {
        private const string ColorId = "MatrixNodeColor";

        private static void Prefix(ref XElement xml, ref bool showMfdCategory)
        {
            try
            {
                XNamespace ns = xml.Name.Namespace;

                // Colors
                XElement colorsElement = xml.Element(ns + "Colors");
                if (colorsElement == null)
                {
                    colorsElement = new XElement(ns + "Colors");
                    xml.Add(colorsElement);
                }
                colorsElement.Add(new XElement(ns + "Color",
                    new XAttribute("id", ColorId),
                    new XAttribute("color", "#4C9FEB")));

                // Styles
                XElement stylesElement = xml.Element(ns + "Styles");
                if (stylesElement == null)
                {
                    stylesElement = new XElement(ns + "Styles");
                    xml.Add(stylesElement);
                }
                stylesElement.Add(Style(ns, "Matrix3x3Mul", "Matrix Multiply A=(0), B=(1)",
                    "3x3 matrix multiplication. Both inputs are 9-element row-major lists [m00..m22].\nVizzy Studio mod required"));
                stylesElement.Add(Style(ns, "Matrix3x3GetElement", "Get Matrix Element M=(0), Row=(1:Number), Col=(2:Number)",
                    "Returns element M[row][col]. Row and col are 0-based.\nVizzy Studio mod required"));
                stylesElement.Add(Style(ns, "Matrix3x3SetElement", "Set Matrix Element M=(0), Row=(1:Number), Col=(2:Number), Value=(3:Number)",
                    "Returns a copy of M with element [row][col] replaced by Value.\nVizzy Studio mod required"));
                stylesElement.Add(Style(ns, "Matrix3x3Normalize", "Normalize Matrix M=(0), Mode=(1:Number)",
                    "Mode 0: normalize columns, 1: normalize rows, 2: Frobenius scale to length 1.\nVizzy Studio mod required"));

                // At the bottom of Operators, below the false block.
                XElement category = xml.Element(ns + "Categories").Elements(ns + "Category")
                    .First(x => (string)x.Attribute("name") == "Operators");

                // Nodes. Default inputs: an identity matrix ListOp constant, and the number constant 0.
                category.Add(
                    Node(ns, "Matrix3x3Mul", Identity(ns), Identity(ns)),
                    Node(ns, "Matrix3x3GetElement", Identity(ns), Number(ns, 0), Number(ns, 0)),
                    Node(ns, "Matrix3x3SetElement", Identity(ns), Number(ns, 0), Number(ns, 0), Number(ns, 0)),
                    Node(ns, "Matrix3x3Normalize", Identity(ns), Number(ns, 0)));
            }
            catch (Exception e)
            {
                Debug.LogError("VizzyToolboxCtorPatch failed: " + e);
            }
        }

        private static XElement Style(XNamespace ns, string id, string format, string tooltip)
        {
            return new XElement(ns + "Style",
                new XAttribute("id", id),
                new XAttribute("color", ColorId),
                new XAttribute("format", format),
                new XAttribute("tooltip", tooltip));
        }

        private static XElement Node(XNamespace ns, string name, params XElement[] children)
        {
            XElement node = new XElement(ns + name, new XAttribute("style", name));
            foreach (XElement child in children)
            {
                node.Add(child);
            }
            return node;
        }

        private static XElement Identity(XNamespace ns)
        {
            // "list-create" is the stock ListOp style; without it the toolbox hits a null reference when rendering.
            return new XElement(ns + "ListOp",
                new XAttribute("style", "list-create"),
                new XAttribute("op", "create"),
                new XElement(ns + "Constant", new XAttribute("text", "1,0,0,0,1,0,0,0,1")));
        }

        private static XElement Number(XNamespace ns, double value)
        {
            return new XElement(ns + "Constant", new XAttribute("number", value));
        }
    }
}
