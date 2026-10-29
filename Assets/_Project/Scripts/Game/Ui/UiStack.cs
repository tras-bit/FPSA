using Kordon.Core.Ui;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// A vertical stack of rows inside one panel.
    ///
    /// Every screen element is allocated in order, so two elements can never land on
    /// the same pixels by accident - which is what happened when labels and sliders
    /// were positioned with independent hand written offsets. Heights come from the
    /// caller and the arithmetic is the unit tested
    /// <see cref="VerticalLayout"/>; overflow is reported instead of drawn.
    /// </summary>
    public sealed class UiStack
    {
        private readonly VerticalLayout _layout;
        private readonly RectTransform _container;
        private readonly float _width;
        private bool _warned;

        public UiStack(Transform panel, string name, float width, float height,
            float paddingTop, float paddingBottom, float spacing)
        {
            _container = UiBuilder.FullRect(panel, name);
            _width = width;
            _layout = new VerticalLayout(height, paddingTop, paddingBottom, spacing);
        }

        /// <summary>Root of the stack, for placing composite rows inside a returned row.</summary>
        public Transform Container => _container;

        /// <summary>
        /// Raw row for composite content the stack cannot know about (a field plus a
        /// stepper, a label plus two buttons).  Returns null on overflow.
        /// </summary>
        public RectTransform RawRow(float height) => Row(height);

        /// <summary>Space left at the bottom; negative means the content overflowed.</summary>
        public float Spare => _layout.Spare;

        /// <summary>Rows that did not fit. Should always be zero - it is logged when not.</summary>
        public int OverflowCount { get; private set; }

        private RectTransform Row(float height)
        {
            if (!_layout.TryAllocate(height, out float centerFromTop))
            {
                OverflowCount++;
                if (!_warned)
                {
                    _warned = true;
                    Debug.LogWarning("[kordon] ui stack '" + _container.name +
                                     "' overflowed: a " + height + " px row did not fit");
                }

                return null;
            }

            RectTransform rect = UiBuilder.Node(_container, "Row");
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_width, height);
            rect.anchoredPosition = new Vector2(0f, -centerFromTop);
            return rect;
        }

        /// <summary>Raw vertical space, used to push a footer to the bottom edge.</summary>
        public void Space(float height) => _layout.TrySpace(height);

        /// <summary>Left aligned label row.</summary>
        public Text Text(string text, int size, Color color, float height,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            RectTransform row = Row(height);
            if (row == null)
            {
                return null;
            }

            Text label = UiBuilder.Label(row, "Text", text, size, color, align);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(14f, 0f);
            label.rectTransform.offsetMax = new Vector2(-14f, 0f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        /// <summary>Section caption with a hairline under it.</summary>
        public void Section(string caption)
        {
            Text label = Text(caption.ToUpperInvariant(), 15, UiBuilder.Accent, 20f);
            if (label != null)
            {
                label.fontStyle = FontStyle.Bold;
            }

            Rule(1f);
        }

        /// <summary>Hairline divider.</summary>
        public void Rule(float height = 1f)
        {
            RectTransform row = Row(height);
            if (row == null)
            {
                return;
            }

            Image image = row.gameObject.AddComponent<Image>();
            image.color = UiBuilder.Rule;
            image.raycastTarget = false;
        }

        public Button Button(string caption, UnityAction onClick, float height, int fontSize = 20)
        {
            RectTransform row = Row(height);
            return row == null ? null : UiBuilder.ButtonIn(_container, caption, onClick, row, fontSize);
        }

        /// <summary>Two buttons side by side; both handles are returned for captions.</summary>
        public void Pair(string leftCaption, UnityAction leftAction, string rightCaption,
            UnityAction rightAction, float height, float gap = 12f, int fontSize = 17)
        {
            Pair(leftCaption, leftAction, rightCaption, rightAction, height, out _, out _,
                gap, fontSize);
        }

        public void Pair(string leftCaption, UnityAction leftAction, string rightCaption,
            UnityAction rightAction, float height, out Button leftButton, out Button rightButton,
            float gap = 12f, int fontSize = 17)
        {
            leftButton = null;
            rightButton = null;
            RectTransform row = Row(height);
            if (row == null)
            {
                return;
            }

            float each = (_width - gap) * 0.5f;
            RectTransform left = UiBuilder.Node(row, "Left");
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 0.5f);
            left.offsetMin = new Vector2(0f, 0f);
            left.offsetMax = new Vector2(0f, 0f);
            left.sizeDelta = new Vector2(each, 0f);

            RectTransform right = UiBuilder.Node(row, "Right");
            right.anchorMin = new Vector2(1f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(1f, 0.5f);
            right.offsetMin = new Vector2(0f, 0f);
            right.offsetMax = new Vector2(0f, 0f);
            right.sizeDelta = new Vector2(each, 0f);

            leftButton = UiBuilder.ButtonIn(row, leftCaption, leftAction, left, fontSize);
            rightButton = UiBuilder.ButtonIn(row, rightCaption, rightAction, right, fontSize);
        }

        /// <summary>
        /// A row of equally sized buttons - kit steppers, presets, short actions.
        /// </summary>
        public void RawButtons(float height, (string caption, UnityAction action)[] buttons,
            int fontSize = 15, float gap = 10f)
        {
            RectTransform row = RawRow(height);
            if (row == null || buttons == null || buttons.Length == 0)
            {
                return;
            }

            float each = (_width - gap * (buttons.Length - 1)) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                RectTransform slot = UiBuilder.Node(row, "Btn" + i);
                slot.anchorMin = new Vector2(0f, 0f);
                slot.anchorMax = new Vector2(0f, 1f);
                slot.pivot = new Vector2(0f, 0.5f);
                slot.offsetMin = Vector2.zero;
                slot.offsetMax = Vector2.zero;
                slot.sizeDelta = new Vector2(each, 0f);
                slot.anchoredPosition = new Vector2(i * (each + gap), slot.anchoredPosition.y);
                UiBuilder.ButtonIn(row, buttons[i].caption, buttons[i].action, slot, fontSize);
            }
        }

        /// <summary>
        /// Label with its current value on the right, then the slider track under it.
        /// Returns the slider and the value label so the screen can update both.
        /// </summary>
        public Slider SliderRow(string caption, float min, float max, float value,
            UnityAction<float> onChanged, out Text valueLabel)
        {
            valueLabel = null;
            RectTransform labelRow = Row(22f);
            if (labelRow != null)
            {
                Text captionLabel = UiBuilder.Label(labelRow, "Caption", caption.ToUpperInvariant(),
                    15, UiBuilder.Dim, TextAnchor.MiddleLeft);
                captionLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
                captionLabel.rectTransform.anchorMax = new Vector2(0.7f, 1f);
                captionLabel.rectTransform.offsetMin = new Vector2(14f, 0f);
                captionLabel.rectTransform.offsetMax = Vector2.zero;

                valueLabel = UiBuilder.Label(labelRow, "Value", string.Empty, 16,
                    UiBuilder.Paper, TextAnchor.MiddleRight);
                valueLabel.rectTransform.anchorMin = new Vector2(0.7f, 0f);
                valueLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
                valueLabel.rectTransform.offsetMin = Vector2.zero;
                valueLabel.rectTransform.offsetMax = new Vector2(-14f, 0f);
            }

            RectTransform row = Row(18f);
            return row == null ? null : UiBuilder.SliderIn(_container, row, min, max, value, onChanged);
        }

        /// <summary>Captioned text field filling the stack width.</summary>
        public InputField Field(string caption, string initial, float height, out Text captionLabel)
        {
            captionLabel = Text(caption.ToUpperInvariant(), 15, UiBuilder.Dim, 20f);
            RectTransform row = Row(height);
            if (row == null)
            {
                return null;
            }

            GameObject go = row.gameObject;
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.1f, 0.11f, 0.12f, 1f);

            Text text = UiBuilder.Label(row, "Text", initial, 20, UiBuilder.Paper,
                TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(14f, 0f);
            text.rectTransform.offsetMax = new Vector2(-14f, 0f);
            text.supportRichText = false;

            InputField field = go.AddComponent<InputField>();
            field.textComponent = text;
            field.text = initial;
            field.characterLimit = 48;
            return field;
        }

        /// <summary>Rebindable controls row: action name left, key right.</summary>
        public Button RowButton(UnityAction onClick, float height, out Text action, out Text key)
        {
            RectTransform row = Row(height);
            if (row == null)
            {
                action = null;
                key = null;
                return null;
            }

            return UiBuilder.RowButtonIn(_container, row, onClick, out action, out key);
        }
    }
}
