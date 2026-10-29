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

        /// <summary>Raised surface used for controls: buttons must read as buttons.</summary>
        public static readonly Color Surface = new Color(0.13f, 0.145f, 0.16f, 1f);
        public static readonly Color SurfaceHover = new Color(0.2f, 0.225f, 0.245f, 1f);
        public static readonly Color SurfacePressed = new Color(0.09f, 0.1f, 0.11f, 1f);
        public static readonly Color Rule = new Color(0.25f, 0.27f, 0.29f, 1f);
        public static readonly Color Track = new Color(0.17f, 0.185f, 0.2f, 1f);

        private static Font _font;
        private static bool _fontResolved;

        /// <summary>
        /// The runtime's built in font.
        ///
        /// Unity 6000 renamed the builtin font: asking for "Arial.ttf" now throws
        /// ArgumentException ("Arial.ttf is no longer a valid built in font") and that
        /// exception propagated out of GameFlow.Awake, so the game booted into a dead
        /// screen.  LegacyRuntime.ttf is the 6000 name; the older name is only tried
        /// afterwards for an editor that still has it, and an OS font is the last
        /// resort so a font change can never take the whole UI down again.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_fontResolved)
                {
                    return _font;
                }

                _fontResolved = true;
                _font = LoadBuiltinFont("LegacyRuntime.ttf")
                        ?? LoadBuiltinFont("Arial.ttf")
                        ?? UnityEngine.Font.CreateDynamicFontFromOSFont(
                            new[] { "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans" }, 16);
                if (_font == null)
                {
                    Debug.LogError("No font available: UI text will not render.");
                }

                return _font;
            }
        }

        private static Font LoadBuiltinFont(string name)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(name);
            }
            catch (System.ArgumentException)
            {
                // The name does not exist in this Unity version; try the next one.
                return null;
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
            if (Object.FindFirstObjectByType<EventSystem>() != null)
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

        /// <summary>
        /// Button that looks like a button: raised surface, hover and pressed colours,
        /// centred label. The old version drew the label on the panel colour, so every
        /// control on a dark panel read as floating text.
        /// </summary>
        public static Button ButtonIn(Transform parent, string caption, UnityAction onClick,
            RectTransform rect, int fontSize = 20, Color? background = null)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = background ?? Surface;

            Text label = Label(rect, "Label", caption, fontSize, Paper, TextAnchor.MiddleCenter);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(10f, 0f);
            label.rectTransform.offsetMax = new Vector2(-10f, 0f);
            label.alignByGeometry = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.55f, 1.6f, 1.65f, 1f);
            colors.pressedColor = new Color(0.62f, 0.65f, 0.68f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.4f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        public static Button Button(Transform parent, string caption, UnityAction onClick,
            Vector2 position, Vector2 size, int fontSize = 22)
        {
            RectTransform rect = Node(parent, "Btn_" + caption);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return ButtonIn(parent, caption, onClick, rect, fontSize);
        }

        /// <summary>
        /// Rebindable controls row: action on the left, current key on the right, the
        /// whole row clickable.  Returns the two labels so the caller can refresh them.
        /// </summary>
        public static Button RowButtonIn(Transform parent, RectTransform rect,
            UnityAction onClick, out Text left, out Text right)
        {
            Button button = ButtonIn(parent, string.Empty, onClick, rect, 18, Surface);
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = string.Empty;
            }

            left = Label(rect, "Action", string.Empty, 18, Paper, TextAnchor.MiddleLeft);
            left.rectTransform.anchorMin = new Vector2(0f, 0f);
            left.rectTransform.anchorMax = new Vector2(0.7f, 1f);
            left.rectTransform.offsetMin = new Vector2(16f, 0f);
            left.rectTransform.offsetMax = new Vector2(0f, 0f);

            right = Label(rect, "Key", string.Empty, 18, Accent, TextAnchor.MiddleRight);
            right.rectTransform.anchorMin = new Vector2(0.7f, 0f);
            right.rectTransform.anchorMax = new Vector2(1f, 1f);
            right.rectTransform.offsetMin = new Vector2(0f, 0f);
            right.rectTransform.offsetMax = new Vector2(-16f, 0f);
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

        /// <summary>
        /// Horizontal slider: a track, a fill and a handle, all wired to the uGUI
        /// <see cref="Slider"/> so dragging works and the fill follows the value.
        /// A Slider with no fillRect/handleRect assigned looks like a dead grey bar,
        /// which is what the settings screen shipped with.
        /// </summary>
        public static Slider SliderIn(Transform parent, RectTransform rect, float min, float max,
            float value, UnityAction<float> onChanged)
        {
            Image track = rect.gameObject.AddComponent<Image>();
            track.color = Track;

            RectTransform fillRect = Node(rect, "Fill");
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(Mathf.InverseLerp(min, max, value), 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = Accent;
            fill.raycastTarget = false;

            RectTransform handleRect = Node(rect, "Handle");
            handleRect.anchorMin = new Vector2(Mathf.InverseLerp(min, max, value), 0.5f);
            handleRect.anchorMax = new Vector2(Mathf.InverseLerp(min, max, value), 0.5f);
            handleRect.sizeDelta = new Vector2(10f, 26f);
            Image handle = handleRect.gameObject.AddComponent<Image>();
            handle.color = Paper;
            handle.raycastTarget = false;

            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = track;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.value = value;
            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(onChanged);
            }

            return slider;
        }

        /// <summary>Slider placed by centre offset, for screens not using a stack yet.</summary>
        public static Slider Slider(Transform parent, Vector2 position, Vector2 size,
            float min, float max, float value, UnityAction<float> onChanged)
        {
            RectTransform rect = Node(parent, "Slider");
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return SliderIn(parent, rect, min, max, value, onChanged);
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
