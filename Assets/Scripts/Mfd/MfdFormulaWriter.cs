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

    /// <summary>A widget's value when the panel opened (From) and now. Rotation only uses X.</summary>
    public sealed class MfdChange
    {
        public string Widget;
        public MfdProperty Property;
        public double X;
        public double Y;
        public double FromX;
        public double FromY;
    }

    /// <summary>What happened to one property, or one axis of it.</summary>
    public sealed class MfdLine
    {
        public string Widget;
        public MfdProperty Property;

        /// <summary>"x" or "y" when the line is about one axis only, otherwise null.</summary>
        public string Axis;

        public bool Applied;

        /// <summary>Whether the change reaches other readers of a shared variable.</summary>
        public bool Shared;

        /// <summary>Whether it was refused only because a variable is shared, which the override would allow.</summary>
        public bool NeedsSharedOverride;

        /// <summary>Old and new text when applied, the reason when not.</summary>
        public string Detail;

        public override string ToString()
        {
            return this.Widget + " " + this.Property.ToString().ToLowerInvariant() + (this.Axis != null ? " " + this.Axis : string.Empty) + ": " + this.Detail;
        }
    }

    /// <summary>
    /// Writes MFD widget values into a program's XML: a block's literal numbers, the one number in a
    /// formula made of blocks or Funk, or the argument of a helper custom instruction. It refuses rather
    /// than guesses: nothing else in the program may change, a formula is only rewritten when one number
    /// decides it, and only when the block still gives where the widget was when the panel opened, so no
    /// other block (a later Set Parent, an anchor, another setter) moves it. Several blocks setting a
    /// widget to the same value change together. Only stacks that can run count.
    /// </summary>
    public static class MfdFormulaWriter
    {
        private const int MaxVariableChain = 4;

        // Widget values are floats, and the game works some out from the screen's size.
        private const double AbsoluteTolerance = 0.01;
        private const double RelativeTolerance = 1e-5;

        /// <summary>
        /// Says what it would do for each change, and does it unless dryRun. An axis that didn't change
        /// is left alone and gives no line.
        /// </summary>
        public static List<MfdLine> Apply(XElement program, IEnumerable<MfdChange> changes, double screenWidth, double screenHeight, bool allowSharedVariables, bool dryRun = false)
        {
            var run = new Run(program, screenWidth, screenHeight, allowSharedVariables, dryRun);
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

        /// <summary>The signed smallest turn from a to b, in degrees: the game reads rotation back in [0, 360).</summary>
        public static double DeltaAngle(double a, double b)
        {
            var d = (b - a) % 360;
            if (d > 180)
            {
                d -= 360;
            }
            else if (d < -180)
            {
                d += 360;
            }

            return d;
        }

        private static string Format(double value)
        {
            return VizzyExpressionSolver.FormatNumber(value);
        }

        private static bool IsPlainNumber(XElement element)
        {
            return element.Name == "Constant" && TryNumber(element, out _);
        }

        private static bool TryNumber(XElement constant, out double value)
        {
            value = 0;
            var attribute = constant.Attribute("number") ?? constant.Attribute("text");
            return attribute != null && double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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

        /// <summary>One axis that changed: where it was when the panel opened, and where it is now.</summary>
        private sealed class Axis
        {
            public string Name;
            public int Index;
            public double From;
            public double To;
            public bool IsRotation;

            public bool Matches(double current)
            {
                var difference = this.IsRotation ? DeltaAngle(current, this.From) : this.From - current;
                return Math.Abs(difference) <= AbsoluteTolerance + (RelativeTolerance * Math.Abs(this.From));
            }

            /// <summary>Rotation adds the turn made, so -30 turned by 10 stays -20 rather than 340.</summary>
            public double Target(double current)
            {
                return this.IsRotation ? current + DeltaAngle(this.From, this.To) : this.To;
            }
        }

        /// <summary>What one change would write, worked out before anything is written.</summary>
        private sealed class Plan
        {
            public readonly List<Action> Writes = new List<Action>();
            public readonly List<string> Parts = new List<string>();
            public string Failure;
            public bool NeedsOverride;
            public bool Shared;

            public bool Ok => this.Failure == null;

            public Plan Fail(string failure, bool needsOverride = false)
            {
                this.Failure ??= failure;
                this.NeedsOverride |= needsOverride;
                return this;
            }
        }

        private sealed class Run
        {
            private const string Moved = "Something else moves this widget: the block doesn't give where it was when the panel opened, for example after a Set Parent or another block that sets it.";

            private readonly XElement _program;
            private readonly bool _allowShared;
            private readonly bool _dryRun;
            private readonly GlobalVectors _globals;

            public Run(XElement program, double screenWidth, double screenHeight, bool allowShared, bool dryRun)
            {
                _program = program;
                _allowShared = allowShared;
                _dryRun = dryRun;
                _globals = new GlobalVectors(program, screenWidth, screenHeight, this.Reachable);
            }

            public List<MfdLine> Lines { get; } = new List<MfdLine>();

            /// <summary>In a stack that can run: one under an event, or a custom instruction's body.</summary>
            public bool Reachable(XElement block)
            {
                var stack = block.Ancestors("Instructions").FirstOrDefault(item => item.Parent == _program);
                var first = stack?.Elements().FirstOrDefault();
                return first != null && (first.Name == "Event" || first.Name == "CustomInstruction");
            }

            public void ApplyOne(MfdChange change)
            {
                var axes = ChangedAxes(change);
                if (axes.Count == 0)
                {
                    return;
                }

                var block = BlockProperty(change.Property);
                var direct = _program.Descendants("SetCraftProperty")
                    .Where(item => (string)item.Attribute("property") == block)
                    .Where(item =>
                    {
                        var name = item.Elements().FirstOrDefault();
                        return name != null && name.Name == "Constant" && (string)name.Attribute("text") == change.Widget;
                    })
                    .Where(this.Reachable)
                    .ToList();

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
                        .Where(this.Reachable)
                        .Select(call => (helper, call)));
                }

                if (direct.Count > 0 && calls.Count > 0)
                {
                    this.Refuse(change, null, "Both a block and a custom instruction call set this, so there's no single one to change.");
                }
                else if (direct.Count > 1 && !AllSameValue(direct))
                {
                    this.Refuse(change, null, direct.Count + " blocks set this to different values, so there's no single one to change.");
                }
                else if (direct.Count > 0)
                {
                    // The same value gives the same plan in each, so they all change or none do.
                    var plans = direct.Select(setter => this.PlanValue(axes, setter.Elements().Skip(1).FirstOrDefault())).ToList();
                    var bad = plans.FirstOrDefault(plan => !plan.Ok);
                    if (bad != null)
                    {
                        this.Refuse(change, null, bad.Failure, bad.NeedsOverride);
                        return;
                    }

                    plans.ForEach(this.Write);
                    this.Done(change, null, string.Join(", ", plans[0].Parts) + (direct.Count > 1 ? " (in all " + direct.Count + " blocks that set it)" : string.Empty), plans[0].Shared);
                }
                else if (calls.Count == 0)
                {
                    this.Refuse(change, null, "No block sets this by the widget's name.");
                }
                else if (calls.Count > 1)
                {
                    this.Refuse(change, null, calls.Count + " custom instruction calls set this, so there's no single one to change.");
                }
                else if (calls[0].Helper.ValueParameter >= 0)
                {
                    var plan = this.PlanValue(axes, calls[0].Call.Elements().ElementAtOrDefault(calls[0].Helper.ValueParameter));
                    this.Finish(change, null, plan, " (in the call to " + calls[0].Helper.Name + ")");
                }
                else
                {
                    this.ApplyToCallArguments(change, axes, calls[0].Helper, calls[0].Call);
                }
            }

            private static List<Axis> ChangedAxes(MfdChange change)
            {
                var axes = new List<Axis>();
                if (change.Property == MfdProperty.Rotation)
                {
                    var axis = new Axis { Name = "rotation", From = change.FromX, To = change.X, IsRotation = true };
                    if (!axis.Matches(change.X))
                    {
                        axes.Add(axis);
                    }

                    return axes;
                }

                var x = new Axis { Name = "x", Index = 0, From = change.FromX, To = change.X };
                var y = new Axis { Name = "y", Index = 1, From = change.FromY, To = change.Y };
                if (!x.Matches(change.X))
                {
                    axes.Add(x);
                }

                if (!y.Matches(change.Y))
                {
                    axes.Add(y);
                }

                return axes;
            }

            private void Write(Plan plan)
            {
                if (!_dryRun)
                {
                    plan.Writes.ForEach(write => write());
                }
            }

            private void Finish(MfdChange change, string axis, Plan plan, string where)
            {
                if (!plan.Ok)
                {
                    this.Refuse(change, axis, plan.Failure, plan.NeedsOverride);
                    return;
                }

                this.Write(plan);
                this.Done(change, axis, string.Join(", ", plan.Parts) + where, plan.Shared);
            }

            private void Refuse(MfdChange change, string axis, string reason, bool needsOverride = false)
            {
                this.Lines.Add(new MfdLine { Widget = change.Widget, Property = change.Property, Axis = axis, Detail = reason, NeedsSharedOverride = needsOverride });
            }

            private void Done(MfdChange change, string axis, string detail, bool shared)
            {
                this.Lines.Add(new MfdLine { Widget = change.Widget, Property = change.Property, Axis = axis, Applied = true, Detail = detail, Shared = shared });
            }

            /// <summary>Both changed axes or neither, for one value: a number, a vector, blocks or Funk, maybe behind a variable.</summary>
            private Plan PlanValue(List<Axis> axes, XElement value)
            {
                var plan = new Plan();
                if (value != null && value.Name == "Variable")
                {
                    var shared = false;
                    if (!this.FollowVariable(value, 0, ref shared, out value, out var failure, out var needsOverride))
                    {
                        return plan.Fail(failure, needsOverride);
                    }

                    plan.Shared = shared;
                }

                var rotation = axes[0].IsRotation;
                if (value == null)
                {
                    return plan.Fail("The block has no value.");
                }

                if (value.Name == "EvaluateExpression")
                {
                    this.PlanFunk(plan, axes, value);
                }
                else if (rotation && (IsPlainNumber(value) || value.Name == "BinaryOp"))
                {
                    this.PlanBlocks(plan, axes[0], value);
                }
                else if (!rotation && value.Name == "Vector")
                {
                    // By position: Elements("Constant") would skip a formula in x and write y's number to x.
                    var parts = value.Elements().Take(2).ToList();
                    if (parts.Count < 2)
                    {
                        return plan.Fail("The vector has fewer than 2 parts.");
                    }

                    foreach (var axis in axes)
                    {
                        this.PlanBlocks(plan, axis, parts[axis.Index]);
                    }
                }
                else
                {
                    plan.Fail("The value is built from " + value.Name + " blocks, which can't be rewritten. Numbers, + - * / blocks and Funk formulas can.");
                }

                if (plan.Ok && plan.Shared)
                {
                    plan.Parts.Add("shared variable: everything else reading it changes too");
                }

                return plan;
            }

            private void PlanBlocks(Plan plan, Axis axis, XElement node)
            {
                if (!this.TryEvaluateBlocks(node, out var current))
                {
                    plan.Fail(axis.Name + ": only numbers, + - * / blocks and the screen's size can be worked out.");
                    return;
                }

                if (!axis.Matches(current))
                {
                    plan.Fail(Moved);
                    return;
                }

                if (!this.SolveBlocks(node, axis.Target(current), out var number, out var solved, out var failure))
                {
                    plan.Fail(axis.Name + ": " + failure);
                    return;
                }

                plan.Parts.Add(Label(axis) + NumberText(number) + " becomes " + Format(solved));
                plan.Writes.Add(() => SetNumber(number, solved));
            }

            private static string Label(Axis axis)
            {
                return axis.IsRotation ? string.Empty : axis.Name + ": ";
            }

            /// <summary>Whether blocks hold the same value, whatever their ids, places in the editor and indents.</summary>
            private static bool AllSameValue(List<XElement> blocks)
            {
                var first = WithoutPlace(blocks[0]);
                return blocks.Skip(1).All(block => XNode.DeepEquals(first, WithoutPlace(block)));
            }

            private static XElement WithoutPlace(XElement block)
            {
                var copy = new XElement(block);

                // A program loaded from a file keeps its line breaks and indents, which differ with nesting.
                copy.DescendantNodes().OfType<XText>().Where(text => string.IsNullOrWhiteSpace(text.Value)).ToList().ForEach(text => text.Remove());
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
                if (!this.TryEvaluateBlocks(arguments[inLeft ? 1 : 0], out var other))
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
                        // n / 0 or k / n = 0 can't be solved.
                        if ((inLeft && other == 0) || (!inLeft && target == 0))
                        {
                            return null;
                        }

                        next = inLeft ? target * other : other / target;
                        break;
                    default:
                        return null;
                }

                return this.SolveFor(arguments[inLeft ? 0 : 1], number, next);
            }

            private bool TryEvaluateBlocks(XElement node, out double value)
            {
                value = 0;
                if (node.Name == "Constant")
                {
                    return TryNumber(node, out value);
                }

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

            private void PlanFunk(Plan plan, List<Axis> axes, XElement formula)
            {
                var constant = formula.Elements("Constant").FirstOrDefault();
                var text = (string)constant?.Attribute("text");
                if (text == null)
                {
                    plan.Fail("The Funk block's text is built by other blocks.");
                    return;
                }

                VizzyExpressionSolver.Node root;
                try
                {
                    root = VizzyExpressionSolver.Parse(text);
                }
                catch (FormatException e)
                {
                    plan.Fail("Couldn't read the formula (" + e.Message + "): " + text);
                    return;
                }

                // Each changed axis as (start, end, node) in the text; replaced from the end so earlier places stay valid.
                var spans = new List<(int Start, int End, VizzyExpressionSolver.Node Node, Axis Axis)>();
                if (axes[0].IsRotation)
                {
                    spans.Add((0, text.Length, root, axes[0]));
                }
                else
                {
                    var vec = root as VizzyExpressionSolver.FuncCallNode;
                    if (vec == null || vec.Name != "vec" || vec.Args.Count < 2)
                    {
                        plan.Fail("The formula isn't a plain vec(x, y, z): " + text);
                        return;
                    }

                    spans.AddRange(axes.Select(axis => (vec.Args[axis.Index].Start, vec.Args[axis.Index].End, vec.Args[axis.Index], axis)));
                }

                var rewritten = text;
                foreach (var span in spans.OrderByDescending(item => item.Start))
                {
                    if (!VizzyExpressionSolver.TryEvaluate(span.Node, _globals.TryResolve, out var current))
                    {
                        plan.Fail(span.Axis.Name + ": the formula's value can't be worked out: " + text);
                        return;
                    }

                    if (!span.Axis.Matches(current))
                    {
                        plan.Fail(Moved);
                        return;
                    }

                    var part = text.Substring(span.Start, span.End - span.Start);
                    var result = VizzyExpressionSolver.TrySolve(part, span.Axis.Target(current), _globals.TryResolve);
                    if (!result.Success)
                    {
                        plan.Fail(span.Axis.Name + ": " + result.FailureReason + ". Formula: " + text);
                        return;
                    }

                    rewritten = rewritten.Substring(0, span.Start) + result.RewrittenText + rewritten.Substring(span.End);
                }

                plan.Parts.Add(text + " becomes " + rewritten);
                plan.Writes.Add(() => constant.SetAttributeValue("text", rewritten));
            }

            /// <summary>
            /// The helper's body works the value out from its parameters, for every caller. So only this
            /// call's arguments may change, each axis on its own line: an axis fixed in the body stays.
            /// </summary>
            private void ApplyToCallArguments(MfdChange change, List<Axis> axes, Helper helper, XElement call)
            {
                var text = (string)helper.Formula.Elements("Constant").FirstOrDefault()?.Attribute("text");
                VizzyExpressionSolver.Node root;
                try
                {
                    root = VizzyExpressionSolver.Parse(text);
                }
                catch (FormatException e)
                {
                    this.Refuse(change, null, "Couldn't read " + helper.Name + "'s formula (" + e.Message + "): " + text);
                    return;
                }

                var arguments = call.Elements().ToList();
                double? Argument(string name)
                {
                    var index = helper.Parameters.IndexOf(name);
                    return index >= 0 && index < arguments.Count && TryNumber(arguments[index], out var number) && arguments[index].Name == "Constant" ? number : (double?)null;
                }

                if (axes[0].IsRotation)
                {
                    this.Finish(change, null, this.PlanHelperAxis(axes[0], text, root, 0, helper, arguments, Argument), string.Empty);
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
                        this.Refuse(change, null, helper.Name + "'s formula isn't vec(x, y, z) or offset + vec(x, y, z): " + text);
                        return;
                    }

                    var offsetXCall = new VizzyExpressionSolver.FuncCallNode { Name = "x", Args = new List<VizzyExpressionSolver.Node> { offset } };
                    var offsetYCall = new VizzyExpressionSolver.FuncCallNode { Name = "y", Args = new List<VizzyExpressionSolver.Node> { offset } };
                    if (!VizzyExpressionSolver.TryEvaluate(offsetXCall, _globals.TryResolve, out offsetX)
                        || !VizzyExpressionSolver.TryEvaluate(offsetYCall, _globals.TryResolve, out offsetY))
                    {
                        this.Refuse(change, null, "The offset in " + helper.Name + "'s formula can't be worked out: " + text);
                        return;
                    }
                }

                if (vec.Args.Count < 2)
                {
                    this.Refuse(change, null, "The vec in " + helper.Name + "'s formula has fewer than 2 parts: " + text);
                    return;
                }

                foreach (var axis in axes)
                {
                    var node = vec.Args[axis.Index];
                    var plan = this.PlanHelperAxis(axis, text.Substring(node.Start, node.End - node.Start), node, axis.Index == 0 ? offsetX : offsetY, helper, arguments, Argument);
                    this.Finish(change, axis.Name, plan, string.Empty);
                }
            }

            private Plan PlanHelperAxis(Axis axis, string text, VizzyExpressionSolver.Node node, double offset, Helper helper, List<XElement> arguments, Func<string, double?> argument)
            {
                var plan = new Plan();
                if (!VizzyExpressionSolver.TryEvaluate(node, _globals.TryResolve, argument, out var value))
                {
                    return plan.Fail(helper.Name + "'s value for this call can't be worked out.");
                }

                var current = value + offset;
                if (!axis.Matches(current))
                {
                    return plan.Fail(Moved);
                }

                var result = VizzyExpressionSolver.TrySolve(text, axis.Target(current) - offset, _globals.TryResolve);
                if (result.Success)
                {
                    return plan.Fail(axis.Name + " is fixed inside " + helper.Name + ", which every caller shares.");
                }

                if (result.PendingLocalParameterName == null)
                {
                    return plan.Fail(axis.Name + ": " + result.FailureReason + ": " + text);
                }

                var index = helper.Parameters.IndexOf(result.PendingLocalParameterName);
                var target = index < 0 ? null : arguments.ElementAtOrDefault(index);
                if (target == null || !IsPlainNumber(target))
                {
                    return plan.Fail(axis.Name + ": the call's " + result.PendingLocalParameterName + " isn't a plain number.");
                }

                var solved = result.NewValue;
                plan.Parts.Add(helper.Name + "'s " + result.PendingLocalParameterName + ": " + NumberText(target) + " becomes " + Format(solved));
                plan.Writes.Add(() => SetNumber(target, solved));
                return plan;
            }

            /// <summary>Custom instructions that set the property for a widget named by one of their parameters.</summary>
            private IEnumerable<Helper> FindHelpers(string block)
            {
                foreach (var stack in _program.Elements("Instructions"))
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
                    .Where(this.Reachable)
                    .ToList();

                // Change Variable and User Input write to it too, so its value isn't just what one block sets.
                if (OtherWrites(_program, name))
                {
                    failure = "The variable " + name + " is also changed by another kind of block, so its value can't be worked out.";
                    return false;
                }

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

        /// <summary>Blocks other than Set Variable that write to a global variable.</summary>
        private static bool OtherWrites(XElement program, string name)
        {
            return program.Descendants("Variable").Any(item =>
                (string)item.Attribute("variableName") == name
                && (string)item.Attribute("local") != "true"
                && item.ElementsBeforeSelf().FirstOrDefault() == null
                && (item.Parent?.Name == "ChangeVariable" || item.Parent?.Name == "UserInput"));
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

            private readonly Func<XElement, bool> _reachable;

            public GlobalVectors(XElement program, double screenWidth, double screenHeight, Func<XElement, bool> reachable)
            {
                _program = program;
                _reachable = reachable;
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
                    .Where(_reachable)
                    .ToList();
                var value = sets.Count == 1 && !OtherWrites(_program, name) ? sets[0].Elements().Skip(1).FirstOrDefault() : null;
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
