namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using Assets.Scripts.Exporter;
    using Assets.Scripts.Vizzy.UI;
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Craft.Program;
    using ModApi.Craft.Program.Expressions;
    using ModApi.Craft.Program.Instructions;
    using ModApi.Ui;
    using TMPro;
    using UI.Xml;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

    /// <summary>
    /// Shift+F or the left bar's search button in the Vizzy editor: type part of a block's name, and matching toolbox blocks appear,
    /// dragged out as usual, or double-clicked to be placed in what's visible of the canvas (see BlockPlacement). Arrow keys move a highlight through the results, and Enter places the highlighted one. Dropdown choices count as their own results, with that choice set, as do this program's variables, lists and custom blocks.
    /// </summary>
    public sealed class BlockSearchPanel : MonoBehaviour
    {
        private const string ButtonId = "vizzy-studio-search-button";
        private const string FlyoutId = "vizzy-studio-search";
        private const string CloseId = "vizzy-studio-search-close";
        private const string InputId = "vizzy-studio-search-input";
        private const string ResultsId = "vizzy-studio-search-results";
        private const int MaxResults = 30;
        private const float MinWidth = 300f;
        private const float MaxWidth = 600f;

        // Results start this far in (LoadNodes), so as much room on the other side.
        private const float SideMargin = 25f;
        private const float HighlightPadding = 3f;

        // Room above the first result, whose highlight would otherwise touch the list's top.
        private const float TopMargin = 8f;
        private const float PlacementGap = 20f;
        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.06f;
        private const string Help = "Block Search (Shift+F)";

        // Private in the game: it's what makes a block behave as a toolbox block.
        private static readonly MethodInfo LoadNodes = typeof(VizzyUIController).GetMethod("LoadNodes", BindingFlags.Instance | BindingFlags.NonPublic);

        private VizzyUIController _controller;
        private XmlElement _button;
        private XmlElement _flyout;
        private RectTransform _results;
        private TMP_InputField _input;
        private List<BlockEntry> _entries;
        private string _shownText;
        private int _clearTypedKeyFrame = -1;
        private readonly List<BlockElementScript> _shownBlocks = new List<BlockElementScript>();
        private int _selected = -1;
        private RectTransform _highlight;
        private ScrollRect _scroll;
        private float _nextRepeat;
        private int _refocusUntilFrame = -1;
        private BlockElementScript _lastPlaced;

        public bool IsOpen => _flyout != null && _flyout.gameObject.activeSelf;

        /// <summary>Gets whether the left bar has this panel's button, which Jump To's sits above.</summary>
        public bool HasButton => _button != null;

        /// <summary>Adds the flyout to the editor's XML, then connects it once it's built.</summary>
        public static void AddTo(BuildUserInterfaceXmlRequest request)
        {
            if (!ModSettings.Instance.BlockSearch.Value)
            {
                return;
            }

            XNamespace ns = XmlLayoutConstants.XmlNamespace;
            var panels = request.XmlDocument.Descendants(ns + "Panel").ToList();
            panels.First(panel => (string)panel.Attribute("id") == "menu-button").Parent.Add(
                new XElement(ns + "Panel",
                    new XAttribute("id", ButtonId),
                    new XAttribute("name", "BlockSearch"),
                    new XAttribute("class", "toggle-button audio-btn-click"),
                    new XAttribute("tooltip", Help),
                    new XElement(ns + "Image",
                        new XAttribute("class", "toggle-button-icon"),
                        new XAttribute("sprite", "VizzyStudio/Sprites/IconBlockSearch"))));

            panels.First(panel => (string)panel.Attribute("id") == "menu").AddAfterSelf(
                new XElement(ns + "Panel",
                    new XAttribute("id", FlyoutId),
                    new XAttribute("class", "panel flyout"),
                    new XAttribute("height", "100%"),
                    new XAttribute("width", MinWidth.ToString()),
                    new XAttribute("active", "false"),
                    new XElement(ns + "Panel",
                        new XAttribute("class", "flyout-header"),
                        new XElement(ns + "TextMeshPro", new XAttribute("text", "BLOCK SEARCH"), new XAttribute("tooltip", Help)),
                        new XElement(ns + "Image",
                            new XAttribute("id", CloseId),
                            new XAttribute("class", "flyout-close-button audio-btn-back"),
                            new XAttribute("onClick", "OnClosePanelClicked();"))),
                    new XElement(ns + "TextMeshProInputField",
                        new XAttribute("id", InputId),
                        new XAttribute("text", string.Empty),
                        new XAttribute("rectAlignment", "UpperCenter"),
                        new XAttribute("lineType", "SingleLine"),
                        new XAttribute("width", "90%"),
                        new XAttribute("height", "30"),
                        new XAttribute("offsetXY", "0 -34"),
                        new XElement(ns + "TMP_Placeholder", new XAttribute("text", "Type a block name"), new XAttribute("alignment", "Left")),
                        new XElement(ns + "TMP_Text", new XAttribute("text", string.Empty), new XAttribute("alignment", "Left"))),
                    new XElement(ns + "VerticalScrollView",
                        new XAttribute("class", "no-image"),
                        new XAttribute("offsetMax", "0 -68"),
                        new XElement(ns + "Panel",
                            new XAttribute("id", ResultsId),
                            new XAttribute("pivot", "0 1"),
                            new XAttribute("anchorMin", "0 1"),
                            new XAttribute("anchorMax", "0 1")))));

            request.AddOnLayoutRebuiltAction(controller =>
            {
                if (controller is VizzyUIController vizzy && vizzy.GetComponent<BlockSearchPanel>() == null)
                {
                    vizzy.gameObject.AddComponent<BlockSearchPanel>().Connect(vizzy);
                }
            });
        }

        public void Close()
        {
            if (IsOpen)
            {
                _button.RemoveClass("toggle-button-toggled");
                _flyout.Hide(false, null, false);
            }
        }

        private void Connect(VizzyUIController controller)
        {
            _controller = controller;
            var layout = (XmlLayout)((IXmlLayoutController)controller).XmlLayout;
            _flyout = layout.GetElementById(FlyoutId);
            _results = layout.GetElementById(ResultsId).rectTransform;
            _input = layout.GetElementById(InputId).GetComponentInChildren<TMP_InputField>(true);
            _input.onFocusSelectAll = false;
            _scroll = _results.GetComponentInParent<ScrollRect>(true);

            // The input's default text color is close to this panel's background.
            _input.textComponent.color = Color.white;
            _input.customCaretColor = true;
            _input.caretColor = Color.white;
            if (_input.placeholder != null)
            {
                _input.placeholder.color = new Color32(0xAB, 0xB4, 0xBE, 0xFF);
            }

            layout.GetElementById(CloseId).AddOnClickEvent(Close);
            _button = layout.GetElementById(ButtonId);
            _button.AddOnClickEvent(() =>
            {
                if (IsOpen)
                {
                    Close();
                }
                else
                {
                    Open();
                }
            });
        }

        private void Update()
        {
            try
            {
                if (_flyout == null || LoadNodes == null || !ModSettings.Instance.BlockSearch.Value)
                {
                    return;
                }

                // The game adds its category buttons after this one is built.
                if (_button.transform.GetSiblingIndex() != _button.transform.parent.childCount - 1)
                {
                    _button.transform.SetAsLastSibling();
                }

                var shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
                if (!IsOpen)
                {
                    if (shift && UnityEngine.Input.GetKeyDown(KeyCode.F) && !Game.Instance.UserInterface.IsTextInputFocused)
                    {
                        Open();
                    }

                    return;
                }

                // A stock flyout opening closes this one, as they share the same space.
                if (_controller.SelectedCategoryButton != null || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    Close();
                    return;
                }

                // The key that opened the search can land in the box a moment later.
                if (Time.frameCount <= _clearTypedKeyFrame && _input.text.Length > 0)
                {
                    _input.text = string.Empty;
                }

                if (_input.text != _shownText)
                {
                    ShowResults(_input.text);
                }

                // Another text field, such as a block's, keeps its own keys. This asks what's selected rather than
                // isFocused: Enter can end the box's editing earlier this frame, and the box stays selected.
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (!Game.Instance.UserInterface.IsTextInputFocused || selected == _input.gameObject)
                {
                    HandleKeys();
                }
            }
            catch (Exception e)
            {
                EditorLog.Error("Block search failed: " + e);
                Close();
            }
        }

        /// <summary>After the input field has handled the same keys this frame.</summary>
        private void LateUpdate()
        {
            try
            {
                if (!IsOpen)
                {
                    return;
                }

                // In a single line field, Up and Down send the caret to the start or the end.
                if (_input.isFocused && (ArrowHeld(KeyCode.UpArrow) || ArrowHeld(KeyCode.DownArrow)))
                {
                    _input.MoveTextEnd(false);
                }

                // Enter ends the field's editing, and typing should carry on.
                if (Time.frameCount <= _refocusUntilFrame && !_input.isFocused)
                {
                    _input.Select();
                    _input.ActivateInputField();
                }

                PositionHighlight();
            }
            catch (Exception e)
            {
                EditorLog.Error("Block search failed: " + e);
                Close();
            }
        }

        private static bool ArrowHeld(KeyCode key)
        {
            return UnityEngine.Input.GetKey(key) || UnityEngine.Input.GetKeyUp(key);
        }

        private void HandleKeys()
        {
            if (_shownBlocks.Count == 0)
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                _refocusUntilFrame = Time.frameCount + 2;
                if (_selected >= 0)
                {
                    Place(_shownBlocks[_selected]);
                }

                return;
            }

            var delta = UnityEngine.Input.GetKey(KeyCode.DownArrow) ? 1 : UnityEngine.Input.GetKey(KeyCode.UpArrow) ? -1 : 0;
            if (delta == 0)
            {
                return;
            }

            var pressed = UnityEngine.Input.GetKeyDown(KeyCode.DownArrow) || UnityEngine.Input.GetKeyDown(KeyCode.UpArrow);
            if (pressed)
            {
                _nextRepeat = Time.unscaledTime + RepeatDelay;
            }
            else if (Time.unscaledTime >= _nextRepeat)
            {
                _nextRepeat = Time.unscaledTime + RepeatInterval;
            }
            else
            {
                return;
            }

            _selected = BlockSearch.MoveSelection(_selected, delta, _shownBlocks.Count, pressed);
            PositionHighlight();
            ScrollToSelected();
        }

        /// <summary>Follows the selected block each frame, as the list's width can change.</summary>
        private void PositionHighlight()
        {
            if (_highlight == null)
            {
                return;
            }

            var block = _selected >= 0 && _selected < _shownBlocks.Count ? _shownBlocks[_selected] : null;
            _highlight.gameObject.SetActive(block != null);
            if (block == null)
            {
                return;
            }

            // LoadNodes pins a result by its top left corner and spaces results by Size. The row is as wide as the list.
            var view = (RectTransform)(_scroll != null && _scroll.viewport != null ? _scroll.viewport : _results.parent);
            var top = block.RectTransform.localPosition.y;
            _highlight.localPosition = new Vector3(0f, top - block.Size.y - HighlightPadding, 0f);
            _highlight.sizeDelta = new Vector2(view.rect.width, block.Size.y + (2f * HighlightPadding));
        }

        private void ScrollToSelected()
        {
            if (_scroll == null || _scroll.content == null || _selected < 0)
            {
                return;
            }

            var view = new Vector3[4];
            ((RectTransform)(_scroll.viewport != null ? _scroll.viewport : _scroll.transform)).GetWorldCorners(view);
            var selected = _shownBlocks[_selected];
            var margin = HighlightPadding * _results.lossyScale.y;
            var top = selected.RectTransform.position.y;
            var bottom = top - (selected.Size.y * _results.lossyScale.y);

            // A block taller than the view shows its top.
            var shift = 0f;
            if (top + margin > view[1].y)
            {
                shift = view[1].y - top - margin;
            }
            else if (bottom - margin < view[0].y)
            {
                shift = Mathf.Min(view[0].y - bottom + margin, view[1].y - top - margin);
            }

            _scroll.velocity = Vector2.zero;
            _scroll.content.position += new Vector3(0f, shift, 0f);
        }

        /// <summary>A row behind the selected result, the color the game's dropdowns use on the row under the mouse.</summary>
        private void BuildHighlight()
        {
            var row = new GameObject("SelectedResultHighlight", typeof(RectTransform), typeof(Image));
            _highlight = (RectTransform)row.transform;
            _highlight.SetParent(_results, false);
            _highlight.SetAsFirstSibling();
            _highlight.anchorMin = _highlight.anchorMax = new Vector2(0f, 1f);
            _highlight.pivot = Vector2.zero;
            var image = row.GetComponent<Image>();
            image.color = new Color32(0x5F, 0x61, 0x60, 0xFF);
            image.raycastTarget = false;
        }

        private void Open()
        {
            _controller.OnClosePanelClicked();
            _controller.GetComponent<VizzyOrganizer.VizzyJumpPanel>()?.Close();
            _entries = BuildEntries();
            _button.AddClass("toggle-button-toggled");
            _flyout.Show();
            _input.text = string.Empty;
            _shownText = string.Empty;
            ClearResults();
            _clearTypedKeyFrame = Time.frameCount + 2;
            _input.Select();
            _input.ActivateInputField();
        }

        private List<BlockEntry> BuildEntries()
        {
            var entries = new List<BlockEntry>();
            var toolbox = _controller.VizzyUI.Toolbox;
            foreach (var category in toolbox.Categories)
            {
                foreach (var node in category.Nodes)
                {
                    try
                    {
                        AddEntries(entries, node, toolbox.GetStyle(node.Style), category.DisplayName ?? category.Name);
                    }
                    catch (Exception e)
                    {
                        EditorLog.Debug("Block search skipped a block: " + e.Message);
                    }
                }
            }

            AddProgramEntries(entries, _controller.VizzyUI.FlightProgram);
            return entries;
        }

        /// <summary>What's particular to this program, built the way the toolbox's own categories build it.</summary>
        private static void AddProgramEntries(List<BlockEntry> entries, FlightProgram program)
        {
            foreach (var variable in program.GlobalVariables.Variables.OrderBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase))
            {
                var isList = variable.IsList;
                var name = variable.Name;
                entries.Add(new BlockEntry(name, isList ? "list" : "variable", isList ? "Lists" : "Variables", new Func<ProgramNode>(() => new VariableExpression(isList) { VariableName = name }), isList ? BlockGroup.List : BlockGroup.Variable));
            }

            foreach (var custom in program.CustomExpressions)
            {
                var name = custom.Name;
                var format = custom.CallFormat;
                entries.Add(new BlockEntry(name, "custom expression", "Custom Expressions", new Func<ProgramNode>(() => WithEmptySlots(new CallCustomExpression { Call = name, Style = "call-custom-expression" }, format)), BlockGroup.CustomExpression));
            }

            foreach (var custom in program.CustomInstructions)
            {
                var name = custom.Name;
                var format = custom.CallFormat;
                entries.Add(new BlockEntry(name, "custom instruction", "Custom Instructions", new Func<ProgramNode>(() => WithEmptySlots(new CallCustomInstruction { Call = name, Style = "call-custom-instruction" }, format)), BlockGroup.CustomInstruction));
            }
        }

        private static ProgramNode WithEmptySlots(ProgramNode node, string format)
        {
            var count = NodeFormat.GetNumExpressionsInFormat(format);
            node.InitializeExpressions(new ProgramExpression[count]);
            for (var i = 0; i < count; i++)
            {
                node.SetExpression(i, new ConstantExpression(0d));
            }

            return node;
        }

        private static void AddEntries(List<BlockEntry> entries, ProgramNode node, NodeStyle style, string category)
        {
            if (style == null)
            {
                return;
            }

            var tokens = NodeFormat.Tokenize(style.Format);
            var title = string.Join(" ", tokens.Where(token => token.TokenType == NodeFormat.TokenType.Text).Select(token => token.Text.Trim()).Where(text => text.Length > 0));
            var list = tokens.FirstOrDefault(token => token.TokenType == NodeFormat.TokenType.List);
            var items = list == null ? null : node.GetListItems(list.Text);
            var listName = list?.Text;
            if (items == null || items.Count == 0)
            {
                entries.Add(new BlockEntry(title, style.Id, category, new Func<ProgramNode>(() => node)));
                return;
            }

            foreach (var item in items.Where(item => !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Text)))
            {
                entries.Add(new BlockEntry(title + " " + item.Text, style.Id, category, new Func<ProgramNode>(() => WithChoice(node, listName, item.Id))));
            }
        }

        private void ShowResults(string text)
        {
            _shownText = text;
            ClearResults();
            var nodes = new List<ProgramNode>();
            foreach (var entry in BlockSearch.Find(text, _entries, MaxResults))
            {
                try
                {
                    nodes.Add(((Func<ProgramNode>)entry.Source)());
                }
                catch (Exception e)
                {
                    EditorLog.Debug("Block search couldn't build '" + entry.Title + "': " + e.Message);
                }
            }

            var size = (Vector2)LoadNodes.Invoke(_controller, new object[] { nodes, _results, null, TopMargin });
            foreach (var block in _results.GetComponentsInChildren<BlockElementScript>(true))
            {
                block.gameObject.AddComponent<ResultClick>().Connect(this, block);
            }

            _flyout.SetAndApplyAttribute("width", Mathf.Clamp(size.x + (2f * SideMargin), MinWidth, MaxWidth).ToString());

            // Top-level results are the panel's own children; the old ones are only gone at the end of the frame.
            _shownBlocks.Clear();
            foreach (Transform child in _results)
            {
                var block = child.gameObject.activeSelf ? child.GetComponent<BlockElementScript>() : null;
                if (block != null)
                {
                    _shownBlocks.Add(block);
                }
            }

            _selected = _shownBlocks.Count > 0 ? 0 : -1;
            BuildHighlight();
            PositionHighlight();
            if (_scroll != null)
            {
                _scroll.velocity = Vector2.zero;
                _scroll.verticalNormalizedPosition = 1f;
            }
        }

        /// <summary>A click on a result selects it, so the highlight stays with what was last used.</summary>
        private void SelectResult(BlockElementScript result)
        {
            var index = _shownBlocks.IndexOf(result.Root);
            if (index >= 0)
            {
                _selected = index;
            }
        }

        /// <summary>Enter or a double click: a copy of the result in what's visible of the canvas, without dragging.</summary>
        private void Place(BlockElementScript result)
        {
            var editor = _controller.VizzyUI;
            var (blocks, columns, last) = MeasureCanvas();
            var placed = editor.NodeBuilder.CloneBlock(result.Root, true).First();
            var scale = editor.ProgramTransform.lossyScale.x;
            var size = placed.Size * scale;
            var box = BlockPlacement.Find(VisibleCanvas(), blocks, columns, size.x, size.y, PlacementGap * scale, last);
            var pivot = placed.RectTransform.pivot;
            placed.transform.position = new Vector3(box.XMin + (pivot.x * size.x), box.YMax - ((1f - pivot.y) * size.y), placed.transform.position.z);
            _lastPlaced = placed;
            editor.CreateUndoStep(null);
        }

        /// <summary>The canvas's own area, less what the flyout covers.</summary>
        private PlacementBox VisibleCanvas()
        {
            var canvas = new Vector3[4];
            ((RectTransform)_controller.ProgramContainer.transform).GetWorldCorners(canvas);
            var flyout = new Vector3[4];
            _flyout.rectTransform.GetWorldCorners(flyout);
            return new PlacementBox(Mathf.Max(canvas[0].x, flyout[2].x), canvas[0].y, canvas[2].x, canvas[2].y);
        }

        /// <summary>
        /// Every stack and loose block as one box in world space. Stacks of two or more are columns,
        /// and so is the block last placed from here, so that placing several builds a column.
        /// </summary>
        private (List<PlacementBox> Blocks, List<PlacementBox> Columns, PlacementBox? Last) MeasureCanvas()
        {
            var groups = new Dictionary<BlockElementScript, (PlacementBox Box, int Instructions)>();
            foreach (var block in _controller.VizzyUI.ProgramTransform.GetComponentsInChildren<BlockElementScript>(false))
            {
                var head = HeadOf(block);
                var box = WorldBox(block);
                var instructions = block is InstructionElementScript ? 1 : 0;
                if (groups.TryGetValue(head, out var group))
                {
                    box = new PlacementBox(Mathf.Min(box.XMin, group.Box.XMin), Mathf.Min(box.YMin, group.Box.YMin), Mathf.Max(box.XMax, group.Box.XMax), Mathf.Max(box.YMax, group.Box.YMax));
                    instructions += group.Instructions;
                }

                groups[head] = (box, instructions);
            }

            var lastHead = _lastPlaced != null ? HeadOf(_lastPlaced) : null;
            PlacementBox? last = lastHead != null && groups.TryGetValue(lastHead, out var lastGroup) ? lastGroup.Box : (PlacementBox?)null;
            var columns = groups.Where(pair => pair.Value.Instructions >= 2 || pair.Key == lastHead).Select(pair => pair.Value.Box).ToList();
            return (groups.Values.Select(group => group.Box).ToList(), columns, last);
        }

        /// <summary>The block a stack is known by: the first of its outermost chain, or a loose block's root.</summary>
        private static BlockElementScript HeadOf(BlockElementScript block)
        {
            var root = block.Root;
            var instruction = root as InstructionElementScript ?? block as InstructionElementScript;
            if (instruction == null)
            {
                return root;
            }

            var first = instruction.FirstInstruction;
            while (first.ParentInstruction != null)
            {
                first = first.ParentInstruction.FirstInstruction;
            }

            return first;
        }

        private static PlacementBox WorldBox(BlockElementScript block)
        {
            var rect = block.RectTransform;
            var scale = rect.lossyScale.x;
            var size = block.Size * scale;
            var left = rect.position.x - (rect.pivot.x * size.x);
            var top = rect.position.y + ((1f - rect.pivot.y) * size.y);
            return new PlacementBox(left, top - size.y, left + size.x, top);
        }

        /// <summary>A copy, as the toolbox's own block has to keep its default.</summary>
        private static ProgramNode WithChoice(ProgramNode node, string list, string value)
        {
            var copy = ProgramSerializer.DeserializeProgramNode(SerializerBridge.SerializeNode(node));
            copy.SetListValue(list, value);
            return copy;
        }

        private void ClearResults()
        {
            _shownBlocks.Clear();
            _selected = -1;
            _highlight = null;
            foreach (Transform child in _results)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private sealed class ResultClick : MonoBehaviour, IPointerClickHandler
        {
            private BlockSearchPanel _panel;
            private BlockElementScript _block;

            public void Connect(BlockSearchPanel panel, BlockElementScript block)
            {
                _panel = panel;
                _block = block;
            }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left)
                {
                    return;
                }

                _panel.SelectResult(_block);
                if (eventData.clickCount == 2)
                {
                    try
                    {
                        _panel.Place(_block);
                    }
                    catch (Exception e)
                    {
                        EditorLog.Error("Block search couldn't place a block: " + e);
                    }
                }
            }
        }
    }
}
