using Assets.Scripts.Vizzy;
using Assets.Scripts.Vizzy.UI;
using HarmonyLib;
using ModApi.Craft.Program;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using ModApi.Ui;
using UnityEngine;

namespace Assets.Scripts
{
    public partial class Mod
    {
        private static readonly Dictionary<string, (Type, Func<ProgramNode>)> ModNodes = new()
        {
            ["Matrix3x3Mul"] = (typeof(Matrix3x3MulExpression), () => new Matrix3x3MulExpression()),
            ["Matrix3x3GetElement"] = (typeof(Matrix3x3GetElementExpression), () => new Matrix3x3GetElementExpression()),
            ["Matrix3x3SetElement"] = (typeof(Matrix3x3SetElementExpression), () => new Matrix3x3SetElementExpression()),
            ["Matrix3x3Normalize"] = (typeof(Matrix3x3NormalizeExpression), () => new Matrix3x3NormalizeExpression()),
        };

        private static readonly XNamespace xNamespace = XmlLayoutConstants.XmlNamespace;
        private static void OnBuildViewPanel(BuildUserInterfaceXmlRequest request)
        {
            var cameraPanelButton =
                request.XmlDocument.Descendants(xNamespace + "ContentButton")
                    .FirstOrDefault(n => n.Attribute("id")?.Value == "toggle-camera-panel-button");

            if (cameraPanelButton != null)
            {
                cameraPanelButton.AddAfterSelf(
                    XElement.Parse(
                        $"<ContentButton name=\"EditVizzyButton\" id=\"edit-vizzy\" class=\"view-button audio-btn-click\" tooltip=\"Edit Vizzy\" xmlns=\"{xNamespace}\">" +
                        "    <Image sprite=\"VizzyDebugger/Sprites/IconVariables\" />" +
                        "</ContentButton>"
                    )
                );
                cameraPanelButton.AddAfterSelf(
                    XElement.Parse(
                        $"<ContentButton name=\"RestartVizzyButton\" id=\"restart-vizzy\" class=\"view-button audio-btn-click\" tooltip=\"Restart Vizzy\" xmlns=\"{xNamespace}\">" +
                        "    <Image sprite=\"VizzyDebugger/Sprites/IconResetStaging\" />" +
                        "</ContentButton>"
                    )
                );
            }
        }
        /// <summary>
        /// 反射注册自定义 Vizzy 节点到 ProgramSerializer 的内部查找表
        /// （与 AdvancedVizzy 相同的注册方式）。
        /// </summary>
        private void RegisterModVizzyNode()
        {
            var programNodeCreatorType = AccessTools.Inner(typeof(ProgramSerializer), "ProgramNodeCreator");
            if (programNodeCreatorType == null)
            {
                Debug.LogError("Registration failed: Could not find ProgramNodeCreator type");
                return;
            }

            var creatorConstructor =
                programNodeCreatorType.GetConstructor(new[]
                    { typeof(string), typeof(Type), typeof(Func<ProgramNode>) });
            if (creatorConstructor == null)
            {
                Debug.LogError("Registration failed: Could not find ProgramNodeCreator constructor");
                return;
            }

            // 访问序列化器内部字典（读取静态字段会先触发静态构造函数，内置节点已就绪）
            var typeNameLookup =
                AccessTools.Field(typeof(ProgramSerializer), "_typeNameLookup")?.GetValue(null) as IDictionary;
            var xmlNameLookup =
                AccessTools.Field(typeof(ProgramSerializer), "_xmlNameLookup")?.GetValue(null) as IDictionary;

            if (typeNameLookup == null || xmlNameLookup == null)
            {
                Debug.LogError("Registration failed: Could not get internal dictionaries of the serializer");
                return;
            }

            foreach (var (xmlName, (nodeType, constructor)) in ModNodes)
            {
                var creator = creatorConstructor.Invoke(new object[] { xmlName, nodeType, constructor });
                typeNameLookup[nodeType.Name] = creator;
                xmlNameLookup[xmlName] = creator;
                Debug.Log("Registered custom Vizzy node: " + xmlName + " -> " + nodeType.Name);
            }
        }

        /// <summary>
        /// 在 Vizzy 工具箱 XML 构造时注入 Matrix 分类、样式和 4 个矩阵节点
        /// （与 AdvancedVizzy 相同的工具箱注入方式）。
        /// </summary>
        [HarmonyPatch(typeof(VizzyToolbox))]
        [HarmonyPatch(MethodType.Constructor)]
        [HarmonyPatch(new Type[] { typeof(XElement), typeof(bool) })]
        public static class VizzyToolboxCtorPatch
        {
            private const string CategoryName = "Matrix";
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
                    stylesElement.Add(Style(ns, "Matrix3x3Mul", "Matrix Multiply: A={0}, B={1}",
                        "3x3 matrix multiplication. Both inputs are 9-element row-major lists [m00..m22]."));
                    stylesElement.Add(Style(ns, "Matrix3x3GetElement", "Get Matrix Element: M={0}, Row={1}, Col={2}",
                        "Returns element M[row][col]. Row and col are 0-based."));
                    stylesElement.Add(Style(ns, "Matrix3x3SetElement", "Set Matrix Element: M={0}, Row={1}, Col={2}, Value={3}",
                        "Returns a copy of M with element [row][col] replaced by Value."));
                    stylesElement.Add(Style(ns, "Matrix3x3Normalize", "Normalize Matrix: M={0}, Mode={1}",
                        "Mode 0: normalize columns, 1: normalize rows, 2: Frobenius scale to length 1."));

                    // Category
                    XElement categoriesElement = xml.Element(ns + "Categories");
                    if (categoriesElement == null)
                    {
                        categoriesElement = new XElement(ns + "Categories");
                        xml.Add(categoriesElement);
                    }
                    XElement category = categoriesElement.Elements(ns + "Category")
                        .FirstOrDefault(x => (string)x.Attribute("name") == CategoryName);
                    if (category == null)
                    {
                        category = new XElement(ns + "Category", new XAttribute("name", CategoryName));
                        categoriesElement.Add(category);
                    }

                    // Nodes（默认输入：单位矩阵 ListOp 常量 / 数字常量 0）
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
                // style="list-create" 是游戏原版 ListOp 节点的样式，缺了它工具箱渲染会 NRE
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
}
