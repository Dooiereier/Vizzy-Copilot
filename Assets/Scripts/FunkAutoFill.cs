namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Assets.Scripts.Craft.FlightData;
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Craft;
    using ModApi.Craft.Program.Expressions;
    using ModApi.Expressions;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;

    /// <summary>
    /// Suggests Funk names under a Funk field in an editor this mod opened. Offers only what the game's
    /// Vizzy Funk context can read. FunkInputPatch hands it the keys while the list is open.
    /// </summary>
    public class FunkAutoFill : MonoBehaviour
    {
        // Survive a quickload, so the names just used stay on top through the tuning loop.
        private static readonly List<string> Recent = new List<string>();

        private TMP_InputField _field;
        private FunkCatalog _catalog;
        private string _program;
        private FunkCompletion _completion;
        private FunkList _list;
        private TMP_InputField _listField;
        private int _selected;
        private string _lastText;
        private int _lastCaret = -1;
        private bool _dismissed;
        private KeyCode _heldKey = KeyCode.None;

        /// <summary>Gets the auto-fill of the current flight scene.</summary>
        public static FunkAutoFill Current { get; private set; }

        /// <summary>Called by the patch, before the field reads its keys. True when this took them.</summary>
        public bool TakeKeys(TMP_InputField field)
        {
            if (_heldKey != KeyCode.None)
            {
                if (UnityEngine.Input.GetKey(_heldKey))
                {
                    return field == _field;
                }

                _heldKey = KeyCode.None;
            }

            if (field != _field || _completion == null)
            {
                return false;
            }

            foreach (var key in new[] { KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Escape })
            {
                if (UnityEngine.Input.GetKeyDown(key))
                {
                    _heldKey = key;
                    OnKey(key);
                    return true;
                }
            }

            return false;
        }

        private static bool IsEnabled()
        {
            return FunkInputPatch.Installed
                && ModSettings.Instance.FunkAutoFill.Value
                && FlightEditorController.Current?.Session?.IsOpen == true;
        }

        private static FunkCatalog BuildCatalog(EditorSession session)
        {
            var catalog = new FunkCatalog();
            var context = new Context(true);
            foreach (var function in context.Functions)
            {
                var parameters = function.Value.Item1.GetParameters().Select(parameter => parameter.Name);
                catalog.TopLevel.Add(new FunkName(function.Key, function.Key + "(" + string.Join(", ", parameters) + ")"));
            }

            foreach (var constant in context.Constants.Keys)
            {
                catalog.TopLevel.Add(new FunkName(constant));
            }

            // The stateful functions and Vizzy's list functions have no parameter names to read.
            foreach (var signature in new Dictionary<string, string>
            {
                { "PID", "PID(target, current, p, i, d)" },
                { "smooth", "smooth(value, maxRate)" },
                { "sum", "sum(value)" },
                { "rate", "rate(value)" },
                { "listLen", "listLen(name)" },
                { "listNum", "listNum(name, index)" },
                { "listBool", "listBool(name, index)" },
                { "listStr", "listStr(name, index)" },
                { "listVec", "listVec(name, index)" },
            })
            {
                catalog.TopLevel.RemoveAll(name => name.Name == signature.Key);
                catalog.TopLevel.Add(new FunkName(signature.Key, signature.Value));
            }

            catalog.TopLevel.AddRange(ValueProperties(typeof(CraftControls)));
            catalog.FlightData.AddRange(ValueProperties(typeof(CraftFlightData)));
            catalog.OrbitData.AddRange(ValueProperties(typeof(CraftOrbitData)));

            // As the editor has them now, including ones just created.
            foreach (var variable in session.Editor.FlightProgram.GlobalVariables.Variables.Where(variable => !variable.IsList))
            {
                catalog.Variables.Add(new FunkName(variable.Name));
            }

            return catalog;
        }

        private static IEnumerable<FunkName> ValueProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => IsValueType(property.PropertyType))
                .Select(property => new FunkName(property.Name));
        }

        private static bool IsValueType(Type type)
        {
            return type == typeof(float) || type == typeof(double) || type == typeof(bool) || type == typeof(int) || type == typeof(short)
                || type == typeof(Vector3d) || type == typeof(Vector3);
        }

        private void Awake()
        {
            Current = this;
        }

        private void OnDestroy()
        {
            _list?.Destroy();
            if (Current == this)
            {
                Current = null;
            }
        }

        private void LateUpdate()
        {
            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                EditorLog.Error("Funk auto-fill failed: " + e);
                Close();
            }
        }

        private void Refresh()
        {
            var field = IsEnabled() ? SelectedFunkField() : null;
            if (field != _field)
            {
                _field = field;
                _catalog = null;
                _dismissed = false;
                _lastText = null;
                _lastCaret = -1;
                Close();
            }

            if (field == null)
            {
                return;
            }

            if (field.text == _lastText && field.stringPosition == _lastCaret)
            {
                return;
            }

            if (field.text != _lastText)
            {
                _dismissed = false;
            }

            _lastText = field.text;
            _lastCaret = field.stringPosition;
            if (_catalog == null)
            {
                var session = FlightEditorController.Current.Session;
                _catalog = BuildCatalog(session);
                _program = session.Data.FlightProgramXml.ToString();
            }

            _completion = _dismissed ? null : FunkCompletion.Find(field.text, field.stringPosition, _catalog, Recent, _program);
            _selected = 0;
            ShowList();
        }

        private static TMP_InputField SelectedFunkField()
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            var field = selected == null ? null : selected.GetComponent<TMP_InputField>();
            var element = selected == null ? null : selected.GetComponent<TextInputElementScript>();
            return field != null && element != null && field.isFocused && element.Parent?.Node is EvaluationExpression ? field : null;
        }

        private void OnKey(KeyCode key)
        {
            var count = _completion.Suggestions.Count;
            if (key == KeyCode.UpArrow)
            {
                _selected = (_selected + count - 1) % count;
                ShowList();
            }
            else if (key == KeyCode.DownArrow)
            {
                _selected = (_selected + 1) % count;
                ShowList();
            }
            else if (key == KeyCode.Escape)
            {
                _dismissed = true;
                Close();
            }
            else
            {
                Accept();
            }
        }

        private void Accept()
        {
            var suggestion = _completion.Suggestions[_selected];
            var name = suggestion.Insert.TrimEnd('(');
            Recent.Remove(name);
            Recent.Insert(0, name);

            var text = _completion.Apply(_field.text, suggestion, out var caret);
            _field.text = text;
            _field.stringPosition = caret;
            _field.selectionStringAnchorPosition = caret;
            _field.selectionStringFocusPosition = caret;

            // Taken as written: the list comes back once more is typed.
            _lastText = text;
            _lastCaret = caret;
            Close();
        }

        private void ShowList()
        {
            if (_completion == null)
            {
                _list?.Hide();
                return;
            }

            if (_list == null || _listField != _field)
            {
                _list?.Destroy();
                _list = new FunkList(_field);
                _listField = _field;
            }

            _list.Show(_field, _completion.Suggestions, _selected);
        }

        private void Close()
        {
            _completion = null;
            _list?.Hide();
        }
    }
}
