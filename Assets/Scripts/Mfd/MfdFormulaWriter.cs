namespace Assets.Scripts.Mfd
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;

    public enum MfdProperty
    {
        Position,
        Size,
        Rotation,
    }

    /// <summary>A widget's new value. Rotation only uses X.</summary>
    public sealed class MfdChange
    {
        public string Widget;
        public MfdProperty Property;
        public double X;
        public double Y;
    }

    /// <summary>What happened to one property, or one axis of it.</summary>
    public sealed class MfdLine
    {
        public string Widget;
        public MfdProperty Property;
        public bool Applied;

        /// <summary>Whether the change reaches other readers of a shared variable.</summary>
        public bool Shared;

        /// <summary>Whether it was refused only because a variable is shared, which the override would allow.</summary>
        public bool NeedsSharedOverride;

        /// <summary>Old and new text when applied, the reason when not.</summary>
        public string Detail;

        public override string ToString()
        {
            return this.Widget + " " + this.Property.ToString().ToLowerInvariant() + ": " + this.Detail;
        }
    }

    /// <summary>
    /// Writes MFD widget values into a program's XML: a block's literal numbers, the one number in a
    /// formula made of blocks or Funk, or the argument of a helper custom instruction. It refuses rather
    /// than guesses: nothing else in the program may change, and a formula is only rewritten when one
    /// number decides it. Several blocks setting a widget to the same value change together.
    /// </summary>
    public static class MfdFormulaWriter
    {
        private const int MaxVariableChain = 4;

        /// <summary>Changes the given program element and says what it did for each change.</summary>
        public static List<MfdLine> Apply(XElement program, IEnumerable<MfdChange> changes, double screenWidth, double screenHeight, bool allowSharedVariables)
        {
            var run = new Run(program, screenWidth, screenHeight, allowSharedVariables);
            foreach (var change in changes)
            {
                run.ApplyOne(change);
            }

            return run.Lines;
        }

        public static string BlockProperty(MfdProperty property)
        {
            switch (property)
            {
                case MfdProperty.Position: return "Mfd.SetPosition";
                case MfdProperty.Size: return "Mfd.SetSize";
                default: return "Mfd.SetRotation";
            }
        }

        private static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static bool IsPlainNumber(XElement element)
        {
            if (element.Name != "Constant")
            {
                return false;
            }

            var attribute = element.Attribute("number") ?? element.Attribute("text");
            return attribute != null && double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private static string NumberText(XElement constant)
        {
            return (constant.Attribute("number") ?? constant.Attribute("text")).Value;
        }

        private static void SetNumber(XElement constant, double value)
        {
            (constant.Attribute("number") ?? constant.Attribute("text")).Value = Format(value);
        }

        private static List<string> ParameterNames(string format)
        {
            var parts = format.Split('|');
            var names = new List<string>();
            for (var i = 1; i < parts.Length; i += 2)
            {
                names.Add(parts[i].Trim());
            }

            return names;
        }

        private sealed class Helper
        {
            public string Name;
            public int NameParameter;
            public List<string> Parameters;

            /// <summary>The value when it's one parameter passed on unchanged, otherwise -1.</summary>
            public int ValueParameter = -1;

            /// <summary>The helper's own formula when the value is worked out from a parameter.</summary>
            public XElement Formula;
        }

        private sealed class Run
        {
            private readonly XElement _program;
            private readonly bool _allowShared;
            private readonly GlobalVectors _globals;

            public Run(XElement program, double screenWidth, double screenHeight, bool allowShared)
            {
                _program = program;
                _allowShared = allowShared;
                _globals = new GlobalVectors(program, screenWidth, screenHeight);
            }

            public List<MfdLine> Lines { get; } = new List<MfdLine>();

            public void ApplyOne(MfdChange change)
            {
                var block = BlockProperty(change.Property);
                var direct = _program.Descendants("SetCraftProperty")
                    .Where(item => (string)item.Attribute("property") == block)
                    .Where(item =>
                    {
                        var name = item.Elements().FirstOrDefault();
                        return name != null && name.Name == "Constant" && (string)name.Attribute("text") == change.Widget;
                    })
                    .ToList();

                if (direct.Count > 1 && !AllSameValue(direct))
                {
                    this.Refuse(change, direct.Count + " blocks set this to different values, so there's no single one to change.");
                    return;
                }

                if (direct.Count > 0)
                {
                    // The same value gives the same outcome in each, so they all change or none do.
                    var first = this.Lines.Count;
                    foreach (var setter in direct)
                    {
                        this.ApplyToValue(change, setter.Elements().Skip(1).FirstOrDefault(), string.Empty);
                    }

                    if (direct.Count > 1)
                    {
                        this.Lines.RemoveRange(first + 1, this.Lines.Count - first - 1);
                        if (this.Lines[first].Applied)
                        {
                            this.Lines[first].Detail += " (in all " + direct.Count + " blocks that set it)";
                        }
                    }

                    return;
                }

                var calls = new List<(Helper Helper, XElement Call)>();
                foreach (var helper in this.FindHelpers(block))
                {
                    calls.AddRange(_program.Descendants("CallCustomInstruction")
                        .Where(call => (string)call.Attribute("call") == helper.Name)
                        .Where(call =>
                        {
                            var name = call.Elements().ElementAtOrDefault(helper.NameParameter);
                            return name != null && name.Name == "Constant" && (string)name.Attribute("text") == change.Widget;
                        })
                        .Select(call => (helper, call)));
                }

                if (calls.Count == 0)
                {
                    this.Refuse(change, "No block sets this by the widget's name.");
                }
                else if (calls.Count > 1)
                {
                    this.Refuse(change, calls.Count + " custom instruction calls set this, so there's no single one to change.");
                }
                else if (calls[0].Helper.ValueParameter >= 0)
                {
                    this.ApplyToValue(change, calls[0].Call.Elements().ElementAtOrDefault(calls[0].Helper.ValueParameter), " (in the call to " + calls[0].Helper.Name + ")");
                }
                else
                {
                    this.ApplyToCallArguments(change, calls[0].Helper, calls[0].Call);
                }
            }

            private void Refuse(MfdChange change, string reason, bool needsOverride = false)
            {
                this.Lines.Add(new MfdLine { Widget = change.Widget, Property = change.Property, Detail = reason, NeedsSharedOverride = needsOverride });
            }

            private void Done(MfdChange change, string detail, bool shared)
            {
                this.Lines.Add(new MfdLine { Widget = change.Widget, Property = change.Property, Applied = true, Detail = detail, Shared = shared });
            }

            private void ApplyToValue(MfdChange change, XElement value, string where)
            {
                var shared = false;
                if (value != null && value.Name == "Variable")
                {
                    if (!this.FollowVariable(value, 0, ref shared, out value, out var failure, out var needsOverride))
                    {
                        this.Refuse(change, failure, needsOverride);
                        return;
                    }
                }

                if (shared)
                {
                    where += " (shared variable: everything else reading it changes too)";
                }

                if (value == null)
                {
                    this.Refuse(change, "The block has no value.");
                }
                else if (value.Name == "EvaluateExpression")
                {
                    this.ApplyToFormula(change, value, where, shared);
                }
                else if (change.Property == MfdProperty.Rotation && (IsPlainNumber(value) || value.Name == "BinaryOp"))
                {
                    if (!this.SolveBlocks(value, change.X, out var number, out var solved, out var failure))
                    {
                        this.Refuse(change, failure);
                        return;
                    }

                    var old = NumberText(number);
                    SetNumber(number, solved);
                    this.Done(change, old + " becomes " + Format(solved) + where, shared);
                }
                else if (change.Property != MfdProperty.Rotation && value.Name == "Vector")
                {
                    // By position: Elements("Constant") would skip a formula in x and write y's number to x.
                    var parts = value.Elements().Take(2).ToList();
                    if (parts.Count < 2)
                    {
                        this.Refuse(change, "The vector has fewer than 2 parts.");
                        return;
                    }

                    // Both axes or neither.
                    var xSolved = this.SolveBlocks(parts[0], change.X, out var xNumber, out var xValue, out var xFailure);
                    var ySolved = this.SolveBlocks(parts[1], change.Y, out var yNumber, out var yValue, out var yFailure);
                    if (!xSolved || !ySolved)
                    {
                        this.Refuse(change, "x: " + (xFailure ?? "fine") + " y: " + (yFailure ?? "fine"));
                        return;
                    }

                    var old = "(" + NumberText(xNumber) + ", " + NumberText(yNumber) + ")";
                    SetNumber(xNumber, xValue);
                    SetNumber(yNumber, yValue);
                    this.Done(change, old + " becomes (" + Format(xValue) + ", " + Format(yValue) + ")" + where, shared);
                }
                else
                {
                    this.Refuse(change, "The value is built from " + value.Name + " blocks, which can't be rewritten. Numbers, + - * / blocks and Funk formulas can.");
                }
            }

            /// <summary>Whether blocks hold the same value, whatever their ids and places in the editor.</summary>
            private static bool AllSameValue(List<XElement> blocks)
            {
                var first = WithoutPlace(blocks[0]);
                return blocks.Skip(1).All(block => XNode.DeepEquals(first, WithoutPlace(block)));
            }

            private static XElement WithoutPlace(XElement block)
            {
                var copy = new XElement(block);
                foreach (var element in copy.DescendantsAndSelf())
                {
                    element.Attribute("id")?.Remove();
                    element.Attribute("pos")?.Remove();
                }

                return copy;
            }

            /// <summary>
            /// For a plain number, or + - * / blocks over numbers and known sizes: finds the one number in
            /// it and what that number must be for the whole to give the target.
            /// </summary>
            private bool SolveBlocks(XElement value, double target, out XElement number, out double solved, out string failure)
            {
                number = null;
                solved = 0;
                failure = null;
                var numbers = value.DescendantsAndSelf("Constant").Where(IsPlainNumber).ToList();
                if (numbers.Count != 1)
                {
                    failure = "It needs exactly one number to change, and this has " + numbers.Count + ".";
                    return false;
                }

                number = numbers[0];
                var result = this.SolveFor(value, number, target);
                if (result == null || double.IsNaN(result.Value) || double.IsInfinity(result.Value))
                {
                    failure = "Only + - * / blocks, numbers and known sizes can be solved.";
                    return false;
                }

                solved = result.Value;
                return true;
            }

            private double? SolveFor(XElement node, XElement number, double target)
            {
                if (node == number)
                {
                    return target;
                }

                var arguments = node.Elements().ToList();
                if (node.Name != "BinaryOp" || arguments.Count != 2)
                {
                    return null;
                }

                var op = (string)node.Attribute("op");
                var inLeft = arguments[0].DescendantsAndSelf().Contains(number);
                var otherKnown = this.TryEvaluateBlocks(arguments[inLeft ? 1 : 0], out var other);
                if (!otherKnown)
                {
                    return null;
                }

                double next;
                switch (op)
                {
                    case "+":
                        next = target - other;
                        break;
                    case "-":
                        next = inLeft ? target + other : other - target;
                        break;
                    case "*":
                        if (other == 0)
                        {
                            return null;
                        }

                        next = target / other;
                        break;
                    case "/":
                        if (inLeft)
                        {
                            next = target * other;
                        }
                        else if (target == 0)
                        {
                            return null;
                        }
                        else
                        {
                            next = other / target;
                        }

                        break;
                    default:
                        return null;
                }

                return this.SolveFor(arguments[inLeft ? 0 : 1], number, next);
            }

            private bool TryEvaluateBlocks(XElement node, out double value)
            {
                value = 0;
                if (node.Name == "BinaryOp")
                {
                    var arguments = node.Elements().ToList();
                    if (arguments.Count != 2 || !this.TryEvaluateBlocks(arguments[0], out var left) || !this.TryEvaluateBlocks(arguments[1], out var right))
                    {
                        return false;
                    }

                    switch ((string)node.Attribute("op"))
                    {
                        case "+":
                            value = left + right;
                            return true;
                        case "-":
                            value = left - right;
                            return true;
                        case "*":
                            value = left * right;
                            return true;
                        case "/":
                            value = left / right;
                            return right != 0;
                        default:
                            return false;
                    }
                }

                if (node.Name == "VectorOp")
                {
                    var op = (string)node.Attribute("op");
                    var source = node.Elements().FirstOrDefault();
                    if ((op != "x" && op != "y" && op != "z") || source == null || !this.TryKnownVector(source, out var x, out var y, out var z))
                    {
                        return false;
                    }

                    value = op == "x" ? x : op == "y" ? y : z;
                    return true;
                }

                // Only ever another number: SolveBlocks has already found there's one to change.
                return false;
            }

            private bool TryKnownVector(XElement source, out double x, out double y, out double z)
            {
                x = y = z = 0;
                if (source.Name == "Variable")
                {
                    return (string)source.Attribute("local") != "true" && _globals.TryResolve((string)source.Attribute("variableName"), out x, out y, out z);
                }

                return _globals.TryKnownBlock(source, out x, out y, out z);
            }

            private void ApplyToFormula(MfdChange change, XElement formula, string where, bool shared)
            {
                var constant = formula.Elements("Constant").FirstOrDefault();
                var text = (string)constant?.Attribute("text");
                if (text == null)
                {
                    this.Refuse(change, "The Funk block's text is built by other blocks.");
                    return;
                }

                string rewritten;
                if (change.Property == MfdProperty.Rotation)
                {
                    var result = VizzyExpressionSolver.TrySolve(text, change.X, _globals.TryResolve);
                    if (!result.Success)
                    {
                        this.Refuse(change, result.FailureReason + ": " + text);
                        return;
                    }

                    rewritten = result.RewrittenText;
                }
                else
                {
                    VizzyExpressionSolver.FuncCallNode vec;
                    try
                    {
                        vec = VizzyExpressionSolver.Parse(text) as VizzyExpressionSolver.FuncCallNode;
                    }
                    catch (FormatException e)
                    {
                        this.Refuse(change, "Couldn't read the formula (" + e.Message + "): " + text);
                        return;
                    }

                    if (vec == null || vec.Name != "vec" || vec.Args.Count < 2)
                    {
                        this.Refuse(change, "The formula isn't a plain vec(x, y, z): " + text);
                        return;
                    }

                    var x = vec.Args[0];
                    var y = vec.Args[1];
                    var xResult = VizzyExpressionSolver.TrySolve(text.Substring(x.Start, x.End - x.Start), change.X, _globals.TryResolve);
                    var yResult = VizzyExpressionSolver.TrySolve(text.Substring(y.Start, y.End - y.Start), change.Y, _globals.TryResolve);
                    if (!xResult.Success || !yResult.Success)
                    {
                        this.Refuse(change, "x: " + (xResult.FailureReason ?? "fine") + "; y: " + (yResult.FailureReason ?? "fine") + ". Formula: " + text);
                        return;
                    }

                    // y first: it comes later in the text, so x's position stays valid.
                    rewritten = text.Substring(0, y.Start) + yResult.RewrittenText + text.Substring(y.End);
                    rewritten = rewritten.Substring(0, x.Start) + xResult.RewrittenText + rewritten.Substring(x.End);
                }

                constant.SetAttributeValue("text", rewritten);
                this.Done(change, text + " becomes " + rewritten + where, shared);
            }

            /// <summary>
            /// The helper's body works the value out from its parameters, for every caller. So only this
            /// call's arguments may change, each axis separately: an axis fixed in the body stays.
            /// </summary>
            private void ApplyToCallArguments(MfdChange change, Helper helper, XElement call)
            {
                var text = (string)helper.Formula.Elements("Constant").FirstOrDefault()?.Attribute("text");
                VizzyExpressionSolver.Node root;
                try
                {
                    root = VizzyExpressionSolver.Parse(text);
                }
                catch (FormatException e)
                {
                    this.Refuse(change, "Couldn't read " + helper.Name + "'s formula (" + e.Message + "): " + text);
                    return;
                }

                if (change.Property == MfdProperty.Rotation)
                {
                    this.ApplyAxis(change, "rotation", text, change.X, helper, call);
                    return;
                }

                var vec = root as VizzyExpressionSolver.FuncCallNode;
                double offsetX = 0, offsetY = 0;
                if (vec == null || vec.Name != "vec")
                {
                    var sum = root as VizzyExpressionSolver.BinaryOpNode;
                    VizzyExpressionSolver.Node offset = null;
                    if (sum != null && sum.Op == '+')
                    {
                        if (sum.Right is VizzyExpressionSolver.FuncCallNode right && right.Name == "vec")
                        {
                            vec = right;
                            offset = sum.Left;
                        }
                        else if (sum.Left is VizzyExpressionSolver.FuncCallNode left && left.Name == "vec")
                        {
                            vec = left;
                            offset = sum.Right;
                        }
                    }

                    if (offset == null)
                    {
                        this.Refuse(change, helper.Name + "'s formula isn't vec(x, y, z) or offset + vec(x, y, z): " + text);
                        return;
                    }

                    var offsetXCall = new VizzyExpressionSolver.FuncCallNode { Name = "x", Args = new List<VizzyExpressionSolver.Node> { offset } };
                    var offsetYCall = new VizzyExpressionSolver.FuncCallNode { Name = "y", Args = new List<VizzyExpressionSolver.Node> { offset } };
                    if (!VizzyExpressionSolver.TryEvaluate(offsetXCall, _globals.TryResolve, out offsetX)
                        || !VizzyExpressionSolver.TryEvaluate(offsetYCall, _globals.TryResolve, out offsetY))
                    {
                        this.Refuse(change, "The offset in " + helper.Name + "'s formula can't be worked out: " + text);
                        return;
                    }
                }

                if (vec.Args.Count < 2)
                {
                    this.Refuse(change, "The vec in " + helper.Name + "'s formula has fewer than 2 parts: " + text);
                    return;
                }

                var x = vec.Args[0];
                var y = vec.Args[1];
                this.ApplyAxis(change, "x", text.Substring(x.Start, x.End - x.Start), change.X - offsetX, helper, call);
                this.ApplyAxis(change, "y", text.Substring(y.Start, y.End - y.Start), change.Y - offsetY, helper, call);
            }

            private void ApplyAxis(MfdChange change, string axis, string text, double target, Helper helper, XElement call)
            {
                var result = VizzyExpressionSolver.TrySolve(text, target, _globals.TryResolve);
                if (result.Success)
                {
                    this.Refuse(change, axis + " is fixed inside " + helper.Name + ", which every caller shares.");
                    return;
                }

                if (result.PendingLocalParameterName == null)
                {
                    this.Refuse(change, axis + ": " + result.FailureReason + ": " + text);
                    return;
                }

                var index = helper.Parameters.IndexOf(result.PendingLocalParameterName);
                var argument = index < 0 ? null : call.Elements().ElementAtOrDefault(index);
                if (argument == null || !IsPlainNumber(argument))
                {
                    this.Refuse(change, axis + ": the call's " + result.PendingLocalParameterName + " isn't a plain number.");
                    return;
                }

                var old = NumberText(argument);
                SetNumber(argument, result.NewValue);
                this.Done(change, axis + " (" + helper.Name + "'s " + result.PendingLocalParameterName + "): " + old + " becomes " + Format(result.NewValue), false);
            }

            /// <summary>Custom instructions that set the property for a widget named by one of their parameters.</summary>
            private IEnumerable<Helper> FindHelpers(string block)
            {
                foreach (var stack in _program.Descendants("Instructions"))
                {
                    var definition = stack.Elements().FirstOrDefault();
                    if (definition == null || definition.Name != "CustomInstruction")
                    {
                        continue;
                    }

                    var name = (string)definition.Attribute("name");
                    var format = (string)definition.Attribute("format");
                    var sets = stack.Elements("SetCraftProperty").Where(item => (string)item.Attribute("property") == block).ToList();
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(format) || sets.Count != 1)
                    {
                        continue;
                    }

                    var arguments = sets[0].Elements().ToList();
                    if (arguments.Count < 2 || arguments[0].Name != "Variable")
                    {
                        continue;
                    }

                    var parameters = ParameterNames(format);
                    var nameIndex = parameters.IndexOf((string)arguments[0].Attribute("variableName"));
                    if (nameIndex < 0)
                    {
                        continue;
                    }

                    var helper = new Helper { Name = name, NameParameter = nameIndex, Parameters = parameters };
                    if (arguments[1].Name == "Variable")
                    {
                        helper.ValueParameter = parameters.IndexOf((string)arguments[1].Attribute("variableName"));
                        if (helper.ValueParameter >= 0)
                        {
                            yield return helper;
                        }
                    }
                    else if (arguments[1].Name == "EvaluateExpression" && UsesParameter(arguments[1], parameters))
                    {
                        helper.Formula = arguments[1];
                        yield return helper;
                    }
                }
            }

            private static bool UsesParameter(XElement formula, List<string> parameters)
            {
                var text = (string)formula.Elements("Constant").FirstOrDefault()?.Attribute("text");
                if (text == null)
                {
                    return false;
                }

                try
                {
                    return UsesParameter(VizzyExpressionSolver.Parse(text), parameters);
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            private static bool UsesParameter(VizzyExpressionSolver.Node node, List<string> parameters)
            {
                switch (node)
                {
                    case VizzyExpressionSolver.VarRefNode variable:
                        return !variable.IsGlobal && parameters.Contains(variable.Name);
                    case VizzyExpressionSolver.BinaryOpNode binary:
                        return UsesParameter(binary.Left, parameters) || UsesParameter(binary.Right, parameters);
                    case VizzyExpressionSolver.UnaryMinusNode unary:
                        return UsesParameter(unary.Inner, parameters);
                    case VizzyExpressionSolver.FuncCallNode call:
                        return call.Args.Any(item => UsesParameter(item, parameters));
                    default:
                        return false;
                }
            }

            /// <summary>
            /// Finds what a global variable was set to. Allowed when one block sets it and one thing reads
            /// it, or with the override when more read it: then the change reaches them all on purpose.
            /// </summary>
            private bool FollowVariable(XElement variable, int depth, ref bool shared, out XElement value, out string failure, out bool needsOverride)
            {
                value = null;
                failure = null;
                needsOverride = false;
                var name = (string)variable.Attribute("variableName");
                if ((string)variable.Attribute("local") == "true")
                {
                    failure = "The value comes from the local variable " + name + ".";
                    return false;
                }

                if (depth > MaxVariableChain)
                {
                    failure = "Too many variables lead to this value.";
                    return false;
                }

                var sets = _program.Descendants("SetVariable")
                    .Where(item => (string)item.Elements("Variable").FirstOrDefault()?.Attribute("variableName") == name)
                    .ToList();

                // Anything but the name a SetVariable writes to is a read, including another SetVariable's value.
                var reads = _program.Descendants("Variable").Count(item =>
                    (string)item.Attribute("variableName") == name
                    && (string)item.Attribute("local") != "true"
                    && !(item.Parent?.Name == "SetVariable" && item.ElementsBeforeSelf().FirstOrDefault() == null));

                // A Funk name may have v: or be bare.
                var pattern = new Regex(@"(?<![A-Za-z0-9_.:])(v:)?" + Regex.Escape(name) + @"(?![A-Za-z0-9_])");
                var funkReads = _program.Descendants("EvaluateExpression")
                    .SelectMany(item => item.Elements("Constant"))
                    .Select(item => (string)item.Attribute("text"))
                    .Count(text => text != null && pattern.IsMatch(text));

                if (sets.Count != 1)
                {
                    failure = "The variable " + name + " is set by " + sets.Count + " blocks, so there's no single value to change.";
                    return false;
                }

                if (reads != 1 || funkReads != 0)
                {
                    if (!_allowShared)
                    {
                        failure = "The variable " + name + " is also read elsewhere, so changing it moves those too.";
                        needsOverride = true;
                        return false;
                    }

                    shared = true;
                }

                value = sets[0].Elements().Skip(1).FirstOrDefault();
                if (value != null && value.Name == "Variable")
                {
                    return this.FollowVariable(value, depth + 1, ref shared, out value, out failure, out needsOverride);
                }

                return true;
            }
        }

        /// <summary>
        /// Works out global vectors used in formulas, for two shapes: the live size of _Screen, and a Funk
        /// vec(a, b, c). The value must come from one block: anything else fails rather than guesses.
        /// </summary>
        private sealed class GlobalVectors
        {
            private readonly XElement _program;
            private readonly double _screenWidth;
            private readonly double _screenHeight;
            private readonly Dictionary<string, double[]> _known = new Dictionary<string, double[]>();
            private readonly HashSet<string> _working = new HashSet<string>();

            public GlobalVectors(XElement program, double screenWidth, double screenHeight)
            {
                _program = program;
                _screenWidth = screenWidth;
                _screenHeight = screenHeight;
            }

            /// <summary>A block whose vector is known without running: the size of _Screen.</summary>
            public bool TryKnownBlock(XElement block, out double x, out double y, out double z)
            {
                x = y = z = 0;
                if (block.Name != "CraftProperty" || (string)block.Attribute("property") != "Mfd.Size"
                    || (string)block.Elements("Constant").FirstOrDefault()?.Attribute("text") != "_Screen")
                {
                    return false;
                }

                x = _screenWidth;
                y = _screenHeight;
                return true;
            }

            public bool TryResolve(string name, out double x, out double y, out double z)
            {
                x = y = z = 0;
                if (!_known.TryGetValue(name, out var value))
                {
                    if (!_working.Add(name))
                    {
                        return false;
                    }

                    value = this.Find(name);
                    _working.Remove(name);
                    _known[name] = value;
                }

                if (value == null)
                {
                    return false;
                }

                x = value[0];
                y = value[1];
                z = value[2];
                return true;
            }

            private double[] Find(string name)
            {
                var sets = _program.Descendants("SetVariable")
                    .Where(item =>
                    {
                        var variable = item.Elements("Variable").FirstOrDefault();
                        return (string)variable?.Attribute("variableName") == name && (string)variable?.Attribute("local") != "true";
                    })
                    .ToList();
                var value = sets.Count == 1 ? sets[0].Elements().Skip(1).FirstOrDefault() : null;
                if (value == null)
                {
                    return null;
                }

                if (this.TryKnownBlock(value, out var screenX, out var screenY, out var screenZ))
                {
                    return new[] { screenX, screenY, screenZ };
                }

                var text = value.Name == "EvaluateExpression" ? (string)value.Elements("Constant").FirstOrDefault()?.Attribute("text") : null;
                if (text == null)
                {
                    return null;
                }

                VizzyExpressionSolver.FuncCallNode vec;
                try
                {
                    vec = VizzyExpressionSolver.Parse(text) as VizzyExpressionSolver.FuncCallNode;
                }
                catch (FormatException)
                {
                    return null;
                }

                if (vec == null || vec.Name != "vec" || vec.Args.Count < 2
                    || !VizzyExpressionSolver.TryEvaluate(vec.Args[0], this.TryResolve, out var x)
                    || !VizzyExpressionSolver.TryEvaluate(vec.Args[1], this.TryResolve, out var y))
                {
                    return null;
                }

                var z = 0d;
                if (vec.Args.Count > 2 && !VizzyExpressionSolver.TryEvaluate(vec.Args[2], this.TryResolve, out z))
                {
                    return null;
                }

                return new[] { x, y, z };
            }
        }
    }
}
