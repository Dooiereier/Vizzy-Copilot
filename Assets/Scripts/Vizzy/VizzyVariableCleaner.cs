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
    /// Removes global variables nothing in the open program uses, after asking, from the editor's Clean
    /// Variables button or the console command CleanUnusedVariables. A variable can be used by block, by name in
    /// Funk text, or by name in a list function, so all of those count. Another part's program or an Input
    /// modifier reading it through Part.VZ.Name can't be seen from here; Undo brings variables back.
    /// </summary>
    public static class VizzyVariableCleaner
    {
        private const int MaxNamesListed = 20;

        /// <summary>From the console, which doesn't say which editor.</summary>
        public static void CleanOpenEditor()
        {
            var editor = UnityEngine.Object.FindObjectOfType<VizzyUIScript>();
            if (editor == null || editor.FlightProgram == null)
            {
                EditorLog.Info("Open a Vizzy program in the editor first, then run CleanUnusedVariables.");
                return;
            }

            AskAndClean(editor);
        }

        public static void AskAndClean(VizzyUIScript editor)
        {
            // The program tree only catches up with the blocks when it's saved.
            editor.SaveFlightProgram();
            var unused = GetUnusedVariableNames(editor.FlightProgram, out var funkUnreadable);
            if (funkUnreadable)
            {
                // Funk text built by other blocks could name any variable.
                editor.ShowMessage("No variables removed: a Funk block's text is built by other blocks.");
                return;
            }

            if (unused.Count == 0)
            {
                editor.ShowMessage("No unused variables.");
                return;
            }

            var names = string.Join(", ", unused.Take(MaxNamesListed));
            if (unused.Count > MaxNamesListed)
            {
                names += " and " + (unused.Count - MaxNamesListed) + " more";
            }

            var dialog = Game.Instance.UserInterface.CreateMessageDialog(ModApi.Ui.MessageDialogType.OkayCancel, null, true);
            dialog.ExtraWide = true;
            dialog.MessageText = "Remove " + unused.Count + " unused variable(s)? " + names
                + ". Another part's program or an Input modifier could still read them by name. Undo brings them back.";
            dialog.OkayClicked += _ =>
            {
                dialog.Close();
                editor.CreateUndoStep(null);
                foreach (var name in unused)
                {
                    editor.DeleteVariable(name);
                }

                editor.ShowMessage(unused.Count + " unused variable(s) removed.");
                EditorLog.Info("Removed " + unused.Count + " unused variable(s): " + string.Join(", ", unused));
            };
        }

        /// <summary>The program's global variables that nothing in it uses. Changes nothing.</summary>
        public static IReadOnlyList<string> GetUnusedVariableNames(FlightProgram program, out bool funkUnreadable)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var unreadable = false;
            foreach (var root in FunkNames.AllRoots(program))
            {
                FunkNames.Walk(root, node =>
                {
                    // Locals aren't in GlobalVariables.
                    if (node is VariableExpression variable && !variable.IsLocal && !string.IsNullOrEmpty(variable.VariableName))
                    {
                        used.Add(variable.VariableName);
                    }
                    else if (FunkNames.TypedVariableName(node) is string typed)
                    {
                        used.Add(typed);
                    }
                    else if (node is EvaluationExpression funk)
                    {
                        if (funk.GetExpression(0) is ConstantExpression text)
                        {
                            used.UnionWith(FunkCompletion.NamesIn(text.ExpressionResult.TextValue));
                        }
                        else
                        {
                            unreadable = true;
                        }
                    }
                });
            }

            funkUnreadable = unreadable;
            return program.GlobalVariables.Variables
                .Select(variable => variable.Name)
                .Where(name => !used.Contains(name))
                .ToList();
        }
    }
}
