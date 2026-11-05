namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using Assets.Scripts.Exporter;
    using Assets.Scripts.Vizzy.UI;
    using ModApi.Craft.Program;
    using ModApi.Ui;
    using TMPro;
    using UI.Xml;
    using UnityEngine;

    /// <summary>
    /// Shift+F in the Vizzy editor: type part of a block's name, and matching toolbox blocks appear,
    /// dragged out as usual. Dropdown choices count as their own results, with that choice set.
    /// </summary>
    public sealed class BlockSearchPanel : MonoBehaviour
    {
        private const string FlyoutId = "vizzy-studio-search";
        private const string CloseId = "vizzy-studio-search-close";
        private const string InputId = "vizzy-studio-search-input";
        private const string ResultsId = "vizzy-studio-search-results";
        private const int MaxResults = 30;
        private const float MinWidth = 300f;
        private const float MaxWidth = 600f;

        // Private in the game: it's what makes a block behave as a toolbox block.
        private static readonly MethodInfo LoadNodes = typeof(VizzyUIController).GetMethod("LoadNodes", BindingFlags.Instance | BindingFlags.NonPublic);

        private VizzyUIController _controller;
        private XmlElement _flyout;
        private RectTransform _results;
        private TMP_InputField _input;
        private List<BlockEntry> _entries;
        private string _shownText;
        private int _clearTypedKeyFrame = -1;

        public bool IsOpen => _flyout != null && _flyout.gameObject.activeSelf;

        /// <summary>Adds the flyout to the editor's XML, then connects it once it's built.</summary>
        public static void AddTo(BuildUserInterfaceXmlRequest request)
        {
            XNamespace ns = XmlLayoutConstants.XmlNamespace;
            request.XmlDocument.Descendants(ns + "Panel").First(panel => (string)panel.Attribute("id") == "menu").AddAfterSelf(
                new XElement(ns + "Panel",
                    new XAttribute("id", FlyoutId),
                    new XAttribute("class", "panel flyout"),
                    new XAttribute("height", "100%"),
                    new XAttribute("width", MinWidth.ToString()),
                    new XAttribute("active", "false"),
                    new XElement(ns + "Panel",
                        new XAttribute("class", "flyout-header"),
                        new XElement(ns + "TextMeshPro", new XAttribute("text", "BLOCK SEARCH")),
                        new XElement(ns + "Image",
                            new XAttribute("id", CloseId),
                            new XAttribute("class", "flyout-close-button audio-btn-back"),
                            new XAttribute("onClick", "OnClosePanelClicked();"))),
                    new XElement(ns + "TextMeshProInputField",
                        new XAttribute("id", InputId),
                        new XAttribute("text", string.Empty),
                        new XAttribute("rectAlignment", "UpperCenter"),
                        new XAttribute("height", "30"),
                        new XAttribute("offsetXY", "0 -34")),
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
            layout.GetElementById(CloseId).AddOnClickEvent(Close);
        }

        private void Update()
        {
            try
            {
                if (_flyout == null || LoadNodes == null || !ModSettings.Instance.BlockSearch.Value)
                {
                    return;
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

            return entries;
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
            if (items == null || items.Count == 0)
            {
                entries.Add(new BlockEntry(title, style.Id, category, new Choice(node, null, null)));
                return;
            }

            foreach (var item in items.Where(item => !string.IsNullOrEmpty(item.Id) && !string.IsNullOrEmpty(item.Text)))
            {
                entries.Add(new BlockEntry((title + " " + item.Text).Trim(), style.Id, category, new Choice(node, list.Text, item.Id)));
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
                    nodes.Add(((Choice)entry.Source).Build());
                }
                catch (Exception e)
                {
                    EditorLog.Debug("Block search couldn't build '" + entry.Title + "': " + e.Message);
                }
            }

            var size = (Vector2)LoadNodes.Invoke(_controller, new object[] { nodes, _results, null, 0f });
            _flyout.SetAndApplyAttribute("width", Mathf.Clamp(size.x, MinWidth, MaxWidth).ToString());
        }

        private void ClearResults()
        {
            foreach (Transform child in _results)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        /// <summary>A toolbox block, with one dropdown set to a choice if it has one.</summary>
        private sealed class Choice
        {
            private readonly ProgramNode _node;
            private readonly string _list;
            private readonly string _value;

            public Choice(ProgramNode node, string list, string value)
            {
                _node = node;
                _list = list;
                _value = value;
            }

            /// <summary>A copy when a choice is set: the toolbox's own block has to keep its default.</summary>
            public ProgramNode Build()
            {
                if (_list == null)
                {
                    return _node;
                }

                var copy = ProgramSerializer.DeserializeProgramNode(SerializerBridge.SerializeNode(_node));
                copy.SetListValue(_list, _value);
                return copy;
            }
        }
    }
}
