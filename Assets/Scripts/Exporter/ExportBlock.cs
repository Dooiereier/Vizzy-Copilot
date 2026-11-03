namespace Assets.Scripts.Exporter
{
    using Assets.Scripts.Vizzy.UI;
    using Assets.Scripts.Vizzy.UI.Elements;

    /// <summary>The Export Block button: exports the block last clicked on the canvas.</summary>
    public static class ExportBlock
    {
        private static BlockElementScript _selected;

        public static void Select(BlockElementScript block)
        {
            // Toolbox blocks aren't part of the program.
            var editor = block == null ? null : block.VizzyUI as VizzyUIScript;
            if (editor == null || block.Node == null || !block.transform.IsChildOf(editor.ProgramTransform))
            {
                return;
            }

            _selected = block;
        }

        public static void Run(VizzyUIScript editor)
        {
            if (_selected == null || _selected.VizzyUI as VizzyUIScript != editor)
            {
                editor.ShowMessage("Click on a block to export to another Vizzy.");
                return;
            }

            var block = _selected;
            if (!VizzyExportSnippet.TryCapture(block, out var snippet, out var error))
            {
                Game.Instance.UserInterface.CreateMessageDialog(error ?? "Nothing to export.");
                return;
            }

            Game.Instance.UserInterface.CreateListView(new ExportTargetViewModel(editor, snippet));
        }
    }
}
