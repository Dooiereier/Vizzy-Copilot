namespace Assets.Scripts.Mfd
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A small symbolic solver for the Funk used by MFD position, size and
    /// rotation formulas (e.g. "0.22 * x(v:ScreenSize)").
    ///
    /// Given an expression string and the desired result of evaluating it,
    /// this finds the single numeric literal (or, in a later increment, a
    /// subroutine-local parameter) acting as "the unknown", inverts the
    /// arithmetic tree (+, -, *, /, unary -) to solve for it, and splices
    /// just its new numeric text into the original string -- everything
    /// else (whitespace, function calls, variable names) stays
    /// byte-identical.
    ///
    /// This has a real mathematical ceiling, not just an engineering one:
    /// it only handles the unknown appearing exactly once, reached only
    /// through linear operations. The unknown appearing more than once, or
    /// under a nonlinear function (sin/cos/min/max/etc.), correctly fails
    /// rather than guessing -- there is no general fix for that, it's not
    /// solvable from the text alone.
    /// </summary>
    internal static class VizzyExpressionSolver
    {
        public abstract class Node
        {
            public int Start;
            public int End;
        }

        public class NumberNode : Node
        {
            public double Value;
        }

        public class VarRefNode : Node
        {
            public string Name;
            public bool IsGlobal;
        }

        public class FuncCallNode : Node
        {
            public string Name;
            public List<Node> Args = new List<Node>();
        }

        public class BinaryOpNode : Node
        {
            public char Op;
            public Node Left;
            public Node Right;
        }

        public class UnaryMinusNode : Node
        {
            public Node Inner;
        }

        private class Token
        {
            public string Text;
            public int Start;
            public int End;
        }

        private static readonly Regex TokenRegex = new Regex(
            @"\s*(?:(?<num>\d+\.?\d*|\.\d+)|(?<vref>v:[A-Za-z_][A-Za-z0-9_]*)|(?<ident>[A-Za-z_][A-Za-z0-9_]*)|(?<op>[()+\-*/,]))",
            RegexOptions.Compiled);

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var pos = 0;
            while (pos < text.Length)
            {
                var match = TokenRegex.Match(text, pos);
                if (!match.Success || match.Index != pos)
                {
                    if (text.Substring(pos).Trim().Length == 0)
                    {
                        break;
                    }

                    throw new FormatException($"unexpected character at {pos} in '{text}'");
                }

                pos = match.Index + match.Length;
                var g = match.Groups["num"].Success ? match.Groups["num"]
                    : match.Groups["vref"].Success ? match.Groups["vref"]
                    : match.Groups["ident"].Success ? match.Groups["ident"]
                    : match.Groups["op"];

                tokens.Add(new Token { Text = g.Value, Start = g.Index, End = g.Index + g.Length });
            }

            return tokens;
        }

        private class Parser
        {
            private readonly List<Token> _tokens;
            private int _i;

            public Parser(List<Token> tokens)
            {
                _tokens = tokens;
            }

            private Token Peek() => _i < _tokens.Count ? _tokens[_i] : null;

            private Token Eat(string expected = null)
            {
                var t = Peek();
                if (t == null)
                {
                    throw new FormatException("unexpected end of expression");
                }

                if (expected != null && t.Text != expected)
                {
                    throw new FormatException($"expected '{expected}', got '{t.Text}'");
                }

                _i++;
                return t;
            }

            public Node Parse()
            {
                var node = ParseExpr();
                if (Peek() != null)
                {
                    throw new FormatException("trailing tokens");
                }

                return node;
            }

            private Node ParseExpr()
            {
                var node = ParseTerm();
                while (Peek() != null && (Peek().Text == "+" || Peek().Text == "-"))
                {
                    var op = Eat().Text[0];
                    var rhs = ParseTerm();
                    node = new BinaryOpNode { Op = op, Left = node, Right = rhs, Start = node.Start, End = rhs.End };
                }

                return node;
            }

            private Node ParseTerm()
            {
                var node = ParseFactor();
                while (Peek() != null && (Peek().Text == "*" || Peek().Text == "/"))
                {
                    var op = Eat().Text[0];
                    var rhs = ParseFactor();
                    node = new BinaryOpNode { Op = op, Left = node, Right = rhs, Start = node.Start, End = rhs.End };
                }

                return node;
            }

            private Node ParseFactor()
            {
                var tok = Peek();
                if (tok == null)
                {
                    throw new FormatException("unexpected end of expression");
                }

                if (tok.Text == "-")
                {
                    Eat();
                    var inner = ParseFactor();
                    return new UnaryMinusNode { Inner = inner, Start = tok.Start, End = inner.End };
                }

                if (tok.Text == "(")
                {
                    Eat("(");
                    var inner = ParseExpr();
                    var close = Eat(")");
                    inner.Start = tok.Start;
                    inner.End = close.End;
                    return inner;
                }

                if (tok.Text.StartsWith("v:"))
                {
                    Eat();
                    return new VarRefNode { Name = tok.Text.Substring(2), IsGlobal = true, Start = tok.Start, End = tok.End };
                }

                if (Regex.IsMatch(tok.Text, @"^(\d+\.?\d*|\.\d+)$"))
                {
                    Eat();
                    return new NumberNode { Value = double.Parse(tok.Text, CultureInfo.InvariantCulture), Start = tok.Start, End = tok.End };
                }

                if (Regex.IsMatch(tok.Text, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                {
                    Eat();
                    if (Peek() != null && Peek().Text == "(")
                    {
                        Eat("(");
                        var args = new List<Node>();
                        if (Peek() != null && Peek().Text != ")")
                        {
                            args.Add(ParseExpr());
                            while (Peek() != null && Peek().Text == ",")
                            {
                                Eat(",");
                                args.Add(ParseExpr());
                            }
                        }

                        var close = Eat(")");
                        return new FuncCallNode { Name = tok.Text, Args = args, Start = tok.Start, End = close.End };
                    }

                    return new VarRefNode { Name = tok.Text, IsGlobal = false, Start = tok.Start, End = tok.End };
                }

                throw new FormatException($"unexpected token '{tok.Text}'");
            }
        }

        public static Node Parse(string text) => new Parser(Tokenize(text)).Parse();

        /// <summary>
        /// A number for a program, with a decimal point. Seven significant digits, as values come from floats:
        /// more would write 123.7 as 123.699997.
        /// </summary>
        public static string FormatNumber(double value)
        {
            var rounded = double.Parse(value.ToString("G7", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            return rounded.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Resolves a global variable to its (x, y, z) value. Every real
        /// global seen so far in these formulas (ScreenSize, SystemsPos,
        /// etc.) is a vector, always accessed through x()/y()/z()
        /// component extraction rather than used as a bare scalar.
        /// </summary>
        public delegate bool TryResolveGlobalVector(string name, out double x, out double y, out double z);

        /// <summary>
        /// Evaluates a fully-known expression (every leaf resolvable) to a
        /// number.
        /// </summary>
        public static bool TryEvaluate(Node node, TryResolveGlobalVector resolveGlobal, out double result)
        {
            return TryEvaluate(node, resolveGlobal, null, out result);
        }

        /// <summary>
        /// As above, with a value for each bare name a custom instruction's parameter stands for: the
        /// arguments of one call. A bare name without one stays unknown.
        /// </summary>
        public static bool TryEvaluate(Node node, TryResolveGlobalVector resolveGlobal, Func<string, double?> resolveLocal, out double result)
        {
            var numberNode = node as NumberNode;
            if (numberNode != null)
            {
                result = numberNode.Value;
                return true;
            }

            var varRefNode = node as VarRefNode;
            if (varRefNode != null && !varRefNode.IsGlobal && resolveLocal?.Invoke(varRefNode.Name) is double local)
            {
                result = local;
                return true;
            }

            if (varRefNode != null)
            {
                // A bare global used directly as a scalar isn't something
                // any real formula seen so far does (globals are vectors,
                // always accessed via x()/y()/z()) -- correctly unresolvable.
                result = 0;
                return false;
            }

            var unaryNode = node as UnaryMinusNode;
            if (unaryNode != null)
            {
                double innerVal;
                if (!TryEvaluate(unaryNode.Inner, resolveGlobal, resolveLocal, out innerVal))
                {
                    result = 0;
                    return false;
                }

                result = -innerVal;
                return true;
            }

            var binOpNode = node as BinaryOpNode;
            if (binOpNode != null)
            {
                double l, r;
                if (!TryEvaluate(binOpNode.Left, resolveGlobal, resolveLocal, out l) || !TryEvaluate(binOpNode.Right, resolveGlobal, resolveLocal, out r))
                {
                    result = 0;
                    return false;
                }

                if (binOpNode.Op == '+')
                {
                    result = l + r;
                    return true;
                }

                if (binOpNode.Op == '-')
                {
                    result = l - r;
                    return true;
                }

                if (binOpNode.Op == '*')
                {
                    result = l * r;
                    return true;
                }

                if (binOpNode.Op == '/')
                {
                    if (r == 0)
                    {
                        result = 0;
                        return false;
                    }

                    result = l / r;
                    return true;
                }

                result = 0;
                return false;
            }

            var funcCallNode = node as FuncCallNode;
            if (funcCallNode != null && funcCallNode.Args.Count == 1)
            {
                var argVarRef = funcCallNode.Args[0] as VarRefNode;
                if (argVarRef != null && argVarRef.IsGlobal &&
                    (funcCallNode.Name == "x" || funcCallNode.Name == "y" || funcCallNode.Name == "z"))
                {
                    double gx, gy, gz;
                    if (!resolveGlobal(argVarRef.Name, out gx, out gy, out gz))
                    {
                        result = 0;
                        return false;
                    }

                    result = funcCallNode.Name == "x" ? gx : funcCallNode.Name == "y" ? gy : gz;
                    return true;
                }
            }

            // Any other function call (min/max/sin/cos/vec, or x/y/z of
            // anything other than a bare global variable) is not evaluated
            // here -- none of the real formulas seen so far need it on the
            // "other side" of the unknown, and guessing at which ones are
            // safe to evaluate isn't worth the risk.
            result = 0;
            return false;
        }

        private static void CollectMatches(Node n, Func<Node, bool> isTarget, List<Node> matches)
        {
            if (n == null)
            {
                return;
            }

            if (isTarget(n))
            {
                matches.Add(n);
            }

            var binOp = n as BinaryOpNode;
            if (binOp != null)
            {
                CollectMatches(binOp.Left, isTarget, matches);
                CollectMatches(binOp.Right, isTarget, matches);
                return;
            }

            var unary = n as UnaryMinusNode;
            if (unary != null)
            {
                CollectMatches(unary.Inner, isTarget, matches);
                return;
            }

            var funcCall = n as FuncCallNode;
            if (funcCall != null)
            {
                foreach (var a in funcCall.Args)
                {
                    CollectMatches(a, isTarget, matches);
                }
            }
        }

        private static bool TryFindUniqueTarget(Node node, Func<Node, bool> isTarget, out Node found)
        {
            var matches = new List<Node>();
            CollectMatches(node, isTarget, matches);
            found = matches.Count == 1 ? matches[0] : null;
            return matches.Count == 1;
        }

        /// <summary>
        /// Finds the UnaryMinusNode that directly wraps <paramref name="target"/>,
        /// if any -- used so splicing a new value in place of a number that
        /// was written as e.g. "-0.05" replaces the WHOLE "-0.05" (operator
        /// included) rather than just the digits, which would otherwise
        /// produce a "--0.1" double-negative when the solved value is
        /// itself negative. Even though that would still parse and
        /// evaluate correctly in this grammar, it's not worth risking
        /// against however the game's own Vizzy editor displays or
        /// re-parses it.
        /// </summary>
        private static UnaryMinusNode FindDirectUnaryParent(Node node, Node target)
        {
            var unary = node as UnaryMinusNode;
            if (unary != null)
            {
                if (ReferenceEquals(unary.Inner, target))
                {
                    return unary;
                }

                return FindDirectUnaryParent(unary.Inner, target);
            }

            var binOp = node as BinaryOpNode;
            if (binOp != null)
            {
                return FindDirectUnaryParent(binOp.Left, target) ?? FindDirectUnaryParent(binOp.Right, target);
            }

            var funcCall = node as FuncCallNode;
            if (funcCall != null)
            {
                foreach (var a in funcCall.Args)
                {
                    var found = FindDirectUnaryParent(a, target);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static bool Contains(Node node, Node target)
        {
            if (ReferenceEquals(node, target))
            {
                return true;
            }

            var binOp = node as BinaryOpNode;
            if (binOp != null)
            {
                return Contains(binOp.Left, target) || Contains(binOp.Right, target);
            }

            var unary = node as UnaryMinusNode;
            if (unary != null)
            {
                return Contains(unary.Inner, target);
            }

            var funcCall = node as FuncCallNode;
            if (funcCall != null)
            {
                return funcCall.Args.Any(a => Contains(a, target));
            }

            return false;
        }

        /// <summary>
        /// Solves the tree for the value the target leaf must have for the
        /// whole expression to evaluate to <paramref name="desiredResult"/>.
        /// Null if the target isn't reachable via a chain of purely linear
        /// operations with every sibling fully resolvable.
        /// </summary>
        private static double? SolveFor(Node node, Node target, double desiredResult, TryResolveGlobalVector resolveGlobal)
        {
            if (ReferenceEquals(node, target))
            {
                return desiredResult;
            }

            var unaryNode = node as UnaryMinusNode;
            if (unaryNode != null)
            {
                return SolveFor(unaryNode.Inner, target, -desiredResult, resolveGlobal);
            }

            var b = node as BinaryOpNode;
            if (b != null)
            {
                var leftHas = Contains(b.Left, target);
                var rightHas = Contains(b.Right, target);
                if (leftHas == rightHas)
                {
                    return null; // target on both sides, or neither -- not solvable this way
                }

                if (leftHas)
                {
                    double rVal;
                    if (!TryEvaluate(b.Right, resolveGlobal, out rVal))
                    {
                        return null;
                    }

                    double? newDesired;
                    if (b.Op == '+') newDesired = desiredResult - rVal;
                    else if (b.Op == '-') newDesired = desiredResult + rVal;
                    else if (b.Op == '*') newDesired = rVal != 0 ? desiredResult / rVal : (double?)null;
                    else if (b.Op == '/') newDesired = desiredResult * rVal;
                    else newDesired = null;

                    return newDesired.HasValue ? SolveFor(b.Left, target, newDesired.Value, resolveGlobal) : null;
                }
                else
                {
                    double lVal;
                    if (!TryEvaluate(b.Left, resolveGlobal, out lVal))
                    {
                        return null;
                    }

                    double? newDesired;
                    if (b.Op == '+') newDesired = desiredResult - lVal;
                    else if (b.Op == '-') newDesired = lVal - desiredResult;
                    else if (b.Op == '*') newDesired = lVal != 0 ? desiredResult / lVal : (double?)null;
                    else if (b.Op == '/') newDesired = desiredResult != 0 ? lVal / desiredResult : (double?)null;
                    else newDesired = null;

                    return newDesired.HasValue ? SolveFor(b.Right, target, newDesired.Value, resolveGlobal) : null;
                }
            }

            return null; // target inside a function call, or anywhere non-linear
        }

        public class SolveResult
        {
            public bool Success;
            public string RewrittenText;
            /// <summary>The bare name to change instead, when it's the only unknown: a custom instruction's parameter, set by its call.</summary>
            public string PendingLocalParameterName;
            public double NewValue;
            public string FailureReason;
        }

        /// <summary>
        /// Attempts to solve <paramref name="expressionText"/> so it
        /// evaluates to <paramref name="desiredResult"/>, treating the
        /// single numeric literal in the expression as the unknown.
        ///
        /// Deliberately only proceeds when there is EXACTLY ONE NumberNode
        /// in the whole expression -- if there were several, we have no
        /// reliable way to tell which one the author intended as "the
        /// tunable value" versus a fixed part of the formula, so it's safer
        /// to fail than guess.
        /// </summary>
        public static SolveResult TrySolve(string expressionText, double desiredResult, TryResolveGlobalVector resolveGlobal)
        {
            Node root;
            try
            {
                root = Parse(expressionText);
            }
            catch (FormatException ex)
            {
                return new SolveResult { Success = false, FailureReason = $"parse error: {ex.Message}" };
            }

            if (TryFindUniqueTarget(root, n => n is NumberNode, out var numberTarget))
            {
                var solved = SolveFor(root, numberTarget, desiredResult, resolveGlobal);
                if (solved.HasValue)
                {
                    // If the number is written as e.g. "-0.05", replace the
                    // WHOLE "-0.05" (operator included), not just the
                    // digits -- otherwise a negative solved value would
                    // double up with the existing "-" in the source text.
                    var unaryParent = FindDirectUnaryParent(root, numberTarget);
                    var spliceStart = unaryParent?.Start ?? numberTarget.Start;
                    var spliceEnd = unaryParent?.End ?? numberTarget.End;
                    var spliceValue = unaryParent != null ? -solved.Value : solved.Value;

                    // A negative in brackets: "2 * -3" or "a - -3" may not read the same in the game's Funk.
                    var spliced = FormatNumber(spliceValue);
                    if (spliceValue < 0)
                    {
                        spliced = "(" + spliced + ")";
                    }

                    var newText = expressionText.Substring(0, spliceStart) + spliced + expressionText.Substring(spliceEnd);
                    return new SolveResult { Success = true, RewrittenText = newText, NewValue = solved.Value };
                }

                return new SolveResult { Success = false, FailureReason = "the literal number isn't reachable through purely linear (+,-,*,/) operations from known values -- formula too complex to preserve" };
            }

            if (TryFindUniqueTarget(root, n => n is VarRefNode v && !v.IsGlobal, out var varTarget))
            {
                var solved = SolveFor(root, varTarget, desiredResult, resolveGlobal);
                if (solved.HasValue)
                {
                    return new SolveResult
                    {
                        Success = false,
                        PendingLocalParameterName = ((VarRefNode)varTarget).Name,
                        NewValue = solved.Value,
                        FailureReason = $"the value comes from the name '{((VarRefNode)varTarget).Name}', which only a custom instruction's call can set",
                    };
                }
            }

            return new SolveResult { Success = false, FailureReason = "no single literal number or subroutine parameter could be solved for (the unknown appears more than once, or only under a nonlinear function)" };
        }
    }
}
