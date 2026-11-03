namespace Assets.Scripts.Exporter
{
    using Assets.Scripts.Vizzy.UI;
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Ui.Inspector;
    using UnityEngine;
    using UnityEngine.UI;

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

            // Toolbox blocks aren't part of the program.
            var editor = block.VizzyUI as VizzyUIScript;
            if (editor == null || !block.transform.IsChildOf(editor.ProgramTransform))
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
            var created = _panel;
            created.Closed += _ =>
            {
                if (_panel == created)
                {
                    _panel = null;
                }
            };

            // Above the editor, which sits last in the same UI.
            _panel.Transform.SetParent(ui.Transform, true);
            _panel.Transform.SetAsLastSibling();
            var rect = (RectTransform)_panel.Transform;
            var camera = PlaceAt(rect, screenPosition);
            rect.gameObject.AddComponent<CloseWhenAbandoned>().Connect(editor, rect, camera);
        }

        /// <summary>Puts the panel's corner at the click, kept on screen. Returns the canvas's camera.</summary>
        private static Camera PlaceAt(RectTransform rect, Vector2 screenPosition)
        {
            var parent = (RectTransform)rect.parent;
            var canvas = rect.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screenPosition, camera, out var world))
            {
                rect.position = world;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            var panelCorners = new Vector3[4];
            var parentCorners = new Vector3[4];
            rect.GetWorldCorners(panelCorners);
            parent.GetWorldCorners(parentCorners);
            var shift = Vector3.zero;
            shift.x = Mathf.Min(0f, parentCorners[2].x - panelCorners[2].x) + Mathf.Max(0f, parentCorners[0].x - panelCorners[0].x);
            shift.y = Mathf.Max(0f, parentCorners[0].y - panelCorners[0].y) + Mathf.Min(0f, parentCorners[2].y - panelCorners[2].y);
            rect.position += shift;
            return camera;
        }

        private static void Close()
        {
            var panel = _panel;
            _panel = null;

            // A scene change destroys the panel without closing it.
            if (panel is Object unityPanel ? unityPanel != null : panel != null)
            {
                panel.Close();
            }
        }

        private static void Export(BlockElementScript block)
        {
            if (!VizzyExportSnippet.TryCapture(block, out var snippet, out var error))
            {
                Game.Instance.UserInterface.CreateMessageDialog(error ?? "Nothing to export.");
                return;
            }

            Game.Instance.UserInterface.CreateListView(new ExportTargetViewModel((VizzyUIScript)block.VizzyUI, snippet));
        }

        /// <summary>Closes the panel on a click elsewhere, or when its editor has gone.</summary>
        private sealed class CloseWhenAbandoned : MonoBehaviour
        {
            private VizzyUIScript _editor;
            private RectTransform _rect;
            private Camera _camera;

            public void Connect(VizzyUIScript editor, RectTransform rect, Camera camera)
            {
                _editor = editor;
                _rect = rect;
                _camera = camera;
            }

            private void Update()
            {
                var clicked = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
                if (_editor == null || !_editor.gameObject.activeInHierarchy
                    || (clicked && !RectTransformUtility.RectangleContainsScreenPoint(_rect, Input.mousePosition, _camera)))
                {
                    ExportMenu.Close();
                }
            }
        }
    }
}
