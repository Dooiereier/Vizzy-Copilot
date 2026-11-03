namespace Assets.Scripts
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Assets.Scripts.Craft.FlightData;
    using Assets.Scripts.Vizzy.UI;
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Craft;
    using ModApi.Craft.Program.Expressions;
    using ModApi.Expressions;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;

    /// <summary>
    /// Suggests Funk names under a Funk field in any Vizzy editor, in the designer or in flight. Offers
    /// only what the game's Vizzy Funk context can read. FunkInputPatch hands it the keys while the list
    /// is open. All it ever changes is the text the player chooses to insert.
    /// </summary>
    public class FunkAutoFill : MonoBehaviour
    {
        // Survive a quickload, so the names just used stay on top through the tuning loop.
        private static readonly List<string> Recent = new List<string>();

        private TMP_InputField _field;
        private TextInputElementScript _element;
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
            return FunkInputPatch.Installed && ModSettings.Instance.FunkAutoFill.Value;
        }

        private static FunkCatalog BuildCatalog(VizzyUIScript editor)
        {
            var catalog = new FunkCatalog();
            // Non-public in the game's Context, and reading them keeps this list matching the game's.
            var context = new Context(true);
            foreach (DictionaryEntry function in ContextMember(context, "Functions"))
            {
                var method = (MethodInfo)function.Value.GetType().GetField("Item1").GetValue(function.Value);
                var parameters = method.GetParameters().Select(parameter => parameter.Name);
                var name = (string)function.Key;
                catalog.TopLevel.Add(new FunkName(name, name + "(" + string.Join(", ", parameters) + ")"));
            }

            foreach (DictionaryEntry constant in ContextMember(context, "Constants"))
            {
                catalog.TopLevel.Add(new FunkName((string)constant.Key));
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
            foreach (var variable in editor.FlightProgram.GlobalVariables.Variables.Where(variable => !variable.IsList))
            {
                catalog.Variables.Add(new FunkName(variable.Name));
            }

            return catalog;
        }

        private static IDictionary ContextMember(Context context, string name)
        {
            return (IDictionary)typeof(Context)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(context);
        }

        private static IEnumerable<FunkName> ValueProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => IsValueType(property.PropertyType))
                .Select(property => new FunkName(property.Name));
        }

        private static bool IsValueType(Type type)
        {
            // Not Vector3: FD.AccelerationFrame compiled, then stopped the program when it ran.
            return type == typeof(float) || type == typeof(double) || type == typeof(bool) || type == typeof(int) || type == typeof(short)
                || type == typeof(Vector3d);
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
            var element = IsEnabled() ? SelectedFunkField() : null;
            var field = element == null ? null : element.GetComponent<TMP_InputField>();
            if (field != _field)
            {
                _field = field;
                _element = element;
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
                var editor = (VizzyUIScript)_element.VizzyUI;
                _catalog = BuildCatalog(editor);

                // What the program's fields hold now. Not serialized: that renumbers the live blocks.
                _program = string.Join("\n", editor.ProgramTransform.GetComponentsInChildren<TextInputElementScript>(true).Select(input => input.Text));
            }

            _completion = _dismissed ? null : FunkCompletion.Find(field.text, field.stringPosition, _catalog, Recent, _program);
            _selected = 0;
            ShowList();
        }

        private static TextInputElementScript SelectedFunkField()
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            var field = selected == null ? null : selected.GetComponent<TMP_InputField>();
            var element = selected == null ? null : selected.GetComponent<TextInputElementScript>();
            return field != null && element != null && field.isFocused && element.Parent?.Node is EvaluationExpression ? element : null;
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
