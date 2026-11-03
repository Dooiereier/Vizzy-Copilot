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
            listView.Title = "Export To";
            listView.PrimaryButtonText = "Export";
            listView.CanDelete = false;
        }

        public override void OnPrimaryButtonClicked(ListViewItemScript selectedItem)
        {
            if (selectedItem?.ItemModel is FileInfo file)
            {
                // Saving the open program would write over the export.
                if (string.Equals(Path.GetFileNameWithoutExtension(file.Name), _editor.FlightProgram?.Name, System.StringComparison.OrdinalIgnoreCase))
                {
                    Game.Instance.UserInterface.CreateMessageDialog("That's the program you have open. Choose another one.");
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
                dialogs.CreateMessageDialog(error ?? "Export failed.");
                return;
            }

            var created = new List<string>();
            AddCount(created, counts.VariablesCreated, "global variable", "global variables");
            AddCount(created, counts.CustomExpressionsCreated, "custom expression", "custom expressions");
            AddCount(created, counts.CustomInstructionsCreated, "custom instruction", "custom instructions");
            var note = created.Count > 0 ? " Created " + string.Join(" and ", created) + " it needed." : string.Empty;
            dialogs.CreateMessageDialog("Exported to " + Path.GetFileNameWithoutExtension(path) + "." + note
                + " It'll show up as a new block group next time that program is loaded.");
        }

        private static void AddCount(List<string> notes, int count, string singular, string plural)
        {
            if (count > 0)
            {
                notes.Add(count + " " + (count == 1 ? singular : plural));
            }
        }
    }
}
