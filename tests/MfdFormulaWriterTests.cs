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

    private static XElement Program(string body)
    {
        return XElement.Parse("<Program name=\"Test\"><Instructions>" + body + "</Instructions></Program>");
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

    private static List<MfdLine> Move(XElement program, double x, double y, bool shared = false, MfdProperty property = MfdProperty.Position)
    {
        return MfdFormulaWriter.Apply(program, new[] { new MfdChange { Widget = "A", Property = property, X = x, Y = y } }, ScreenWidth, ScreenHeight, shared);
    }

    private static string Text(XElement program)
    {
        return (string)program.Descendants("EvaluateExpression").First().Element("Constant").Attribute("text");
    }

    public static void Run(Action<string, bool> check)
    {
        var literal = Program(Set("SetPosition", Vec("1", "2")));
        check("MFD: a literal vector is applied", Move(literal, 10, 20).Single().Applied);
        var numbers = literal.Descendants("Constant").Where(item => item.Attribute("number") != null).ToList();
        check("MFD: x and y are written, z stays", (string)numbers[0].Attribute("number") == "10" && (string)numbers[1].Attribute("number") == "20" && (string)numbers[2].Attribute("number") == "0");

        var formulaInX = Program(Set("SetPosition", "<Vector style=\"vec\"><BinaryOp /><Constant number=\"5\" /><Constant number=\"6\" /></Vector>"));
        var formulaBefore = new XElement(formulaInX);
        check("MFD: a block in x is refused, not written over y", !Move(formulaInX, 1, 2).Single().Applied && XNode.DeepEquals(formulaInX, formulaBefore));

        var size = Program(Set("SetSize", Vec("1", "2")));
        check("MFD: size uses its own block", Move(size, 3, 4, false, MfdProperty.Size).Single().Applied && !Move(size, 3, 4).Single().Applied);

        var rotation = Program(Set("SetRotation", "<Constant number=\"15\" />"));
        check("MFD: a literal rotation", Move(rotation, 90, 0, false, MfdProperty.Rotation).Single().Applied
            && (string)rotation.Descendants("Constant").Last().Attribute("number") == "90");

        var rotationFunk = Program(Set("SetRotation", Funk("2 * x(v:Screen)")) + Assign("Screen", "<CraftProperty property=\"Mfd.Size\"><Constant text=\"_Screen\" /></CraftProperty>"));
        check("MFD: a rotation formula is solved", Move(rotationFunk, 100, 0, false, MfdProperty.Rotation).Single().Applied && Text(rotationFunk) == "0.5 * x(v:Screen)");

        var screen = Assign("Screen", "<CraftProperty property=\"Mfd.Size\"><Constant text=\"_Screen\" /></CraftProperty>");
        var solved = Program(Set("SetPosition", Funk("vec(0.5 * x(v:Screen), 10, 0)")) + screen);
        check("MFD: a formula's one number on each axis is solved", Move(solved, 50, 20).Single().Applied && Text(solved) == "vec(0.25 * x(v:Screen), 20, 0)");

        var twoNumbers = Program(Set("SetPosition", Funk("vec(1 + 2 * x(v:Screen), 10, 0)")) + screen);
        var twoBefore = new XElement(twoNumbers);
        check("MFD: two numbers on an axis are refused", !Move(twoNumbers, 50, 20).Single().Applied && XNode.DeepEquals(twoNumbers, twoBefore));

        var twice = Program(Set("SetPosition", Vec("1", "2")) + Set("SetPosition", Vec("3", "4")));
        var twiceBefore = new XElement(twice);
        check("MFD: two blocks with different values are refused", !Move(twice, 5, 6).Single().Applied && XNode.DeepEquals(twice, twiceBefore));

        var sameTwice = Program(Set("SetPosition", Vec("1", "2")).Replace("<SetCraftProperty ", "<SetCraftProperty id=\"1\" ") + Set("SetPosition", Vec("1", "2")).Replace("<SetCraftProperty ", "<SetCraftProperty id=\"2\" "));
        var sameLines = Move(sameTwice, 5, 6);
        check("MFD: blocks with the same value give one line", sameLines.Count == 1 && sameLines[0].Applied && sameLines[0].Detail.Contains("all 2 blocks"));
        check("MFD: and all change", sameTwice.Descendants("Vector").All(item => (string)item.Elements().First().Attribute("number") == "5"));
        check("MFD: ids stay", sameTwice.Descendants("SetCraftProperty").Select(item => (string)item.Attribute("id")).SequenceEqual(new[] { "1", "2" }));

        // As in a real block: -0.46 * x of _Screen's size, 0.455 * y of it.
        var screenSize = "<CraftProperty property=\"Mfd.Size\"><Constant text=\"_Screen\" /></CraftProperty>";
        string Scaled(string number, string axis) => "<BinaryOp op=\"*\"><Constant text=\"" + number + "\" /><VectorOp op=\"" + axis + "\">" + screenSize + "</VectorOp></BinaryOp>";
        var scaled = Program(Set("SetPosition", "<Vector>" + Scaled("-0.46", "x") + Scaled("0.455", "y") + "<Constant number=\"0\" /></Vector>"));
        check("MFD: block formulas on both axes are solved", Move(scaled, 100, 50).Single().Applied);
        var scaledNumbers = scaled.Descendants("Constant").Select(item => (string)item.Attribute("text") ?? (string)item.Attribute("number")).Where(text => text != "_Screen" && text != "A").ToList();
        check("MFD: and written as the fraction of the screen", scaledNumbers.SequenceEqual(new[] { "0.5", "0.5", "0" }));

        var mixed = Program(Set("SetPosition", "<Vector>" + Scaled("0.1", "x") + "<Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        check("MFD: a formula and a plain number mix", Move(mixed, 40, 9).Single().Applied && (string)mixed.Descendants("Constant").Last(item => item.Attribute("number") != null && item.Attribute("number").Value != "0").Attribute("number") == "9");

        var divided = Program(Set("SetRotation", "<BinaryOp op=\"/\"><VectorOp op=\"x\">" + screenSize + "</VectorOp><Constant text=\"4\" /></BinaryOp>"));
        check("MFD: dividing by the number solves for it", Move(divided, 100, 0, false, MfdProperty.Rotation).Single().Applied && (string)divided.Descendants("Constant").Last().Attribute("text") == "2");

        var subtracted = Program(Set("SetRotation", "<BinaryOp op=\"-\"><VectorOp op=\"y\">" + screenSize + "</VectorOp><Constant text=\"4\" /></BinaryOp>"));
        check("MFD: subtracting the number solves for it", Move(subtracted, 30, 0, false, MfdProperty.Rotation).Single().Applied && (string)subtracted.Descendants("Constant").Last().Attribute("text") == "70");

        var twoInBlocks = Program(Set("SetPosition", "<Vector><BinaryOp op=\"+\"><Constant text=\"1\" /><Constant text=\"2\" /></BinaryOp><Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        var twoInBlocksBefore = new XElement(twoInBlocks);
        check("MFD: two numbers on an axis in blocks are refused, y isn't written either", !Move(twoInBlocks, 5, 6).Single().Applied && XNode.DeepEquals(twoInBlocks, twoInBlocksBefore));

        var otherOp = Program(Set("SetPosition", "<Vector><BinaryOp op=\"^\"><Constant text=\"2\" /><VectorOp op=\"x\">" + screenSize + "</VectorOp></BinaryOp><Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        check("MFD: other operators are refused", !Move(otherOp, 5, 6).Single().Applied);

        var unknownVector = Program(Set("SetPosition", "<Vector><BinaryOp op=\"*\"><Constant text=\"2\" /><VectorOp op=\"x\"><CraftProperty property=\"Mfd.Position\"><Constant text=\"B\" /></CraftProperty></VectorOp></BinaryOp><Constant number=\"7\" /><Constant number=\"0\" /></Vector>"));
        check("MFD: another widget's value isn't known, so it's refused", !Move(unknownVector, 5, 6).Single().Applied);

        check("MFD: no block for the widget is refused", !Move(Program(Set("SetPosition", Vec("1", "2"), "B")), 5, 6).Single().Applied);

        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            var comma = Program(Set("SetPosition", Vec("1.5", "2.5")));
            check("MFD: a decimal comma system still reads a decimal point", Move(comma, 7.25, 8.5).Single().Applied);
            check("MFD: and writes one", (string)comma.Descendants("Constant").ElementAt(1).Attribute("number") == "7.25");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }

        var helpers =
            "</Instructions><Instructions><CustomInstruction format=\"First |n| |v| \" name=\"First\" />"
            + "<SetCraftProperty property=\"Mfd.SetPosition\"><Variable local=\"true\" variableName=\"n\" /><Variable local=\"true\" variableName=\"v\" /></SetCraftProperty>"
            + "</Instructions><Instructions><CustomInstruction format=\"Second |a| |pos| |n| \" name=\"Second\" />"
            + "<SetCraftProperty property=\"Mfd.SetPosition\"><Variable local=\"true\" variableName=\"n\" /><Variable local=\"true\" variableName=\"pos\" /></SetCraftProperty>";
        var secondHelper = Program(
            "<CallCustomInstruction call=\"First\"><Constant text=\"B\" />" + Vec("0", "0") + "</CallCustomInstruction>"
            + "<CallCustomInstruction call=\"Second\"><Constant number=\"1\" />" + Vec("3", "4") + "<Constant text=\"A\" /></CallCustomInstruction>"
            + helpers);
        check("MFD: every helper is searched, not only the first", Move(secondHelper, 7, 8).Single().Applied
            && (string)secondHelper.Descendants("CallCustomInstruction").Last().Descendants("Constant").ElementAt(1).Attribute("number") == "7");

        var calledTwice = Program(
            "<CallCustomInstruction call=\"First\"><Constant text=\"A\" />" + Vec("0", "0") + "</CallCustomInstruction>"
            + "<CallCustomInstruction call=\"First\"><Constant text=\"A\" />" + Vec("1", "1") + "</CallCustomInstruction>"
            + helpers);
        check("MFD: two calls naming the widget are refused", !Move(calledTwice, 7, 8).Single().Applied);

        var formulaHelper = Program(
            screen
            + "<CallCustomInstruction call=\"Sensor\"><Constant text=\"A\" /><Constant number=\"0.2\" /></CallCustomInstruction>"
            + "<CallCustomInstruction call=\"Sensor\"><Constant text=\"B\" /><Constant number=\"0.4\" /></CallCustomInstruction>"
            + "</Instructions><Instructions><CustomInstruction format=\"Sensor |Name| |Position| \" name=\"Sensor\" />"
            + "<SetCraftProperty property=\"Mfd.SetPosition\"><Variable local=\"true\" variableName=\"Name\" />" + Funk("vec(0, Position * y(v:Screen), 0)") + "</SetCraftProperty>");
        var helperBody = Text(formulaHelper);
        var axes = Move(formulaHelper, 5, 50);
        check("MFD: a helper's axis fixed in its body is left alone", !axes[0].Applied);
        check("MFD: a helper's parameter is solved in this call only", axes[1].Applied
            && (string)formulaHelper.Descendants("CallCustomInstruction").First().Descendants("Constant").ElementAt(1).Attribute("number") == "0.5"
            && (string)formulaHelper.Descendants("CallCustomInstruction").Last().Descendants("Constant").ElementAt(1).Attribute("number") == "0.4");
        check("MFD: a helper's body doesn't change", Text(formulaHelper) == helperBody);

        var dedicated = Program(Assign("P", Vec("1", "2")) + Set("SetPosition", Global("P")));
        check("MFD: a variable only this block reads is followed", Move(dedicated, 5, 6).Single().Applied);

        var sharedProgram = Program(Assign("P", Vec("1", "2")) + Set("SetPosition", Global("P")) + Set("SetPosition", Global("P"), "B"));
        var refused = Move(sharedProgram, 5, 6).Single();
        check("MFD: a shared variable is refused and says the override helps", !refused.Applied && refused.NeedsSharedOverride);
        var forced = Move(sharedProgram, 5, 6, true).Single();
        check("MFD: the override changes it and marks it shared", forced.Applied && forced.Shared);

        var readByFunk = Program(Assign("P", Vec("1", "2")) + Set("SetPosition", Global("P")) + Set("SetSize", Funk("vec(P, 1, 0)"), "B"));
        check("MFD: a bare Funk name counts as a read", !Move(readByFunk, 5, 6).Single().Applied);

        var readByAssign = Program(Assign("P", Vec("1", "2")) + Assign("Q", Global("P")) + Set("SetPosition", Global("P")));
        check("MFD: another variable's value counts as a read", !Move(readByAssign, 5, 6).Single().Applied);

        var setTwice = Program(Assign("P", Vec("1", "2")) + Assign("P", Vec("3", "4")) + Set("SetPosition", Global("P")));
        var setTwiceLine = Move(setTwice, 5, 6, true).Single();
        check("MFD: a variable set twice is refused even with the override", !setTwiceLine.Applied && !setTwiceLine.NeedsSharedOverride);

        var local = Program(Set("SetPosition", "<Variable local=\"true\" variableName=\"P\" />"));
        check("MFD: a local variable is refused", !Move(local, 5, 6).Single().Applied);

        var blocks = Program(Set("SetPosition", "<BinaryOp />"));
        check("MFD: blocks are refused", !Move(blocks, 5, 6).Single().Applied);

        var ambiguousScreen = Program(Set("SetPosition", Funk("vec(0.5 * x(v:Screen), 10, 0)")) + screen + screen);
        check("MFD: a global set twice can't be worked out", !Move(ambiguousScreen, 5, 6).Single().Applied);

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
                var changes = names.SelectMany(name => new[] { MfdProperty.Position, MfdProperty.Size, MfdProperty.Rotation }.Select(property => new MfdChange { Widget = name, Property = property, X = 11, Y = 22 })).ToList();
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
