namespace Assets.Scripts.Mfd
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;
    using Assets.Scripts.Craft.Parts.Modifiers;
    using Assets.Scripts.Craft.Parts.Modifiers.Mfd;
    using ModApi.Craft.Program.Craft;
    using ModApi.Ui.Inspector;
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// The Arrange MFD panel for one MFD. The flight is held still while it's open, so a running program
    /// can't overwrite what's being typed. Edits change the live widgets at once; Save writes them into
    /// the program and the craft's files, and closing puts back what wasn't saved.
    /// </summary>
    public sealed class MfdEditorSession
    {
        private const string ScreenWidgetName = "_Screen";
        private const int PanelWidth = 440;
        private const float MaxScreenHeight = 0.9f;
        private const int WidgetsClassifiedPerFrame = 3;
        private const int LabelLineHeight = 22;
        private const int LabelCharactersPerLine = 52;
        private const float MinSize = 1f;
        private static readonly string[] Steps = { "1", "10", "100" };

        // Private: the game only offers lookups by name, and no list of a screen's widgets.
        private static readonly FieldInfo WidgetsField = typeof(MfdScript).GetField("_widgets", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly FlightProgramData _data;
        private readonly MfdWidgetOutline _outline = new MfdWidgetOutline();
        private readonly InspectorRowPaint _rowPaint = new InspectorRowPaint();
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<ItemModel> _selectedValues = new List<ItemModel>();
        private readonly List<LabelModel> _wrappedLabels = new List<LabelModel>();

        private MfdScript _mfd;
        private IInspectorPanel _panel;
        private GroupModel _selectedGroup;
        private GroupModel _changesGroup;
        private Row _selected;
        private XElement _classifyProgram;
        private int _classifyNext;
        private float _step = 10f;
        private bool _allowShared;
        private string _message;

        public MfdEditorSession(FlightProgramData data)
        {
            _data = data;
        }

        public bool IsOpen => _panel != null;

        public void Open()
        {
            _mfd = _data.Part.PartScript.GetModifier<MfdScript>();
            var widgets = WidgetsField?.GetValue(_mfd) as IDictionary<string, IMfdWidget>;
            var scene = Game.Instance.FlightScene;
            if (widgets == null)
            {
                EditorLog.Error("Couldn't read the MFD's widgets: MfdScript._widgets has changed.");
                scene.FlightSceneUI.ShowMessage("Arrange MFD doesn't work with this game version.");
                return;
            }

            FlightPause.Hold();
            try
            {
                this.BuildPanel(widgets);
            }
            catch
            {
                FlightPause.Release();
                throw;
            }
        }

        private void BuildPanel(IDictionary<string, IMfdWidget> widgets)
        {
            foreach (var pair in widgets.Where(item => item.Key != ScreenWidgetName && item.Value?.Transform != null).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                _rows.Add(new Row(pair.Value));
            }

            var model = new InspectorModel("VizzyStudioArrangeMfd", "Arrange MFD: " + _data.Part.DisplayName);
            _selectedGroup = this.BuildSelectedGroup();
            model.AddGroup(_selectedGroup);
            _changesGroup = this.BuildChangesGroup();
            model.AddGroup(_changesGroup);
            var list = new GroupModel("Widgets");
            model.AddGroup(list);
            foreach (var row in _rows)
            {
                row.Button = list.Add(new TextButtonModel(row.Name, _ => this.Select(row)));
            }

            var info = new InspectorPanelCreationInfo
            {
                StartPosition = InspectorPanelCreationInfo.InspectorStartPosition.UpperLeft,
                Resizable = true,
                PanelWidth = PanelWidth,
                PanelMaxHeight = MaxScreenHeight,
            };
            _panel = Game.Instance.UserInterface.CreateInspectorPanel(model, info);
            _panel.Closed += this.OnPanelClosed;
            _classifyProgram = new XElement(_data.FlightProgramXml);
        }

        public void Close()
        {
            var panel = _panel;
            if (panel != null)
            {
                panel.Close();
                this.Finish();
            }
        }

        /// <summary>Each frame from the flight controller.</summary>
        public void Update()
        {
            if (_panel == null)
            {
                return;
            }

            if (_mfd == null || _data.Script == null)
            {
                this.Close();
                return;
            }

            // The program could remove a widget before the flight was held, or after a load.
            if (_selected != null && !_selected.IsAlive)
            {
                this.Select(null);
            }

            // Building rows can reset their colors, and they appear after the panel opens.
            if (_selected != null)
            {
                _rowPaint.Paint(_selected.Button, true);
            }

            foreach (var label in _wrappedLabels)
            {
                var text = label.ItemElement?.GameObject == null ? null : label.ItemElement.GameObject.GetComponentInChildren<TMP_Text>(true);
                if (text != null && !text.enableWordWrapping)
                {
                    text.enableWordWrapping = true;
                }
            }

            this.ClassifySome();
        }

        private static string Number(float value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string ShortName(MfdProperty property)
        {
            switch (property)
            {
                case MfdProperty.Position: return "position";
                case MfdProperty.Size: return "size";
                default: return "rotation";
            }
        }

        /// <summary>Works out over several frames what each widget can save, as a large program takes a moment per widget.</summary>
        private void ClassifySome()
        {
            for (var i = 0; i < WidgetsClassifiedPerFrame && _classifyNext < _rows.Count; i++)
            {
                var row = _rows[_classifyNext++];
                if (row.IsAlive && row.Button != null)
                {
                    var lines = this.Classify(row);
                    row.Button.Label = row.Name + "  -  " + StatusText(lines);
                    row.Button.Tooltip = string.Join("\n", lines.Select(line => line.ToString()));
                }
            }
        }

        private List<MfdLine> Classify(Row row)
        {
            this.ScreenSize(out var width, out var height);
            return MfdFormulaWriter.Apply(_classifyProgram, row.AllChanges(), width, height, false);
        }

        private static string StatusText(List<MfdLine> lines)
        {
            var parts = new List<string>();
            foreach (var property in new[] { MfdProperty.Position, MfdProperty.Size, MfdProperty.Rotation })
            {
                var own = lines.Where(line => line.Property == property).ToList();
                if (own.Any(line => line.Applied))
                {
                    parts.Add(ShortName(property));
                }
                else if (own.Any(line => line.NeedsSharedOverride))
                {
                    parts.Add(ShortName(property) + " (shared)");
                }
            }

            return parts.Count == 0 ? "can't save" : "saves " + string.Join(", ", parts);
        }

        private void ScreenSize(out double width, out double height)
        {
            var screen = _mfd.GetWidget(ScreenWidgetName);
            width = screen?.Size.x ?? 0;
            height = screen?.Size.y ?? 0;
        }

        private void Select(Row row)
        {
            if (_selected != null && _selected.Button != null)
            {
                _rowPaint.Paint(_selected.Button, false);
            }

            _selected = row;
            if (row != null)
            {
                _outline.Show(row.Widget);
            }
            else
            {
                _outline.Hide();
            }

            this.ReplaceSelectedGroup();
        }

        private void ReplaceSelectedGroup()
        {
            var group = this.BuildSelectedGroup();
            _panel.ReplaceGroup(_selectedGroup, group);
            _selectedGroup = group;
        }

        private void ReplaceChangesGroup()
        {
            var group = this.BuildChangesGroup();
            _panel.ReplaceGroup(_changesGroup, group);
            _changesGroup = group;
        }

        private GroupModel BuildSelectedGroup()
        {
            _selectedValues.Clear();
            var group = new GroupModel("Selected widget");
            var row = _selected;
            if (row == null)
            {
                group.Add(new LabelModel("Choose a widget in the list below.", ElementAlignment.Left));
                return group;
            }

            group.Add(new LabelModel(row.Name, ElementAlignment.Left));
            this.AddValue(group, "X", () => row.Widget.LocalPosition.x, value => row.SetPosition(new Vector2(value, row.Widget.LocalPosition.y)));
            this.AddValue(group, "Y", () => row.Widget.LocalPosition.y, value => row.SetPosition(new Vector2(row.Widget.LocalPosition.x, value)));
            group.Add(this.IconRow(
                Icon("Ui/Sprites/Design/IconSpinnerArrowLeft", "Move left", () => row.Move(new Vector2(-_step, 0f))),
                Icon("Ui/Sprites/Design/IconSpinnerArrowRight", "Move right", () => row.Move(new Vector2(_step, 0f))),
                Icon("Ui/Sprites/Common/IconMoveUp", "Move up", () => row.Move(new Vector2(0f, _step))),
                Icon("Ui/Sprites/Common/IconMoveDown", "Move down", () => row.Move(new Vector2(0f, -_step)))));

            this.AddValue(group, "Width", () => row.Widget.Size.x, value => row.SetSize(new Vector2(value, row.Widget.Size.y)));
            this.AddValue(group, "Height", () => row.Widget.Size.y, value => row.SetSize(new Vector2(row.Widget.Size.x, value)));
            group.Add(this.IconRow(
                Icon("Ui/Sprites/Design/IconSpinnerArrowLeft", "Narrower", () => row.Grow(new Vector2(-_step, 0f))),
                Icon("Ui/Sprites/Design/IconSpinnerArrowRight", "Wider", () => row.Grow(new Vector2(_step, 0f))),
                Icon("Ui/Sprites/Common/IconMoveUp", "Taller", () => row.Grow(new Vector2(0f, _step))),
                Icon("Ui/Sprites/Common/IconMoveDown", "Shorter", () => row.Grow(new Vector2(0f, -_step)))));

            this.AddValue(group, "Rotation", () => row.Widget.LocalRotation, value => row.Widget.LocalRotation = value);
            group.Add(this.IconRow(
                Icon("Ui/Sprites/Design/IconSpinnerArrowLeft", "Rotation down", () => row.Widget.LocalRotation -= _step),
                Icon("Ui/Sprites/Design/IconSpinnerArrowRight", "Rotation up", () => row.Widget.LocalRotation += _step)));

            group.Add(new DropdownModel(
                "Step",
                () => Number(_step),
                value => _step = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                Steps));
            return group;
        }

        private void AddValue(GroupModel group, string label, Func<float> get, Action<float> set)
        {
            _selectedValues.Add(group.Add(new FloatInputModel(
                label,
                get,
                value => this.Run(() => set(value)),
                null,
                null,
                Number)));
        }

        private IconButtonRowModel IconRow(params IconButtonModel[] icons)
        {
            var row = new IconButtonRowModel();
            foreach (var icon in icons)
            {
                row.Add(icon);
            }

            return row;
        }

        private IconButtonModel Icon(string sprite, string tooltip, Action action)
        {
            return new IconButtonModel(sprite, _ => this.Run(action), tooltip);
        }

        /// <summary>A value changed: the fields and the list of changes follow.</summary>
        private void Run(Action action)
        {
            try
            {
                action();
                _message = null;
                foreach (var value in _selectedValues)
                {
                    value.Update();
                }

                this.ReplaceChangesGroup();
            }
            catch (Exception e)
            {
                EditorLog.Error("Couldn't change the widget: " + e);
            }
        }

        private List<MfdChange> PendingChanges()
        {
            return _rows.Where(row => row.IsAlive).SelectMany(row => row.PendingChanges()).ToList();
        }

        private GroupModel BuildChangesGroup()
        {
            _wrappedLabels.Clear();
            var group = new GroupModel("Changes");
            if (_message != null)
            {
                this.AddWrapped(group, _message);
            }

            var changes = this.PendingChanges();
            if (changes.Count == 0)
            {
                if (_message == null)
                {
                    group.Add(new LabelModel("Nothing changed yet.", ElementAlignment.Left));
                }

                return group;
            }

            this.ScreenSize(out var width, out var height);
            var lines = MfdFormulaWriter.Apply(new XElement(_data.FlightProgramXml), changes, width, height, _allowShared);
            foreach (var line in lines)
            {
                this.AddWrapped(group, (line.Applied ? string.Empty : "Won't save: ") + line);
            }

            if (_allowShared || lines.Any(line => line.NeedsSharedOverride))
            {
                group.Add(new ToggleModel(
                    "Also change shared variables",
                    () => _allowShared,
                    value =>
                    {
                        _allowShared = value;
                        this.ReplaceChangesGroup();
                    },
                    "A shared variable moves everything that reads it, at the next launch."));
            }

            group.Add(new TextButtonModel("Save", _ => this.Save()));
            group.Add(new TextButtonModel("Revert", _ => this.Run(this.RevertAll)));
            return group;
        }

        private void AddWrapped(GroupModel group, string text)
        {
            var label = group.Add(new LabelModel(text, ElementAlignment.Left));
            label.PreferredHeight = LabelLineHeight * Mathf.Max(1, Mathf.CeilToInt((float)text.Length / LabelCharactersPerLine));
            _wrappedLabels.Add(label);
        }

        private void RevertAll()
        {
            foreach (var row in _rows.Where(item => item.IsAlive))
            {
                row.Revert();
            }
        }

        /// <summary>
        /// The running program first, the way Edit Vizzy's Apply does it, so threads carry on and a quickload
        /// keeps the edit. The craft's files only follow if that worked.
        /// </summary>
        private void Save()
        {
            try
            {
                var changes = this.PendingChanges();
                if (changes.Count == 0)
                {
                    return;
                }

                this.ScreenSize(out var width, out var height);
                var before = _data.FlightProgramXml;
                var program = new XElement(before);
                var lines = MfdFormulaWriter.Apply(program, changes, width, height, _allowShared);
                foreach (var line in lines)
                {
                    EditorLog.Debug((line.Applied ? "MFD save: " : "MFD not saved: ") + line);
                }

                if (!lines.Any(line => line.Applied))
                {
                    _message = "Nothing could be saved.";
                    this.ReplaceChangesGroup();
                    return;
                }

                if (!ProgramSwapper.Swap(_data, program, out var swapMessage))
                {
                    _message = swapMessage;
                    this.ReplaceChangesGroup();
                    return;
                }

                LatestEdits.Remember(CraftPrograms.Key(_data), before, program);
                _message = MfdCraftFiles.Write(_data, changes, lines, width, height, _allowShared);

                if (lines.Any(line => line.Shared))
                {
                    _message += " Other widgets reading the shared variable move at the next launch.";
                }

                // What couldn't be saved stays listed, so it can still be reverted.
                foreach (var row in _rows.Where(item => item.IsAlive))
                {
                    row.AcceptSaved(lines);
                }

                _classifyProgram = new XElement(program);
                _classifyNext = 0;
                this.ReplaceChangesGroup();
            }
            catch (Exception e)
            {
                EditorLog.Error("Couldn't save the MFD changes: " + e);
                _message = "Saving failed: see Player.log.";
                this.ReplaceChangesGroup();
            }
        }

        private void OnPanelClosed(object sender)
        {
            this.Finish();
        }

        /// <summary>Whichever way the panel closed, once. At a scene's end the widgets and the flight may be gone.</summary>
        private void Finish()
        {
            if (_panel == null)
            {
                return;
            }

            _panel = null;
            try
            {
                this.RevertAll();
                _outline.Hide();
            }
            catch (Exception e)
            {
                EditorLog.Debug("MFD panel closed while the scene ends: " + e.Message);
            }

            try
            {
                FlightPause.Release();
            }
            catch (Exception e)
            {
                EditorLog.Debug("Flight not released while the scene ends: " + e.Message);
            }
        }

        /// <summary>One widget, and its values when the panel opened or last saved.</summary>
        private sealed class Row
        {
            private Vector2 _position;
            private Vector2 _size;
            private float _rotation;

            public Row(IMfdWidget widget)
            {
                this.Widget = widget;
                this.Name = widget.Name;
                _position = widget.LocalPosition;
                _size = widget.Size;
                _rotation = widget.LocalRotation;
            }

            public IMfdWidget Widget { get; }

            public string Name { get; }

            public TextButtonModel Button { get; set; }

            /// <summary>A destroyed widget's own properties throw, so its Unity object is asked first.</summary>
            public bool IsAlive => (!(this.Widget is UnityEngine.Object unity) || unity != null) && this.Widget.Transform != null;

            public void SetPosition(Vector2 value)
            {
                this.Widget.LocalPosition = value;
            }

            public void Move(Vector2 delta)
            {
                this.Widget.LocalPosition += delta;
            }

            public void SetSize(Vector2 value)
            {
                this.Widget.Size = new Vector2(Mathf.Max(MinSize, value.x), Mathf.Max(MinSize, value.y));
            }

            public void Grow(Vector2 delta)
            {
                this.SetSize(this.Widget.Size + delta);
            }

            public void Revert()
            {
                this.Widget.LocalPosition = _position;
                this.Widget.Size = _size;
                this.Widget.LocalRotation = _rotation;
            }

            /// <summary>Every property at its current value, for finding out what could be saved.</summary>
            public IEnumerable<MfdChange> AllChanges()
            {
                yield return this.Change(MfdProperty.Position);
                yield return this.Change(MfdProperty.Size);
                yield return this.Change(MfdProperty.Rotation);
            }

            public IEnumerable<MfdChange> PendingChanges()
            {
                if (this.Widget.LocalPosition != _position)
                {
                    yield return this.Change(MfdProperty.Position);
                }

                if (this.Widget.Size != _size)
                {
                    yield return this.Change(MfdProperty.Size);
                }

                if (!Mathf.Approximately(this.Widget.LocalRotation, _rotation))
                {
                    yield return this.Change(MfdProperty.Rotation);
                }
            }

            public void AcceptSaved(IEnumerable<MfdLine> lines)
            {
                foreach (var line in lines.Where(item => item.Applied && item.Widget == this.Name))
                {
                    switch (line.Property)
                    {
                        case MfdProperty.Position:
                            _position = this.Widget.LocalPosition;
                            break;
                        case MfdProperty.Size:
                            _size = this.Widget.Size;
                            break;
                        default:
                            _rotation = this.Widget.LocalRotation;
                            break;
                    }
                }
            }

            private MfdChange Change(MfdProperty property)
            {
                switch (property)
                {
                    case MfdProperty.Position:
                        return new MfdChange { Widget = this.Name, Property = property, X = this.Widget.LocalPosition.x, Y = this.Widget.LocalPosition.y };
                    case MfdProperty.Size:
                        return new MfdChange { Widget = this.Name, Property = property, X = this.Widget.Size.x, Y = this.Widget.Size.y };
                    default:
                        return new MfdChange { Widget = this.Name, Property = property, X = this.Widget.LocalRotation };
                }
            }
        }
    }
}
