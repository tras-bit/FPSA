using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Every screen in KORDON is built in code: no canvas prefabs, no scene UI,
    /// nothing to drift from the rules.  These primitives are the whole toolkit -
    /// panels, labels, buttons, rows - styled once, in one place, so the boot menu
    /// and the scoreboard look like the same product.
    /// </summary>
    public static class UiBuilder
    {
        public static readonly Color Ink = new Color(0.05f, 0.06f, 0.07f, 0.82f);
        public static readonly Color InkSolid = new Color(0.06f, 0.07f, 0.08f, 1f);
        public static readonly Color Paper = new Color(0.86f, 0.88f, 0.86f, 1f);
        public static readonly Color Accent = new Color(0.85f, 0.45f, 0.12f, 1f);
        public static readonly Color Danger = new Color(0.78f, 0.16f, 0.14f, 1f);
        public static readonly Color MagazineFull = new Color(0.92f, 0.92f, 0.90f, 1f);
        public static readonly Color MagazinePartial = new Color(0.93f, 0.76f, 0.12f, 1f);
        public static readonly Color MagazineEmpty = new Color(0.72f, 0.13f, 0.11f, 1f);

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                return _font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform Panel(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return rect;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Text Label(Transform parent, string name, string content, int size,
            TextAnchor anchor, Color color, FontStyle style = FontStyle.Normal)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text text = go.AddComponent<Text>();
            text.text = content;
            text.font = Font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string name, string caption,
            UnityAction onClick, int fontSize = 22)
        {
            RectTransform rect = Panel(parent, "Btn_" + name, Ink, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 64f));
            Text label = Label(rect, "Label", caption, fontSize, TextAnchor.MiddleCenter, Paper);
            StretchLabel(label);
            Button button = rect.gameObject.AddComponent<Button>();
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        public static void StretchLabel(Text text)
        {
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static RectTransform Row(Transform parent, string name, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.preferredHeight = size.y;
            return rect;
        }

        public static VerticalLayoutGroup Column(Transform parent, string name, float spacing,
            TextAnchor alignment = TextAnchor.UpperCenter)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            VerticalLayoutGroup group = go.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = alignment;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup Line(Transform parent, string name, float spacing)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            HorizontalLayoutGroup group = go.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleCenter;
            return group;
        }

        public static Color MagazineColor(string colorKey)
        {
            switch (colorKey)
            {
                case "full": return MagazineFull;
                case "partial": return MagazinePartial;
                default: return MagazineEmpty;
            }
        }
    }
}
