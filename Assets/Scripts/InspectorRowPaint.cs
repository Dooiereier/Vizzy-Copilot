namespace Assets.Scripts
{
    using System.Collections.Generic;
    using ModApi.Ui.Inspector;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Marks an inspector row as selected by coloring it directly: switching a row to the game's primary
    /// button style makes its text smaller.
    /// </summary>
    public sealed class InspectorRowPaint
    {
        // The game's Primary and PrimaryHover colors.
        private static readonly Color SelectedColor = new Color32(0x00, 0xB7, 0xED, 0xFF);
        private static readonly Color SelectedHoverColor = new Color32(0x00, 0xA1, 0xF2, 0xFF);

        private readonly Dictionary<TextButtonModel, (ColorBlock Colors, Color Text)> _originals = new Dictionary<TextButtonModel, (ColorBlock, Color)>();

        public void Clear()
        {
            _originals.Clear();
        }

        public void Paint(TextButtonModel model, bool selected)
        {
            var row = model.ItemElement?.GameObject;
            var button = row == null ? null : row.GetComponentInChildren<Selectable>(true);
            var text = row == null ? null : row.GetComponentInChildren<TMP_Text>(true);
            if (button == null || text == null)
            {
                return;
            }

            if (!_originals.ContainsKey(model))
            {
                _originals[model] = (button.colors, text.color);
            }

            var original = _originals[model];
            var colors = original.Colors;
            if (selected)
            {
                colors.normalColor = SelectedColor;
                colors.highlightedColor = SelectedHoverColor;
                colors.selectedColor = SelectedColor;
            }

            button.colors = colors;
            text.color = selected ? Color.white : original.Text;
        }
    }
}
