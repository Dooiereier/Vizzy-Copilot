namespace Assets.Scripts.Vizzy
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Assets.Scripts.Vizzy.UI;
    using ModApi.Craft.Program;
    using ModApi.Craft.Program.Expressions;
    using UnityEngine;

    /// <summary>
    /// Removes global variables nothing in the open program uses, through the console command
    /// CleanUnusedVariables. A variable can be used by block, by name in Funk text, or by name in a list
    /// function, so all of those count. Another part's program or an Input modifier reading it through
    /// Part.VZ.Name can't be seen from here; Undo brings variables back.
    /// </summary>
    public static class VizzyVariableCleaner
    {
        public static void CleanCurrentEditingProgram()
        {
            var editor = UnityEngine.Object.FindObjectOfType<VizzyUIScript>();
            if (editor == null || editor.FlightProgram == null)
            {
                EditorLog.Info("Open a Vizzy program in the editor first, then run CleanUnusedVariables.");
                return;
            }

            // The program tree only catches up with the blocks at an undo step, so make one first.
            editor.CreateUndoStep(null);
            var unused = GetUnusedVariableNames(editor.FlightProgram, out var funkUnreadable);
            if (funkUnreadable)
            {
                // Funk text built by other blocks could name any variable.
                editor.ShowMessage("No variables removed: a Funk block's text is built by other blocks.");
                EditorLog.Info("Variables not cleaned: a Funk block's text is built by other blocks.");
                return;
            }

            foreach (var name in unused)
            {
                editor.DeleteVariable(name);
            }

            editor.ShowMessage(unused.Count + " unused variable(s) removed.");
            EditorLog.Info("Removed " + unused.Count + " unused variable(s): " + string.Join(", ", unused));
        }

        /// <summary>The program's global variables that nothing in it uses. Changes nothing.</summary>
        public static IReadOnlyList<string> GetUnusedVariableNames(FlightProgram program, out bool funkUnreadable)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var unreadable = false;
            foreach (var root in AllRoots(program))
            {
                ProgramNode.ExecuteActionOnTree<VariableExpression>(root, variable =>
                {
                    // Locals aren't in GlobalVariables.
                    if (!variable.IsLocal && !string.IsNullOrEmpty(variable.VariableName))
                    {
                        used.Add(variable.VariableName);
                    }
                });

                ProgramNode.ExecuteActionOnTree<EvaluationExpression>(root, funk =>
                {
                    if (funk.GetExpression(0) is ConstantExpression text)
                    {
                        used.UnionWith(FunkCompletion.NamesIn(text.ExpressionResult.TextValue));
                    }
                    else
                    {
                        unreadable = true;
                    }
                });
            }

            funkUnreadable = unreadable;
            return program.GlobalVariables.Variables
                .Select(variable => variable.Name)
                .Where(name => !used.Contains(name))
                .ToList();
        }

        private static IEnumerable<ProgramNode> AllRoots(FlightProgram program)
        {
            return program.RootInstructions.Cast<ProgramNode>()
                .Concat(program.RootExpressions)
                .Concat(program.CustomInstructions)
                .Concat(program.CustomExpressions);
        }
    }
}
