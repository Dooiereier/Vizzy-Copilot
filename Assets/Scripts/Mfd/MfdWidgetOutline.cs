namespace Assets.Scripts.Mfd
{
    using ModApi.Craft.Program.Craft;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// An outline on the selected widget, as a child of it, so its place is always right. Only draws:
    /// no raycasts, so the MFD's own buttons keep working.
    /// </summary>
    public sealed class MfdWidgetOutline
    {
        private const float Thickness = 2f;
        private const string ShaderName = "Assets/Resources/Shaders/VizzyStudioOverlay.shader";
        private static readonly Color OutlineColor = new Color(1f, 0.25f, 0.25f, 0.9f);

        private static Material _material;
        private static bool _materialTried;
        private GameObject _root;

        public void Show(IMfdWidget widget)
        {
            if (_root == null)
            {
                this.Build();
            }

            var parent = widget.Transform;
            _root.transform.SetParent(parent, false);
            _root.transform.SetAsLastSibling();
            foreach (var child in _root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = parent.gameObject.layer;
            }

            var rect = (RectTransform)_root.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        private void Build()
        {
            _root = new GameObject("VizzyStudio.MfdOutline", typeof(RectTransform));
            this.AddBar(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, Thickness));
            this.AddBar(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -Thickness), new Vector2(0f, 0f));
            this.AddBar(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(Thickness, 0f));
            this.AddBar(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-Thickness, 0f), new Vector2(0f, 0f));
        }

        private void AddBar(Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)bar.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = bar.GetComponent<Image>();
            image.color = OutlineColor;
            image.raycastTarget = false;
            var material = Material();
            if (material != null)
            {
                image.material = material;
            }
        }

        private static Material Material()
        {
            if (!_materialTried)
            {
                _materialTried = true;
                var shader = Mod.Instance.ResourceLoader.LoadAsset<Shader>(ShaderName);
                if (shader != null)
                {
                    _material = new Material(shader);
                }
                else
                {
                    EditorLog.Warning("The MFD outline's shader wasn't found: the outline may flicker against a widget's background.");
                }
            }

            return _material;
        }
    }
}
