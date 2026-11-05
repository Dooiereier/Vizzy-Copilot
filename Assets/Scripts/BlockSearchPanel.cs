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

    /// <summary>
    /// Shift+F or the left bar's search button in the Vizzy editor: type part of a block's name, and matching toolbox blocks appear,
    /// dragged out as usual, or double-clicked to appear in the middle of the view. Dropdown choices count as their own results, with that choice set, as do this program's variables, lists and custom blocks.
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
        private const string Help = "Block Search (Shift+F): type part of a block's name or a dropdown choice, such as + or wait, then drag the block out or double-click it.";

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
            }
            catch (Exception e)
            {
                EditorLog.Error("Block search failed: " + e);
                Close();
            }
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

            var size = (Vector2)LoadNodes.Invoke(_controller, new object[] { nodes, _results, null, 0f });
            foreach (var block in _results.GetComponentsInChildren<BlockElementScript>(true))
            {
                block.gameObject.AddComponent<ResultClick>().Connect(this, block);
            }

            _flyout.SetAndApplyAttribute("width", Mathf.Clamp(size.x, MinWidth, MaxWidth).ToString());
        }

        /// <summary>Double click: a copy of the result in the middle of the view, without dragging.</summary>
        private void PlaceAtCenter(BlockElementScript result)
        {
            var root = result.Root;
            var editor = _controller.VizzyUI;
            var viewport = (RectTransform)_controller.ProgramContainer.transform;
            var placed = editor.NodeBuilder.CloneBlock(root, true).First();
            placed.transform.position = viewport.TransformPoint(viewport.rect.center);
            editor.CreateUndoStep(null);
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
                if (eventData.button == PointerEventData.InputButton.Left && eventData.clickCount == 2)
                {
                    try
                    {
                        _panel.PlaceAtCenter(_block);
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
