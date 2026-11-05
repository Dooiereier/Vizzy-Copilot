namespace Assets.Scripts.VizzyOrganizer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml.Linq;
    using Assets.Scripts.Vizzy.UI;
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Craft.Program;
    using ModApi.Craft.Program.Instructions;
    using ModApi.Ui;
    using TMPro;
    using UI.Xml;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// A Jump To flyout on the Vizzy editor's left bar, listing "//Path/To" tag comments as a tree. Selecting
    /// a row pans the canvas to that folder's chains; nothing is hidden or changed, and tags are plain comments.
    /// Built from the editor's own XML, so it looks like the toolbox and menu flyouts.
    /// </summary>
    public sealed class VizzyJumpPanel : MonoBehaviour
    {
        private const string ButtonId = "vizzy-studio-jump-button";
        private const string FlyoutId = "vizzy-studio-jump";
        private const string CloseId = "vizzy-studio-jump-close";
        private const string RowsId = "vizzy-studio-jump-rows";
        private const string TemplateId = "vizzy-studio-jump-row";
        private const float IndentPerDepth = 16f;
        private const string Help = "Jump To: moves the view to tagged blocks. Tag them with a comment starting with //, such as //Launch, or //Launch/Pitch Program to nest it.";

        private static readonly Color SelectedRow = new Color(0.16f, 0.42f, 0.72f, 1f);

        private readonly List<Image> _rowImages = new List<Image>();
        private readonly List<Color> _rowColors = new List<Color>();
        private readonly List<string> _rowPaths = new List<string>();
        private VizzyUIController _controller;
        private XmlElement _button;
        private XmlElement _flyout;
        private Transform _rows;
        private GameObject _template;
        private VizzyFolderIndex _index = new VizzyFolderIndex();

        // null = the Center row. Otherwise the ids owned by the selected row.
        private HashSet<int> _selectedIds;

        /// <summary>Adds the button and flyout to the editor's XML, then connects them once it's built.</summary>
        public static void AddTo(BuildUserInterfaceXmlRequest request)
        {
            XNamespace ns = XmlLayoutConstants.XmlNamespace;
            var panels = request.XmlDocument.Descendants(ns + "Panel").ToList();
            var menuButton = panels.First(panel => (string)panel.Attribute("id") == "menu-button");
            menuButton.Parent.Add(
                new XElement(ns + "Panel",
                    new XAttribute("id", ButtonId),
                    new XAttribute("name", "JumpTo"),
                    new XAttribute("class", "toggle-button audio-btn-click"),
                    new XAttribute("tooltip", Help),
                    new XElement(ns + "Image",
                        new XAttribute("class", "toggle-button-icon"),
                        new XAttribute("sprite", "VizzyStudio/Sprites/IconJumpTo"))));

            panels.First(panel => (string)panel.Attribute("id") == "menu").AddAfterSelf(
                new XElement(ns + "Panel",
                    new XAttribute("id", FlyoutId),
                    new XAttribute("class", "panel flyout"),
                    new XAttribute("height", "100%"),
                    new XAttribute("width", "300"),
                    new XAttribute("active", "false"),
                    new XElement(ns + "Panel",
                        new XAttribute("class", "flyout-header"),
                        new XElement(ns + "TextMeshPro", new XAttribute("text", "JUMP TO"), new XAttribute("tooltip", Help)),
                        new XElement(ns + "Image",
                            new XAttribute("id", CloseId),
                            new XAttribute("class", "flyout-close-button audio-btn-back"),
                            new XAttribute("onClick", "OnClosePanelClicked();"))),
                    new XElement(ns + "VerticalScrollView",
                        new XAttribute("class", "no-image"),
                        new XAttribute("offsetMax", "0 -30"),
                        new XElement(ns + "VerticalLayout",
                            new XAttribute("id", RowsId),
                            new XAttribute("class", "no-image"),
                            new XAttribute("pivot", "0 1"),
                            new XAttribute("childAlignment", "UpperLeft"),
                            new XAttribute("childForceExpandHeight", "false"),
                            new XAttribute("contentSizeFitter", "vertical"),
                            new XAttribute("padding", "10"),
                            new XAttribute("spacing", "6"),
                            new XElement(ns + "Button",
                                new XAttribute("id", TemplateId),
                                new XAttribute("class", "btn audio-btn-click"),
                                new XAttribute("preferredHeight", "30"),
                                new XAttribute("active", "false"),
                                new XElement(ns + "TextMeshPro", new XAttribute("text", string.Empty)))))));

            request.AddOnLayoutRebuiltAction(controller =>
            {
                if (controller is VizzyUIController vizzy && vizzy.GetComponent<VizzyJumpPanel>() == null)
                {
                    vizzy.gameObject.AddComponent<VizzyJumpPanel>().Connect(vizzy);
                }
            });
        }

        private static Color ColorForName(string name)
        {
            // Stable per name, so a folder's color doesn't shift as others come and go.
            var hash = name.Aggregate(17, (total, c) => unchecked((total * 31) + c));
            return Color.HSVToRGB((hash & 0x7fffffff) % 360 / 360f, 0.55f, 1f);
        }

        private static int GetInstructionId(ProgramInstruction instruction) => ((IInstructionId)instruction).Id;

        private void Connect(VizzyUIController controller)
        {
            _controller = controller;
            var layout = (XmlLayout)((IXmlLayoutController)controller).XmlLayout;
            _flyout = layout.GetElementById(FlyoutId);
            _rows = layout.GetElementById(RowsId).transform;
            _template = layout.GetElementById(TemplateId).gameObject;
            _button = layout.GetElementById(ButtonId);
            _button.AddOnClickEvent(Toggle);
            layout.GetElementById(CloseId).AddOnClickEvent(HideFlyout);
        }

        /// <summary>A stock flyout opening closes this one, as they share the same space.</summary>
        private void Update()
        {
            // The game adds its category buttons after this one is built.
            if (_button != null)
            {
                var search = GetComponent<BlockSearchPanel>();
                var place = _button.transform.parent.childCount - (search != null && search.HasButton ? 2 : 1);
                if (_button.transform.GetSiblingIndex() != place)
                {
                    _button.transform.SetSiblingIndex(place);
                }
            }

            if (_flyout != null && _flyout.gameObject.activeSelf && _controller.SelectedCategoryButton != null)
            {
                HideFlyout();
            }
        }

        private void Toggle()
        {
            if (_flyout.gameObject.activeSelf)
            {
                HideFlyout();
                return;
            }

            _controller.OnClosePanelClicked();
            GetComponent<BlockSearchPanel>()?.Close();
            _button.AddClass("toggle-button-toggled");
            _flyout.Show();
            Refresh();
        }

        public void Close()
        {
            if (_flyout != null && _flyout.gameObject.activeSelf)
            {
                HideFlyout();
            }
        }

        private void HideFlyout()
        {
            _button.RemoveClass("toggle-button-toggled");
            _flyout.Hide(false, null, false);
        }

        /// <summary>Rebuilds the tree from the program as the editor has it now.</summary>
        private void Refresh()
        {
            LoadIndex();
            BuildRows();
            _selectedIds = null;
        }

        /// <summary>Saving renumbers the blocks, so ids from an earlier load no longer mean the same ones.</summary>
        private void LoadIndex()
        {
            var editor = _controller.VizzyUI;

            // Comment text has no public accessor, so it's read from the saved form, keyed by block id.
            var commentTextById = new Dictionary<int, string>();
            foreach (var comment in editor.SaveFlightProgram().Descendants("Comment"))
            {
                if (int.TryParse((string)comment.Attribute("id"), out var id))
                {
                    commentTextById[id] = (string)comment.Element("Constant")?.Attribute("text");
                }
            }

            _index = VizzyFolderIndex.Build(editor.FlightProgram, comment =>
                commentTextById.TryGetValue(GetInstructionId(comment), out var text) ? text : null);
        }

        private void BuildRows()
        {
            foreach (Transform child in _rows)
            {
                if (child.gameObject != _template)
                {
                    Destroy(child.gameObject);
                }
            }

            _rowImages.Clear();
            _rowColors.Clear();
            _rowPaths.Clear();
            AddRow("Center", 0, null, null);
            foreach (var folder in _index.Root.Children.Values.OrderBy(folder => folder.Name))
            {
                AddFolderRows(folder, 1, ColorForName(folder.Name));
            }
        }

        private void AddFolderRows(VizzyFolderNode folder, int depth, Color color)
        {
            AddRow(folder.Name, depth, folder.FullPath, color);
            foreach (var child in folder.Children.Values.OrderBy(child => child.Name))
            {
                AddFolderRows(child, depth + 1, color);
            }
        }

        private void AddRow(string label, int depth, string path, Color? color)
        {
            var row = Instantiate(_template, _rows);
            row.SetActive(true);
            var text = row.GetComponentInChildren<TextMeshProUGUI>(true);
            text.text = label;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.margin = new Vector4(8f + (Mathf.Max(0, depth - 1) * IndentPerDepth), 0f, 4f, 0f);
            text.fontStyle = depth <= 1 ? FontStyles.Bold : FontStyles.Normal;
            if (color != null)
            {
                text.color = color.Value;
            }

            var image = row.GetComponent<Image>();
            _rowImages.Add(image);
            _rowColors.Add(image.color);
            _rowPaths.Add(path);
            row.GetComponent<Button>().onClick.AddListener(() => Select(path));
        }

        /// <summary>Reads the program again first: it can have changed since the rows were built.</summary>
        private void Select(string path)
        {
            LoadIndex();
            var paths = new List<string> { null };
            var folders = new Dictionary<string, VizzyFolderNode>();
            foreach (var folder in _index.Root.Children.Values.OrderBy(folder => folder.Name))
            {
                CollectFolders(folder, paths, folders);
            }

            if (!paths.SequenceEqual(_rowPaths))
            {
                BuildRows();
            }

            var found = path != null && folders.ContainsKey(path);
            for (var i = 0; i < _rowImages.Count; i++)
            {
                _rowImages[i].color = (found || path == null) && _rowPaths[i] == path ? SelectedRow : _rowColors[i];
            }

            if (path != null && !found)
            {
                return;
            }

            _selectedIds = found ? new HashSet<int>(folders[path].GetAllInstructionIds()) : null;
            ApplyFilter();
        }

        private static void CollectFolders(VizzyFolderNode folder, List<string> paths, Dictionary<string, VizzyFolderNode> folders)
        {
            paths.Add(folder.FullPath);
            folders[folder.FullPath] = folder;
            foreach (var child in folder.Children.Values.OrderBy(child => child.Name))
            {
                CollectFolders(child, paths, folders);
            }
        }

        // 0 = bottom of the viewport, 1 = top. 0.9 puts the target near the very top, leaving
        // most of the screen below it for the chain content that extends downward from there.
        private const float TargetVerticalFraction = 0.9f;

        // 0 = left edge of the viewport, 1 = right. 0.4 puts the target left of center.
        private const float TargetHorizontalFraction = 0.4f;

        // Selecting a folder only pans the camera. Nothing is ever hidden or altered.
        private void ApplyFilter()
        {
            var programContainer = _controller.ProgramContainer;
            if (programContainer == null)
                return;

            if (_selectedIds == null)
            {
                PanToCenter();
                return;
            }

            var chainIdGroups = VizzyFolderIndex.GetChainIdGroups(_controller.VizzyUI?.FlightProgram);
            var matchingChains = chainIdGroups.Where(chain => chain.Any(id => _selectedIds.Contains(id))).ToList();

            // Nested blocks too: a loop or if body is a chain of its own.
            var idToBlock = new Dictionary<int, BlockElementScript>();
            foreach (var block in programContainer.transform.GetComponentsInChildren<BlockElementScript>(true))
                if (block.Node is ProgramInstruction && block.Node is IInstructionId withId)
                    idToBlock[withId.Id] = block;

            // One position per chain - its own topmost/head block - not one per instruction,
            // so a long chain doesn't outweigh a short one in the average.
            var chainHeadWorldPositions = new List<Vector3>();
            foreach (var chain in matchingChains)
            {
                foreach (var id in chain)
                {
                    if (idToBlock.TryGetValue(id, out var headBlock))
                    {
                        chainHeadWorldPositions.Add(headBlock.RectTransform.position);
                        break;
                    }
                }
            }

            PanToBoundsCenter(chainHeadWorldPositions);
        }

        // Puts the canvas's origin in the middle of the viewport.
        private void PanToCenter()
        {
            var viewportTransform = _controller.VizzyUI?.ProgramTransform;
            var viewportRect = _controller.ProgramContainer?.transform as RectTransform;
            if (viewportTransform == null || viewportRect == null)
                return;

            var deltaWorld = viewportRect.TransformPoint(viewportRect.rect.center) - viewportTransform.TransformPoint(Vector3.zero);
            var parent = viewportTransform.parent;
            viewportTransform.anchoredPosition += (Vector2)(parent != null ? parent.InverseTransformVector(deltaWorld) : deltaWorld);
        }

        // Pans the viewport to the mean position of each matching chain's own top block - each
        // chain counts once regardless of how many instructions it has, so this lands wherever
        // the relevant chains are clustered, rather than the bounding box's min/max midpoint
        // (which ignores clustering entirely and only depends on the two most extreme points).
        private void PanToBoundsCenter(List<Vector3> visiblePositions)
        {
            var viewportTransform = _controller.VizzyUI?.ProgramTransform;
            var programContainer = _controller.ProgramContainer;
            if (viewportTransform == null || programContainer == null)
                return;

            if (visiblePositions.Count == 0)
                return;

            var sum = Vector3.zero;
            foreach (var pos in visiblePositions)
                sum += pos;
            var meanWorld = sum / visiblePositions.Count;

            // The true, fixed screen reference is the outer viewport frame
            // (ProgramContainerScript's own rect - it receives pan/zoom input but doesn't
            // move itself), not viewportTransform's own world position, which moves with the pan.
            //
            // Biased toward the top (not dead center) - chain content extends downward from
            // its head, so putting the head near the top leaves more room below to actually
            // see it. Also biased left of center horizontally.
            var viewportRect = (RectTransform)programContainer.transform;
            var targetLocal = new Vector3(
                viewportRect.rect.xMin + viewportRect.rect.width * TargetHorizontalFraction,
                viewportRect.rect.yMin + viewportRect.rect.height * TargetVerticalFraction,
                0f);
            var fixedTargetWorld = viewportRect.TransformPoint(targetLocal);

            var deltaWorld = fixedTargetWorld - meanWorld;
            var parent = viewportTransform.parent;
            var deltaLocal = parent != null ? parent.InverseTransformVector(deltaWorld) : deltaWorld;
            viewportTransform.anchoredPosition += (Vector2)deltaLocal;
        }
    }
}
