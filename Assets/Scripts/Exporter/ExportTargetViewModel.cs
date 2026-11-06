namespace Assets.Scripts.Exporter
{
    using System.Collections.Generic;
    using System.IO;
    using Assets.Scripts.Menu.ListView;
    using Assets.Scripts.Vizzy.UI;
    using ModApi.Ui;

    /// <summary>Chooses the program to export to, in the same list the game uses for Load Program.</summary>
    public class ExportTargetViewModel : LoadProgramViewModel
    {
        private readonly VizzyUIScript _editor;
        private readonly VizzyExportSnippet _snippet;

        public ExportTargetViewModel(VizzyUIScript editor, VizzyExportSnippet snippet) : base(editor, false)
        {
            _editor = editor;
            _snippet = snippet;
        }

        public override void OnListViewInitialized(ListViewScript listView)
        {
            base.OnListViewInitialized(listView);
            listView.Title = Text.Get("VizzyStudio.Export.Title");
            listView.PrimaryButtonText = Text.Get("VizzyStudio.Export.Button");
            listView.CanDelete = false;
        }

        public override void OnPrimaryButtonClicked(ListViewItemScript selectedItem)
        {
            if (selectedItem?.ItemModel is FileInfo file)
            {
                // Saving the open program would write over the export.
                if (string.Equals(Path.GetFileNameWithoutExtension(file.Name), _editor.FlightProgram?.Name, System.StringComparison.OrdinalIgnoreCase))
                {
                    Game.Instance.UserInterface.CreateMessageDialog(Text.Get("VizzyStudio.Export.SameProgram"));
                    return;
                }

                ListView.Close();
                Report(file.FullName);
            }
        }

        private void Report(string path)
        {
            var dialogs = Game.Instance.UserInterface;
            if (!VizzyProgramFileExporter.TryExport(_snippet, path, out var counts, out var error))
            {
                dialogs.CreateMessageDialog(error ?? Text.Get("VizzyStudio.Export.Failed"));
                return;
            }

            var created = new List<string>();
            AddCount(created, counts.VariablesCreated, "VizzyStudio.Export.Variables");
            AddCount(created, counts.CustomExpressionsCreated, "VizzyStudio.Export.CustomExpressions");
            AddCount(created, counts.CustomInstructionsCreated, "VizzyStudio.Export.CustomInstructions");
            var note = created.Count > 0 ? " " + Text.Get("VizzyStudio.Export.Created", string.Join(Text.Get("VizzyStudio.Export.ListSeparator"), created)) : string.Empty;
            dialogs.CreateMessageDialog(Text.Get("VizzyStudio.Export.Done", Path.GetFileNameWithoutExtension(path)) + note);
        }

        private static void AddCount(List<string> notes, int count, string key)
        {
            if (count > 0)
            {
                notes.Add(Text.Count(key, count));
            }
        }
    }
}
