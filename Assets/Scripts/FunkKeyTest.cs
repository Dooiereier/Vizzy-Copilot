namespace Assets.Scripts
{
    using Assets.Scripts.Vizzy.UI.Elements;
    using ModApi.Craft.Program.Expressions;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;

    /// <summary>
    /// Temporary check for Funk auto-fill. The text field handles the keys before this runs, so this
    /// keeps a copy from the frame before: Up and Down put the cursor back, Enter inserts TEST and
    /// selects the field again.
    /// </summary>
    public class FunkKeyTest : MonoBehaviour
    {
        private TMP_InputField _field;
        private TextInputElementScript _element;
        private string _text;
        private int _caret;
        private int _anchor;
        private bool _reselect;
        private int _caretAfterReselect = -1;

        private void Update()
        {
            if (_field == null || !ModSettings.Instance.TestAutoFillKeys.Value)
            {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.UpArrow) || UnityEngine.Input.GetKeyDown(KeyCode.DownArrow))
            {
                EditorLog.Info("Key test: arrow. Cursor went " + _caret + " -> " + _field.stringPosition + ", put back. Focused: " + _field.isFocused);
                _field.stringPosition = _caret;
                _field.selectionStringAnchorPosition = _anchor;
                _field.selectionStringFocusPosition = _caret;
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                EditorLog.Info("Key test: Enter. Focused now: " + _field.isFocused + ", text: " + _text);
                _field.text = _text.Insert(_caret, "TEST");
                _caretAfterReselect = _caret + 4;
                _reselect = true;
            }
        }

        private void LateUpdate()
        {
            if (!ModSettings.Instance.TestAutoFillKeys.Value)
            {
                return;
            }

            if (_reselect && _field != null)
            {
                // Runs after the element's own Update, which turns editing off when an edit ends.
                _reselect = false;
                _element.EnableEditing = true;
                EventSystem.current.SetSelectedGameObject(_field.gameObject);
                _field.ActivateInputField();
                return;
            }

            if (_caretAfterReselect >= 0 && _field != null && _field.isFocused)
            {
                EditorLog.Info("Key test: field selected again, text: " + _field.text);
                _field.stringPosition = _caretAfterReselect;
                _field.selectionStringAnchorPosition = _caretAfterReselect;
                _field.selectionStringFocusPosition = _caretAfterReselect;
                _caretAfterReselect = -1;
            }

            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            var field = selected == null ? null : selected.GetComponent<TMP_InputField>();
            var element = selected == null ? null : selected.GetComponent<TextInputElementScript>();
            if (field == null || element == null || !field.isFocused || !(element.Parent?.Node is EvaluationExpression))
            {
                if (_caretAfterReselect < 0)
                {
                    _field = null;
                }

                return;
            }

            _field = field;
            _element = element;
            _text = field.text;
            _caret = field.stringPosition;
            _anchor = field.selectionStringAnchorPosition;
        }
    }
}
