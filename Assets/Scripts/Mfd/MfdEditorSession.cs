namespace Assets.Scripts.Mfd
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
        private const int PanelWidth = 340;
        private const float MaxScreenHeight = 0.9f;
        private const int WidgetsClassifiedPerFrame = 3;
        private const int LabelLineHeight = 22;
        private const int LabelCharactersPerLine = 40;
        private const float MinSize = 1f;
        private static readonly string[] Steps = { "0.2", "2", "20" };

        // Rotation's degrees need bigger clicks than position and size.
        private const float RotationStepFactor = 5f;

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
        private GroupModel _listGroup;
        private string _shownList;
        private Row _selected;
        private XElement _classifyProgram;
        private float _step = 2f;
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
            var widgets = MfdRestart.Widgets(_mfd);
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
            foreach (var pair in widgets.Where(item => item.Key != MfdRestart.ScreenWidgetName && item.Value?.Transform != null).OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                _rows.Add(new Row(pair.Value));
            }

            var model = new InspectorModel("VizzyStudioArrangeMfd", "Arrange MFD");
            _selectedGroup = this.BuildSelectedGroup();
            model.AddGroup(_selectedGroup);
            _changesGroup = this.BuildChangesGroup();
            model.AddGroup(_changesGroup);

            // Before the panel exists: replacing a group before its Start leaves a dead element behind,
            // which throws in the panel's update every frame and stops the rows after it from updating.
            _classifyProgram = new XElement(_data.FlightProgramXml);
            this.ClassifyAll();
            _listGroup = this.BuildListGroup();
            model.AddGroup(_listGroup);

            var info = new InspectorPanelCreationInfo
            {
                StartPosition = InspectorPanelCreationInfo.InspectorStartPosition.UpperLeft,
                Resizable = true,
                PanelWidth = PanelWidth,
                PanelMaxHeight = MaxScreenHeight,
            };
            _panel = Game.Instance.UserInterface.CreateInspectorPanel(model, info);
            _panel.Closed += this.OnPanelClosed;
        }

        public void Close()
        {
            var panel = _panel;
            if (panel != null)
            {
                try
                {
                    panel.Close();
                }
                catch (Exception e)
                {
                    // At the game's end the panel may already be gone.
                    EditorLog.Debug("MFD panel not closed: " + e.Message);
                }

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
            if (_selected?.Button != null)
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
        }

        private static string Number(float value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Works out what each widget can save. The flight is held, so a moment on a large program does no harm.</summary>
        private void ClassifyAll()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var row in _rows)
            {
                row.CanArrange = false;
                if (row.IsAlive)
                {
                    var lines = this.Classify(row);
                    row.CanArrange = lines.Any(line => line.Property != MfdProperty.Rotation && (line.Applied || line.NeedsSharedOverride));
                    row.Label = row.Name + StatusSuffix(lines);
                    row.Tooltip = string.Join("\n", lines.Select(line => line.ToString()));
                }
            }

            this.ScreenSize(out var width, out var height);
            EditorLog.Debug("MFD list: " + _rows.Count(row => row.CanArrange) + " of " + _rows.Count + " widgets listed, screen " + width + " x " + height + ", " + clock.ElapsedMilliseconds + " ms.");
            foreach (var row in _rows.Where(item => !item.CanArrange))
            {
                EditorLog.Debug("MFD widget " + row.Name + " not listed: " + (row.Tooltip ?? "it's gone"));
            }
        }

        /// <summary>Only the widgets that saving can move or resize.</summary>
        private GroupModel BuildListGroup()
        {
            var shown = _rows.Where(row => row.CanArrange).ToList();
            _shownList = string.Join("\n", shown.Select(row => row.Label));
            var group = new GroupModel("Widgets");
            foreach (var row in _rows)
            {
                row.Button = null;
            }

            foreach (var row in shown)
            {
                row.Button = group.Add(new TextButtonModel(row.Label, _ => this.Select(row)));
                row.Button.Tooltip = row.Tooltip;
            }

            if (shown.Count == 0)
            {
                this.AddWrappedTo(group, "No widget here can be moved or resized and saved. A program may build more of its screen later: open this again then.");
            }

            return group;
        }

        /// <summary>After a save, only if the list differs, as rebuilding it resets its rows.</summary>
        private void RefreshList()
        {
            if (string.Join("\n", _rows.Where(row => row.CanArrange).Select(row => row.Label)) != _shownList)
            {
                var group = this.BuildListGroup();
                _panel.ReplaceGroup(_listGroup, group);
                _listGroup = group;
            }
        }

        private List<MfdLine> Classify(Row row)
        {
            this.ScreenSize(out var width, out var height);
            return MfdFormulaWriter.Apply(_classifyProgram, row.AllChanges(), width, height, false);
        }

        /// <summary>Nothing when position and size both save: only the exceptions need saying. The tooltip has the details.</summary>
        private static string StatusSuffix(List<MfdLine> lines)
        {
            bool Saves(MfdProperty property) => lines.Any(line => line.Property == property && (line.Applied || line.NeedsSharedOverride));
            var parts = new List<string>();
            if (!Saves(MfdProperty.Size))
            {
                parts.Add("only position");
            }
            else if (!Saves(MfdProperty.Position))
            {
                parts.Add("only size");
            }

            if (lines.Any(line => line.Property != MfdProperty.Rotation && line.NeedsSharedOverride))
            {
                parts.Add("shared");
            }

            return parts.Count == 0 ? string.Empty : " (" + string.Join(", ", parts) + ")";
        }

        private void ScreenSize(out double width, out double height)
        {
            var screen = _mfd.GetWidget(MfdRestart.ScreenWidgetName);
            width = screen?.Size.x ?? 0;
            height = screen?.Size.y ?? 0;
        }

        private void Select(Row row)
        {
            if (_selected?.Button != null)
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
                Icon("Ui/Sprites/Design/IconSpinnerArrowLeft", "Rotation down", () => row.Widget.LocalRotation -= _step * RotationStepFactor),
                Icon("Ui/Sprites/Design/IconSpinnerArrowRight", "Rotation up", () => row.Widget.LocalRotation += _step * RotationStepFactor)));

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
                this.AddWrappedTo(group, _message);
            }

            var changes = this.PendingChanges();
            if (changes.Count == 0)
            {
                if (_message == null)
                {
                    group.Add(new LabelModel("Nothing changed yet.", ElementAlignment.Left));
                }

                group.Add(new TextButtonModel("Restart MFD", _ => this.AskToRestart()));
                return group;
            }

            this.ScreenSize(out var width, out var height);
            var lines = MfdFormulaWriter.Apply(new XElement(_data.FlightProgramXml), changes, width, height, _allowShared);
            foreach (var line in lines)
            {
                this.AddWrappedTo(group, (line.Applied ? string.Empty : "Won't save: ") + line);
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
            group.Add(new TextButtonModel("Restart MFD", _ => this.AskToRestart()));
            return group;
        }

        private void AddWrappedTo(GroupModel group, string text)
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
                this.ClassifyAll();
                this.RefreshList();
                this.ReplaceChangesGroup();
            }
            catch (Exception e)
            {
                EditorLog.Error("Couldn't save the MFD changes: " + e);
                _message = "Saving failed: see Player.log.";
                this.ReplaceChangesGroup();
            }
        }

        /// <summary>The panel stays until the answer, so the flight stays held. Then it closes: the program can't build anything while paused.</summary>
        private void AskToRestart()
        {
            var dialog = Game.Instance.UserInterface.CreateMessageDialog(ModApi.Ui.MessageDialogType.OkayCancel, null, true);
            dialog.UseDangerButtonStyle = true;
            dialog.MessageText = MfdRestart.Question(_data) + " Changes that aren't saved are lost.";
            dialog.OkayClicked += _ =>
            {
                dialog.Close();
                try
                {
                    this.Close();
                    if (!MfdRestart.Run(_data))
                    {
                        Game.Instance.FlightScene.FlightSceneUI.ShowMessage("The MFD program didn't restart: see Player.log.");
                    }
                }
                catch (Exception e)
                {
                    EditorLog.Error("Couldn't restart the MFD program: " + e);
                }
            };
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

            /// <summary>Its row in the list, or null when it isn't listed.</summary>
            public TextButtonModel Button { get; set; }

            public bool CanArrange { get; set; }

            public string Label { get; set; }

            public string Tooltip { get; set; }

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
