using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Assets.Scripts.Mfd;

public static class MfdFormulaWriterTests
{
    private const double ScreenWidth = 200;
    private const double ScreenHeight = 100;

    /// <summary>One stack under an On Start event, so its blocks can run; more stacks can follow.</summary>
    private static XElement Program(string body)
    {
        return XElement.Parse("<Program name=\"Test\"><Instructions><Event event=\"FlightStart\" />" + body + "</Instructions></Program>");
    }

    private static string Set(string property, string value, string widget = "A")
    {
        return "<SetCraftProperty property=\"Mfd." + property + "\"><Constant text=\"" + widget + "\" />" + value + "</SetCraftProperty>";
    }

    private static string Vec(string x, string y)
    {
        return "<Vector style=\"vec\"><Constant number=\"" + x + "\" /><Constant number=\"" + y + "\" /><Constant number=\"0\" /></Vector>";
    }

    private static string Funk(string text)
    {
        return "<EvaluateExpression><Constant text=\"" + text + "\" /></EvaluateExpression>";
    }

    private static string Assign(string name, string value)
    {
        return "<SetVariable><Variable local=\"false\" variableName=\"" + name + "\" />" + value + "</SetVariable>";
    }

    private static string Global(string name)
    {
        return "<Variable local=\"false\" variableName=\"" + name + "\" />";
    }

    private const string ScreenSize = "<CraftProperty property=\"Mfd.Size\"><Constant text=\"_Screen\" /></CraftProperty>";

    private static readonly string Screen = Assign("Screen", ScreenSize);

    /// <summary>Moves widget A from where it was when the panel opened to the new place.</summary>
    private static List<MfdLine> Move(XElement program, double fromX, double fromY, double x, double y, bool shared = false, MfdProperty property = MfdProperty.Position, bool dryRun = false)
    {
        var change = new MfdChange { Widget = "A", Property = property, X = x, Y = y, FromX = fromX, FromY = fromY };
        return MfdFormulaWriter.Apply(program, new[] { change }, ScreenWidth, ScreenHeight, shared, dryRun);
    }

    private static List<MfdLine> Turn(XElement program, double from, double to)
    {
        return Move(program, from, 0, to, 0, false, MfdProperty.Rotation);
    }

    private static string Text(XElement program)
    {
        return (string)program.Descendants("EvaluateExpression").First().Element("Constant").Attribute("text");
    }

    private static List<string> Numbers(XElement program)
    {
        return program.Descendants("Constant").Select(item => (string)item.Attribute("number") ?? (string)item.Attribute("text"))
            .Where(text => text != "A" && text != "_Screen").ToList();
    }

    private static bool Refused(List<MfdLine> lines) => lines.Count == 1 && !lines[0].Applied;

    private static bool Applied(List<MfdLine> lines) => lines.Count == 1 && lines[0].Applied;

    public static void Run(Action<string, bool> check)
    {
        var literal = Program(Set("SetPosition", Vec("1", "2")));
        check("MFD: a literal vector is applied", Applied(Move(literal, 1, 2, 10, 20)));
        check("MFD: x and y are written, z stays", Numbers(literal).SequenceEqual(new[] { "10", "20", "0" }));

        var formulaInX = Program(Set("SetPosition", "<Vector style=\"vec\"><BinaryOp /><Constant number=\"5\" /><Constant number=\"6\" /></Vector>"));
        var formulaBefore = new XElement(formulaInX);
        check("MFD: a block in x is refused, not written over y", Refused(Move(formulaInX, 0, 5, 1, 2)) && XNode.DeepEquals(formulaInX, formulaBefore));

        var size = Program(Set("SetSize", Vec("1", "2")));
        check("MFD: size uses its own block", Applied(Move(size, 1, 2, 3, 4, false, MfdProperty.Size)) && Refused(Move(size, 1, 2, 3, 4)));

        var rotation = Program(Set("SetRotation", "<Constant number=\"15\" />"));
        check("MFD: a literal rotation", Applied(Turn(rotation, 15, 90)) && Numbers(rotation).Last() == "90");

        var rotationFunk = Program(Set("SetRotation", Funk("2 * x(v:Screen)")) + Screen);
        check("MFD: a rotation formula is solved for the turn made", Applied(Turn(rotationFunk, 40, 100)) && Text(rotationFunk) == "2.3 * x(v:Screen)");

        var solved = Program(Set("SetPosition", Funk("vec(0.5 * x(v:Screen), 10, 0)")) + Screen);
        check("MFD: a formula's one number on each axis is solved", Applied(Move(solved, 100, 10, 50, 20)) && Text(solved) == "vec(0.25 * x(v:Screen), 20, 0)");

        var twoNumbers = Program(Set("SetPosition", Funk("vec(1 + 2 * x(v:Screen), 10, 0)")) + Screen);
        var twoBefore = new XElement(twoNumbers);
        check("MFD: two numbers on an axis are refused", Refused(Move(twoNumbers, 401, 10, 50, 20)) && XNode.DeepEquals(twoNumbers, twoBefore));

        var twice = Program(Set("SetPosition", Vec("1", "2")) + Set("SetPosition", Vec("3", "4")));
        var twiceBefore = new XElement(twice);
        check("MFD: two blocks with different values are refused", Refused(Move(twice, 1, 2, 5, 6)) && XNode.DeepEquals(twice, twiceBefore));

        check("MFD: no block for the widget is refused", Refused(Move(Program(Set("SetPosition", Vec("1", "2"), "B")), 1, 2, 5, 6)));

        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            var comma = Program(Set("SetPosition", Vec("1.5", "2.5")));
            check("MFD: a decimal comma system still reads a decimal point", Applied(Move(comma, 1.5, 2.5, 7.25, 8.5)));
            check("MFD: and writes one", Numbers(comma)[0] == "7.25");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }

        var floats = Program(Set("SetPosition", Vec("1", "2.50")));
        check("MFD: a float's value is written without its noise", Applied(Move(floats, 1, 2.5, (float)123.7, (float)2.5)) && Numbers(floats)[0] == "123.7");
        check("MFD: an axis that didn't move isn't rewritten", Numbers(floats)[1] == "2.50");
        check("MFD: nothing moved gives no line", Move(Program(Set("SetPosition", Vec("1", "2"))), 1, 2, 1, 2).Count == 0);

        var dry = Program(Set("SetPosition", Vec("1", "2")));
        var dryBefore = new XElement(dry);
        check("MFD: a dry run says what it would do and changes nothing", Applied(Move(dry, 1, 2, 5, 6, false, MfdProperty.Position, true)) && XNode.DeepEquals(dry, dryBefore));

        // The block gives (10, 0), but a later Set Parent left the widget showing at (-90, 0).
        var reparented = Program(Set("SetPosition", Vec("10", "0")) + "<SetCraftProperty property=\"Mfd.SetParent\"><Constant text=\"A\" /><Constant text=\"P\" /></SetCraftProperty>");
        var reparentedBefore = new XElement(reparented);
        var moved = Move(reparented, -90, 0, -88, 0);
        check("MFD: a widget something else moves is refused", Refused(moved) && moved[0].Detail.StartsWith("Something else moves") && XNode.DeepEquals(reparented, reparentedBefore));

        var orphan = XElement.Parse("<Program><Instructions><Event event=\"FlightStart\" />" + Set("SetPosition", Vec("1", "2")) + "</Instructions><Instructions>" + Set("SetPosition", Vec("5", "5")) + "</Instructions></Program>");
        check("MFD: a stack that can't run doesn't count", Applied(Move(orphan, 1, 2, 3, 4)) && Numbers(orphan).SequenceEqual(new[] { "3", "4", "0", "5", "5", "0" }));
        check("MFD: and its blocks aren't changed on their own", Refused(Move(XElement.Parse("<Program><Instructions>" + Set("SetPosition", Vec("1", "2")) + "</Instructions></Program>"), 1, 2, 3, 4)));

        var negative = Program(Set("SetRotation", "<Constant number=\"-30\" />"));
        check("MFD: a negative rotation read back as 330 stays negative", Applied(Turn(negative, 330, 340)) && Numbers(negative).Last() == "-20");

        var negativeFunk = Program(Set("SetRotation", Funk("x(v:Screen) - 3")) + Screen);
        check("MFD: a solved negative goes in brackets", Applied(Turn(negativeFunk, 197, 210)) && Text(negativeFunk) == "x(v:Screen) - (-10)");

        var helpers =
            "</Instructions><Instructions><CustomInstruction format=\"First |n| |v| \" name=\"First\" />"
            + "<SetCraftProperty property=\"Mfd.SetPosition\"><Variable local=\"true\" variableName=\"n\" /><Variable local=\"true\" variableName=\"v\" /></SetCraftProperty>"
            + "</Instructions><Instructions><CustomInstruction format=\"Second |a| |pos| |n| \" name=\"Second\" />"
            + "<SetCraftProperty property=\"Mfd.SetPosition\"><Variable local=\"true\" variableName=\"n\" /><Variable local=\"true\" variableName=\"pos\" /></SetCraftProperty>";
        var secondHelper = Program(
            "<CallCustomInstruction call=\"First\"><Constant text=\"B\" />" + Vec("0", "0") + "</CallCustomInstruction>"
            + "<CallCustomInstruction call=\"Second\"><Constant number=\"1\" />" + Vec("3", "4") + "<Constant text=\"A\" /></CallCustomInstruction>"
            + helpers);
        check("MFD: every helper is searched, not only the first", Applied(Move(secondHelper, 3, 4, 7, 8))
            && (string)secondHelper.Descendants("CallCustomInstruction").Last().Descendants("Constant").ElementAt(1).Attribute("number") == "7");

        var calledTwice = Program(
            "<CallCustomInstruction call=\"First\"><Constant text=\"A\" />" + Vec("0", "0") + "</CallCustomInstruction>"
            + "<CallCustomInstruction call=\"First\"><Constant text=\"A\" />" + Vec("1", "1") + "</CallCustomInstruction>"
            + helpers);
        check("MFD: two calls naming the widget are refused", Refused(Move(calledTwice, 0, 0, 7, 8)));

        var both = Program(Set("SetPosition", Vec("0", "0")) + "<CallCustomInstruction call=\"First\"><Constant text=\"A\" />" + Vec("0", "0") + "</CallCustomInstruction>" + helpers);
        check("MFD: a block and a call both setting it are refused", Refused(Move(both, 0, 0, 7, 8)));

        var formulaHelper = Program(
            Screen
            + "<CallCustomInstruction call=\"Sensor\"><Constant text=\"A\" /><Constant number=\"0.2\" /></CallCustomInstruction>"
            + "<CallCustomInstruction call=\"Sensor\"><Constant text=\"B\" /><Constant number=\"0.4\" /></CallCustomInstruction>"
            + "</Instructions><Instructions><CustomInstruction format=\"Sensor |Name| |Position| \" name=\"Sensor\" />"
            + "<SetCraftProperty property=\"Mfd.SetPosition\"><Variable local=\"true\" variableName=\"Name\" />" + Funk("vec(0, Position * y(v:Screen), 0)") + "</SetCraftProperty>");
        var helperBody = Text(formulaHelper);
        var axes = Move(formulaHelper, 0, 20, 5, 50);
        check("MFD: a helper's axes get a line each", axes.Count == 2 && axes[0].Axis == "x" && axes[1].Axis == "y");
        check("MFD: a helper's axis fixed in its body is left alone", !axes[0].Applied);
        check("MFD: a helper's parameter is solved in this call only", axes[1].Applied
            && (string)formulaHelper.Descendants("CallCustomInstruction").First().Descendants("Constant").ElementAt(1).Attribute("number") == "0.5"
            && (string)formulaHelper.Descendants("CallCustomInstruction").Last().Descendants("Constant").ElementAt(1).Attribute("number") == "0.4");
        check("MFD: a helper's body doesn't change", Text(formulaHelper) == helperBody);
        check("MFD: a helper's call that doesn't give the widget's place is refused", Refused(Move(new XElement(formulaHelper), 0, 99, 0, 50)));

        var dedicated = Program(Assign("P", Vec("1", "2")) + Set("SetPosition", Global("P")));
        check("MFD: a variable only this block reads is followed", Applied(Move(dedicated, 1, 2, 5, 6)));

        var sharedProgram = Program(Assign("P", Vec("1", "2")) + Set("SetPosition", Global("P")) + Set("SetPosition", Global("P"), "B"));
        var refused = Move(sharedProgram, 1, 2, 5, 6);
        check("MFD: a shared variable is refused and says the override helps", Refused(refused) && refused[0].NeedsSharedOverride);
        var forced = Move(sharedProgram, 1, 2, 5, 6, true);
        check("MFD: the override changes it and marks it shared", Applied(forced) && forced[0].Shared);

        var readByFunk = Program(Assign("P", Vec("1", "2")) + Set("SetPosition", Global("P")) + Set("SetSize", Funk("vec(P, 1, 0)"), "B"));
        check("MFD: a bare Funk name counts as a read", Refused(Move(readByFunk, 1, 2, 5, 6)));

        var readByAssign = Program(Assign("P", Vec("1", "2")) + Assign("Q", Global("P")) + Set("SetPosition", Global("P")));
        check("MFD: another variable's value counts as a read", Refused(Move(readByAssign, 1, 2, 5, 6)));

        var setTwice = Program(Assign("P", Vec("1", "2")) + Assign("P", Vec("3", "4")) + Set("SetPosition", Global("P")));
        var setTwiceLines = Move(setTwice, 1, 2, 5, 6, true);
        check("MFD: a variable set twice is refused even with the override", Refused(setTwiceLines) && !setTwiceLines[0].NeedsSharedOverride);

        var changed = Program(Assign("P", Vec("1", "2")) + "<ChangeVariable><Variable local=\"false\" variableName=\"P\" /><Constant number=\"1\" /></ChangeVariable>" + Set("SetPosition", Global("P")));
        check("MFD: a variable another kind of block changes is refused, even with the override", Refused(Move(changed, 1, 2, 5, 6, true)));

        var local = Program(Set("SetPosition", "<Variable local=\"true\" variableName=\"P\" />"));
        check("MFD: a local variable is refused", Refused(Move(local, 1, 2, 5, 6)));

        var blocks = Program(Set("SetPosition", "<BinaryOp />"));
        check("MFD: blocks are refused", Refused(Move(blocks, 1, 2, 5, 6)));

        var ambiguousScreen = Program(Set("SetPosition", Funk("vec(0.5 * x(v:Screen), 10, 0)")) + Screen + Screen);
        check("MFD: a global set twice can't be worked out", Refused(Move(ambiguousScreen, 100, 10, 5, 6)));

        var sameTwice = Program(Set("SetPosition", Vec("1", "2")).Replace("<SetCraftProperty ", "<SetCraftProperty id=\"1\" ") + Set("SetPosition", Vec("1", "2")).Replace("<SetCraftProperty ", "<SetCraftProperty id=\"2\" "));
        var sameLines = Move(sameTwice, 1, 2, 5, 6);
        check("MFD: blocks with the same value give one line", Applied(sameLines) && sameLines[0].Detail.Contains("all 2 blocks"));
        check("MFD: and all change", sameTwice.Descendants("Vector").All(item => (string)item.Elements().First().Attribute("number") == "5"));
        check("MFD: ids stay", sameTwice.Descendants("SetCraftProperty").Select(item => (string)item.Attribute("id")).SequenceEqual(new[] { "1", "2" }));

        var indented = XElement.Parse(
            "<Program><Instructions>\n  <Event event=\"FlightStart\" />\n  " + Set("SetPosition", Vec("1", "2")).Replace("><", ">\n    <")
            + "\n  <If>\n    <Instructions>\n      " + Set("SetPosition", Vec("1", "2")).Replace("><", ">\n          <")
            + "\n    </Instructions>\n  </If>\n</Instructions></Program>",
            LoadOptions.PreserveWhitespace);
        check("MFD: indents from a file don't make matching blocks differ", Applied(Move(indented, 1, 2, 5, 6)));

        // As in a real block: -0.46 * x of _Screen's size, 0.455 * y of it.
        string Scaled(string number, string axis) => "<BinaryOp op=\"*\"><Constant text=\"" + number + "\" /><VectorOp op=\"" + axis + "\">" + ScreenSize + "</VectorOp></BinaryOp>";
        var scaled = Program(Set("SetPosition", "<Vector>" + Scaled("-0.46", "x") + Scaled("0.455", "y") + "<Constant number=\"0\" /></Vector>"));
        check("MFD: block formulas on both axes are solved", Applied(Move(scaled, -92, 45.5, 100, 50)));
        check("MFD: and written as the fraction of the screen", Numbers(scaled).SequenceEqual(new[] { "0.5", "0.5", "0" }));

        var mixed = Program(Set("SetPosition", "<Vector>" + Scaled("0.1", "x") + "<Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        check("MFD: a formula and a plain number mix", Applied(Move(mixed, 20, 7, 40, 9)) && Numbers(mixed).SequenceEqual(new[] { "0.2", "9", "0" }));

        var divided = Program(Set("SetRotation", "<BinaryOp op=\"/\"><VectorOp op=\"x\">" + ScreenSize + "</VectorOp><Constant text=\"4\" /></BinaryOp>"));
        check("MFD: dividing by the number solves for it", Applied(Turn(divided, 50, 100)) && Numbers(divided).Last() == "2");

        var subtracted = Program(Set("SetRotation", "<BinaryOp op=\"-\"><VectorOp op=\"y\">" + ScreenSize + "</VectorOp><Constant text=\"4\" /></BinaryOp>"));
        check("MFD: subtracting the number solves for it", Applied(Turn(subtracted, 96, 30)) && Numbers(subtracted).Last() == "70");

        var zero = Program(Set("SetRotation", "<BinaryOp op=\"/\"><Constant text=\"4\" /><BinaryOp op=\"-\"><VectorOp op=\"x\">" + ScreenSize + "</VectorOp><VectorOp op=\"x\">" + ScreenSize + "</VectorOp></BinaryOp></BinaryOp>"));
        check("MFD: dividing by zero is refused", Refused(Turn(zero, 0, 10)));

        var twoInBlocks = Program(Set("SetPosition", "<Vector><BinaryOp op=\"+\"><Constant text=\"1\" /><Constant text=\"2\" /></BinaryOp><Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        var twoInBlocksBefore = new XElement(twoInBlocks);
        check("MFD: two numbers on an axis in blocks are refused, y isn't written either", Refused(Move(twoInBlocks, 3, 7, 5, 6)) && XNode.DeepEquals(twoInBlocks, twoInBlocksBefore));

        var otherOp = Program(Set("SetPosition", "<Vector><BinaryOp op=\"^\"><Constant text=\"2\" /><VectorOp op=\"x\">" + ScreenSize + "</VectorOp></BinaryOp><Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        check("MFD: other operators are refused", Refused(Move(otherOp, 0, 7, 5, 6)));

        var unknownVector = Program(Set("SetPosition", "<Vector><BinaryOp op=\"*\"><Constant text=\"2\" /><VectorOp op=\"x\"><CraftProperty property=\"Mfd.Position\"><Constant text=\"B\" /></CraftProperty></VectorOp></BinaryOp><Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        check("MFD: another widget's value isn't known, so it's refused", Refused(Move(unknownVector, 0, 7, 5, 6)));

        // A shared variable moves its other readers: the evaluator shows by how much.
        var cascade = Program(Assign("P", Vec("1", "2")) + Set("SetPosition", Global("P")) + Set("SetPosition", Funk("vec(x(v:P) + 10, y(v:P), 0)"), "B"));
        var before = MfdFormulaWriter.Evaluator(cascade, ScreenWidth, ScreenHeight)("B", MfdProperty.Position);
        var preview = new XElement(cascade);
        Move(preview, 1, 2, 5, 6, true);
        var after = MfdFormulaWriter.Evaluator(preview, ScreenWidth, ScreenHeight)("B", MfdProperty.Position);
        check("MFD: another reader's value is worked out before", before != null && before[0] == 11 && before[1] == 2);
        check("MFD: and after the shared change", after != null && after[0] == 15 && after[1] == 6);
        // Placed relative to another widget with "get widget A position": it follows A.
        var relative = Program(Set("SetPosition", Vec("10", "20")) + Set("SetPosition", "<Vector><BinaryOp op=\"+\"><Constant number=\"5\" /><VectorOp op=\"x\"><CraftProperty property=\"Mfd.Position\"><Constant text=\"A\" /></CraftProperty></VectorOp></BinaryOp><VectorOp op=\"y\"><CraftProperty property=\"Mfd.Position\"><Constant text=\"A\" /></CraftProperty></VectorOp><Constant number=\"0\" /></Vector>", "B")
            + Set("SetPosition", "<CraftProperty property=\"Mfd.Position\"><Constant text=\"A\" /></CraftProperty>", "C"));
        var relativeBefore = MfdFormulaWriter.Evaluator(relative, ScreenWidth, ScreenHeight)("B", MfdProperty.Position);
        var followed = new XElement(relative);
        Move(followed, 10, 20, 30, 40);
        var relativeAfter = MfdFormulaWriter.Evaluator(followed, ScreenWidth, ScreenHeight)("B", MfdProperty.Position);
        check("MFD: a widget placed from another's position follows it", relativeBefore != null && relativeAfter != null && relativeBefore[0] == 15 && relativeAfter[0] == 35 && relativeAfter[1] == 40);
        var copied = MfdFormulaWriter.Evaluator(followed, ScreenWidth, ScreenHeight)("C", MfdProperty.Position);
        check("MFD: a widget set to another's position follows it", copied != null && copied[0] == 30 && copied[1] == 40);
        var scaledSize = Program(Set("SetSize", Vec("10", "20")) + Set("SetSize", "<BinaryOp op=\"*\"><Constant text=\"0.5\" /><CraftProperty property=\"Mfd.Size\"><Constant text=\"A\" /></CraftProperty></BinaryOp>", "B"));
        var half = MfdFormulaWriter.Evaluator(scaledSize, ScreenWidth, ScreenHeight)("B", MfdProperty.Size);
        check("MFD: a number times another widget's size is worked out", half != null && half[0] == 5 && half[1] == 10);

        var loop = Program(Set("SetPosition", "<CraftProperty property=\"Mfd.Position\"><Constant text=\"B\" /></CraftProperty>") + Set("SetPosition", "<CraftProperty property=\"Mfd.Position\"><Constant text=\"A\" /></CraftProperty>", "B"));
        check("MFD: widgets placed from each other can't be worked out", MfdFormulaWriter.Evaluator(loop, ScreenWidth, ScreenHeight)("A", MfdProperty.Position) == null);
        check("MFD: a widget without its own block can't be worked out", MfdFormulaWriter.Evaluator(cascade, ScreenWidth, ScreenHeight)("C", MfdProperty.Position) == null);

        RealPrograms(check);
    }

    /// <summary>Every property of every named widget in the user's craft files: never throws, only ever changes numbers and Funk text.</summary>
    private static void RealPrograms(Action<string, bool> check)
    {
        var folder = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? string.Empty, "Library/Application Support/com.jundroo.SimpleRockets2/UserData/CraftDesigns");
        if (!Directory.Exists(folder))
        {
            return;
        }

        int programs = 0, applied = 0, refused = 0;
        var clean = true;
        foreach (var file in Directory.GetFiles(folder, "*.xml"))
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(file);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var program in doc.Descendants("Program").Where(item => item.Descendants("SetCraftProperty").Any(block => ((string)block.Attribute("property") ?? string.Empty).StartsWith("Mfd.Set"))))
            {
                programs++;
                var names = program.Descendants("Constant").Select(item => (string)item.Attribute("text")).Where(text => text != null && text.Length < 40).Distinct().Take(60).ToList();
                var changes = names.SelectMany(name => new[] { MfdProperty.Position, MfdProperty.Size, MfdProperty.Rotation }.Select(property => new MfdChange { Widget = name, Property = property, X = 11, Y = 22, FromX = 0, FromY = 0 })).ToList();
                var copy = new XElement(program);
                foreach (var line in MfdFormulaWriter.Apply(copy, changes, ScreenWidth, ScreenHeight, true))
                {
                    if (line.Applied)
                    {
                        applied++;
                    }
                    else
                    {
                        refused++;
                    }
                }

                var before = program.DescendantsAndSelf().ToList();
                var after = copy.DescendantsAndSelf().ToList();
                clean &= before.Count == after.Count && before.Zip(after, (a, b) => a.Name == b.Name && a.Attributes().Count() == b.Attributes().Count()
                    && (a.Name == "Constant" || a.Attributes().All(attribute => (string)b.Attribute(attribute.Name) == attribute.Value))).All(same => same);
            }
        }

        Console.WriteLine($"Real MFD programs: {programs}, {applied} applied, {refused} refused.");
        check("MFD: real programs only change in Constants", clean);
    }
}
