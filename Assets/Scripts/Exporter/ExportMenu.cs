namespace Assets.Scripts.Exporter
{
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Ui.Inspector;
    using UnityEngine;

    /// <summary>The right-click menu on a block: Export To, in the game's own inspector panel.</summary>
    public static class ExportMenu
    {
        private const float DebounceSeconds = 0.1f;

        private static IInspectorPanel _panel;
        private static float _lastOpenTime = -1f;

        /// <summary>Several click handlers can fire for one click, so only the first counts.</summary>
        public static void ShowFor(BlockElementScript block, Vector2 screenPosition)
        {
            if (Time.unscaledTime - _lastOpenTime < DebounceSeconds || block == null || block.Node == null)
            {
                return;
            }

            _lastOpenTime = Time.unscaledTime;
            Close();

            var model = new InspectorModel("VizzyStudioExport", "Vizzy Block");
            model.Add(new TextButtonModel("Export To...", _ =>
            {
                Close();
                Export(block);
            }));

            var ui = Game.Instance.UserInterface;
            _panel = ui.CreateInspectorPanel(model, new InspectorPanelCreationInfo { Resizable = false, CanPin = false });
            _panel.Closed += _ => _panel = null;

            // Above the editor, which sits last in the same UI.
            _panel.Transform.SetParent(ui.Transform, true);
            _panel.Transform.SetAsLastSibling();
            _panel.Position = screenPosition;
        }

        private static void Close()
        {
            var panel = _panel;
            _panel = null;
            panel?.Close();
        }

        private static void Export(BlockElementScript block)
        {
            if (!VizzyExportSnippet.TryCapture(block, out var snippet, out var error))
            {
                Game.Instance.UserInterface.CreateMessageDialog(error ?? "Nothing to export.");
                return;
            }

            Game.Instance.UserInterface.CreateListView(new ExportTargetViewModel((Assets.Scripts.Vizzy.UI.VizzyUIScript)block.VizzyUI, snippet));
        }
    }
}
