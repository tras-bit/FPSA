using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Every screen and the HUD are built from these primitives in code: no canvas
    /// prefabs, no scene UI to drift out of sync with the rules. One palette, one
    /// font, one set of helpers, so the boot menu and the scoreboard belong to the
    /// same product.
    /// </summary>
    public static class UiBuilder
    {
        public static readonly Color Ink = new Color(0.05f, 0.06f, 0.07f, 0.86f);
        public static readonly Color Paper = new Color(0.87f, 0.88f, 0.85f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.65f, 0.63f, 1f);
        public static readonly Color Accent = new Color(0.88f, 0.46f, 0.13f, 1f);
        public static readonly Color Warning = new Color(0.85f, 0.66f, 0.16f, 1f);
        public static readonly Color Danger = new Color(0.78f, 0.18f, 0.16f, 1f);

        public static Font Font
        {
            get
            {
                // Unity ships a default font; Arial.ttf is the documented builtin name.
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform Node(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Panel(Transform parent, string name, Color color, Vector2 anchor,
            Vector2 position, Vector2 size)
        {
            RectTransform rect = Node(parent, name);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static RectTransform FullRect(Transform parent, string name)
        {
            RectTransform rect = Node(parent, name);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Text Label(Transform parent, string name, string text, int size,
            Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            RectTransform rect = Node(parent, name);
            Text label = rect.gameObject.AddComponent<Text>();
            label.text = text;
            label.font = Font;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.raycastTarget = false;
            return label;
        }

        public static Text LabelAt(Transform parent, string name, string text, int size,
            Color color, Vector2 anchor, Vector2 position, Vector2 size2, TextAnchor align)
        {
            Text label = Label(parent, name, text, size, color, align);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size2;
            return label;
        }

        public static Button Button(Transform parent, string caption, UnityAction onClick,
            Vector2 position, Vector2 size, int fontSize = 22)
        {
            Image background = Panel(parent, "Btn_" + caption, Ink, new Vector2(0.5f, 0.5f),
                position, size);
            Text label = Label(background.transform, "Label", caption, fontSize, Paper,
                TextAnchor.MiddleCenter);
            FullRect(label.transform, "LabelRect");
            ((RectTransform)label.transform).offsetMin = Vector2.zero;
            ((RectTransform)label.transform).offsetMax = Vector2.zero;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            Button button = background.gameObject.AddComponent<Button>();
            button.onClick.AddListener(onClick);
            return button;
        }

        public static void SetButtonText(Button button, string caption)
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = caption;
            }
        }

        public static InputField Field(Transform parent, string placeholder, Vector2 position,
            Vector2 size, string initial = "")
        {
            Image background = Panel(parent, "Field", new Color(0.11f, 0.12f, 0.13f, 1f),
                new Vector2(0.5f, 0.5f), position, size);
            Text text = Label(background.transform, "Text", initial, 20, Paper,
                TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.offsetMin = new Vector2(12f, 0f);
            text.rectTransform.offsetMax = new Vector2(-12f, 0f);
            text.supportRichText = false;

            InputField field = background.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.text = initial;
            field.characterLimit = 48;
            return field;
        }

        public static Slider Slider(Transform parent, Vector2 position, Vector2 size,
            float min, float max, float value, UnityAction<float> onChanged)
        {
            Image background = Panel(parent, "Slider", new Color(0.18f, 0.19f, 0.2f, 1f),
                new Vector2(0.5f, 0.5f), position, size);
            Image fill = Panel(background.transform, "Fill", Accent, new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(size.x * Mathf.InverseLerp(min, max, value),
                    size.y));
            fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.sizeDelta = new Vector2(0f, 0f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            Slider slider = background.gameObject.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(onChanged);
            }

            return slider;
        }

        public static VerticalLayoutGroup Column(Transform parent, string name, float spacing,
            Vector2 anchor, Vector2 position, Vector2 size, TextAnchor align = TextAnchor.UpperCenter)
        {
            RectTransform rect = Node(parent, name);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            VerticalLayoutGroup group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = align;
            group.childControlWidth = false;
            group.childControlHeight = false;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup Row(Transform parent, string name, float spacing,
            Vector2 size, TextAnchor align = TextAnchor.MiddleLeft)
        {
            RectTransform rect = Node(parent, name);
            rect.sizeDelta = size;
            LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = size.y;
            HorizontalLayoutGroup group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = align;
            group.childControlWidth = false;
            group.childControlHeight = false;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        public static Text Cell(Transform row, string name, string text, float width, int size,
            Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Text cell = Label(row, name, text, size, color, align);
            LayoutElement element = cell.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = 26f;
            return cell;
        }

        public static Color MagazineColor(string colorKey)
        {
            switch (colorKey)
            {
                case "full": return new Color(0.94f, 0.94f, 0.92f, 1f);
                case "partial": return new Color(0.94f, 0.76f, 0.12f, 1f);
                default: return new Color(0.74f, 0.14f, 0.12f, 1f);
            }
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
