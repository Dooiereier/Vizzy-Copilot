namespace Assets.Scripts
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Assets.Scripts.Craft.FlightData;
    using Assets.Scripts.Vizzy.UI;
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Craft;
    using ModApi.Craft.Program;
    using ModApi.Craft.Program.Expressions;
    using ModApi.Craft.Program.Instructions;
    using ModApi.Expressions;
    using UnityEngine;

    /// <summary>
    /// The names a Vizzy Funk field can use, read from the game so they can't drift from it. Auto-fill
    /// and the coloring's error marks share these. Only results Vizzy can use count: FD.AccelerationFrame
    /// compiles, then stops a running program.
    /// </summary>
    public static class FunkNames
    {
        private static readonly Dictionary<string, string> KnownSignatures = new Dictionary<string, string>
        {
            // The stateful functions and Vizzy's list functions have no parameter names to read.
            { "PID", "PID(target, current, p, i, d)" },
            { "smooth", "smooth(value, maxRate)" },
            { "sum", "sum(value)" },
            { "rate", "rate(value)" },
            { "listLen", "listLen(\"name\")" },
            { "listNum", "listNum(\"name\", index)" },
            { "listBool", "listBool(\"name\", index)" },
            { "listStr", "listStr(\"name\", index)" },
            { "listVec", "listVec(\"name\", index)" },
        };

        private static List<FunkName> _functions;
        private static List<FunkName> _constants;
        private static List<FunkName> _controls;
        private static List<FunkName> _flightData;
        private static List<FunkName> _orbitData;

        public static List<FunkName> Functions => _functions ?? (_functions = ReadFunctions());

        public static List<FunkName> Constants => _constants ?? (_constants = ReadConstants());

        public static List<FunkName> Controls => _controls ?? (_controls = ValueProperties(typeof(CraftControls)));

        public static List<FunkName> FlightData => _flightData ?? (_flightData = ValueProperties(typeof(CraftFlightData)));

        public static List<FunkName> OrbitData => _orbitData ?? (_orbitData = ValueProperties(typeof(CraftOrbitData)));

        /// <summary>
        /// Every variable name the open program has: its list, and any name typed straight into a block, which
        /// the game creates when the block runs without listing it. Reads the editor's live blocks, because its
        /// program tree only catches up at the next undo step. Also For variables and custom block parameters.
        /// </summary>
        public static HashSet<string> ProgramVariables(VizzyUIScript editor)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var program = editor.FlightProgram;
            foreach (var variable in program.GlobalVariables.Variables)
            {
                names.Add(variable.Name);
            }

            foreach (var block in editor.ProgramTransform.GetComponentsInChildren<BlockElementScript>(true))
            {
                AddVariableOf(block.Node, names);
            }

            foreach (var root in AllRoots(program))
            {
                Walk(root, node => AddVariableOf(node, names));
            }

            foreach (var custom in program.CustomInstructions)
            {
                names.UnionWith(custom.LocalVariables.Select(local => local.Name));
            }

            foreach (var custom in program.CustomExpressions)
            {
                names.UnionWith(custom.LocalVariables.Select(local => local.Name));
            }

            names.Remove(string.Empty);
            return names;
        }

        /// <summary>The roots of everything a program runs.</summary>
        public static IEnumerable<ProgramNode> AllRoots(FlightProgram program)
        {
            return program.RootInstructions.Cast<ProgramNode>()
                .Concat(program.RootExpressions)
                .Concat(program.CustomInstructions)
                .Concat(program.CustomExpressions);
        }

        /// <summary>
        /// Visits a node and everything under and after it. Not ProgramNode.ExecuteActionOnTree: that throws on a
        /// node without an expression list.
        /// </summary>
        public static void Walk(ProgramNode node, Action<ProgramNode> visit)
        {
            if (node == null)
            {
                return;
            }

            visit(node);
            if (node.Expressions != null)
            {
                foreach (var expression in node.Expressions)
                {
                    Walk(expression, visit);
                }
            }

            if (node is ProgramInstruction instruction)
            {
                Walk(instruction.FirstChild, visit);
                Walk(instruction.Next, visit);
            }
        }

        /// <summary>A variable name typed into a set, change or user input block's name slot, or null.</summary>
        public static string TypedVariableName(ProgramNode node)
        {
            var typed = node is SetVariableInstruction || node is ChangeVariableInstruction || node is UserInputInstruction;
            return typed && node.Expressions != null && node.Expressions.Count > 0 && node.Expressions[0] is ConstantExpression name
                ? name.ExpressionResult.TextValue
                : null;
        }

        private static void AddVariableOf(ProgramNode node, HashSet<string> names)
        {
            if (node is VariableExpression variable && variable.VariableName != null)
            {
                names.Add(variable.VariableName);
            }
            else if (node is ForInstruction loop && loop.VariableName != null)
            {
                names.Add(loop.VariableName);
            }
            else if (TypedVariableName(node) != null)
            {
                names.Add(TypedVariableName(node));
            }
        }

        /// <summary>Non-public in the game's Context; reading them keeps these lists matching the game's.</summary>
        public static IDictionary ContextMember(Context context, string name)
        {
            return (IDictionary)typeof(Context)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(context);
        }

        private static List<FunkName> ReadFunctions()
        {
            var names = new List<FunkName>();
            foreach (DictionaryEntry function in ContextMember(new Context(true), "Functions"))
            {
                var method = (MethodInfo)function.Value.GetType().GetField("Item1").GetValue(function.Value);
                var parameters = method.GetParameters().Select(parameter => parameter.Name);
                var name = (string)function.Key;
                if (!KnownSignatures.ContainsKey(name))
                {
                    names.Add(new FunkName(name, name + "(" + string.Join(", ", parameters) + ")"));
                }
            }

            names.AddRange(KnownSignatures.Select(known => new FunkName(known.Key, known.Value)));
            return names;
        }

        private static List<FunkName> ReadConstants()
        {
            var names = new List<FunkName>();
            foreach (DictionaryEntry constant in ContextMember(new Context(true), "Constants"))
            {
                names.Add(new FunkName((string)constant.Key));
            }

            return names;
        }

        private static List<FunkName> ValueProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => IsUsableInVizzy(property.PropertyType))
                .Select(property => new FunkName(property.Name))
                .ToList();
        }

        private static bool IsUsableInVizzy(Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(bool) || type == typeof(int) || type == typeof(short)
                || type == typeof(Vector3d);
        }
    }
}
