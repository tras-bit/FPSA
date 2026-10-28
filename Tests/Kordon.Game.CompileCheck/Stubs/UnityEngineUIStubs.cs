// UnityEngine.UI (uGUI) stub surface for the compile check. KORDON builds every
// menu and the HUD in code (no prefab YAML), so the UI layer touches exactly the
// members below; signatures mirror Unity 6000.0's bundled ugui 2.0.0.

using System;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);

    public class UnityEvent
    {
        public void AddListener(UnityAction call)
        {
        }

        public void RemoveAllListeners()
        {
        }

        public void Invoke()
        {
        }
    }

    public class UnityEvent<T0>
    {
        public void AddListener(UnityAction<T0> call)
        {
        }

        public void RemoveAllListeners()
        {
        }

        public void Invoke(T0 arg0)
        {
        }
    }
}

namespace UnityEngine
{
    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; } = new Vector2(0.5f, 0.5f);
        public Vector2 anchorMax { get; set; } = new Vector2(0.5f, 0.5f);
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 pivot { get; set; } = new Vector2(0.5f, 0.5f);
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Rect rect => new Rect(0f, 0f, sizeDelta.x, sizeDelta.y);

        public void SetInsetAndSizeFromParentEdge(Edge edge, float inset, float size)
        {
        }

        public enum Edge
        {
            Left = 0,
            Right = 1,
            Top = 2,
            Bottom = 3,
        }
    }

    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }
    }

    public class Sprite : Object
    {
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) => new Sprite();
    }

    public class Font : Object
    {
        public int fontSize { get; set; } = 14;
    }

    public class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; } = RenderMode.ScreenSpaceOverlay;
        public int sortingOrder { get; set; }
        public float scaleFactor { get; set; } = 1f;
        public Camera worldCamera { get; set; }
    }

    public enum RenderMode
    {
        ScreenSpaceOverlay = 0,
        ScreenSpaceCamera = 1,
        WorldSpace = 2,
    }

    public class CanvasScaler : Behaviour
    {
        public enum ScaleMode
        {
            ConstantPixelSize = 0,
            ScaleWithScreenSize = 1,
            ConstantPhysicalSize = 2,
        }

        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; } = new Vector2(1920f, 1080f);
        public float matchWidthOrHeight { get; set; }
        public float referencePixelsPerUnit { get; set; } = 100f;
    }

    public class GraphicRaycaster : Behaviour
    {
    }
}

namespace UnityEngine.UI
{
    public abstract class Graphic : Behaviour
    {
        public RectTransform rectTransform =>
            gameObject == null ? null : gameObject.GetComponent<RectTransform>();

        public Color color { get; set; } = Color.white;
        public bool raycastTarget { get; set; } = true;
        public Material material { get; set; }

        public void SetAllDirty()
        {
        }

        public void CrossFadeColor(Color color, float duration, bool ignoreTimeScale,
            bool useAlpha)
        {
        }
    }

    public abstract class MaskableGraphic : Graphic
    {
    }

    public class Image : MaskableGraphic
    {
        public enum FillMethod
        {
            Horizontal = 1,
            Vertical = 2,
            Radial360 = 4,
        }

        public Sprite sprite { get; set; }
        public FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; } = 1f;
        public bool fillCenter { get; set; } = true;
        public float pixelsPerUnitMultiplier { get; set; } = 1f;
        public Image.Type type { get; set; }

        public enum Type
        {
            Simple = 0,
            Sliced = 1,
            Tiled = 2,
            Filled = 3,
        }
    }

    public class RawImage : MaskableGraphic
    {
        public Texture texture { get; set; }
    }

    public enum TextAnchor
    {
        UpperLeft = 0,
        UpperCenter = 1,
        UpperRight = 2,
        MiddleLeft = 3,
        MiddleCenter = 4,
        MiddleRight = 5,
        LowerLeft = 6,
        LowerCenter = 7,
        LowerRight = 8,
    }

    public enum FontStyle
    {
        Normal = 0,
        Bold = 1,
        Italic = 2,
        BoldAndItalic = 3,
    }

    public enum HorizontalWrapMode
    {
        Wrap = 0,
        Overflow = 1,
    }

    public enum VerticalWrapMode
    {
        Truncate = 0,
        Overflow = 1,
    }

    public class Text : MaskableGraphic
    {
        public string text { get; set; } = "";
        public Font font { get; set; }
        public int fontSize { get; set; } = 14;
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; } = TextAnchor.UpperLeft;
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public float lineSpacing { get; set; } = 1f;
        public bool supportRichText { get; set; } = true;
    }

    public class Button : Behaviour
    {
        public UnityEvent onClick { get; } = new UnityEvent();
        public bool interactable { get; set; } = true;
    }

    public class Toggle : Behaviour
    {
        public bool isOn { get; set; }
        public UnityEvent<bool> onValueChanged { get; } = new UnityEvent<bool>();
    }

    public class Slider : Behaviour
    {
        public float value { get; set; }
        public float minValue { get; set; }
        public float maxValue { get; set; } = 1f;
        public UnityEvent<float> onValueChanged { get; } = new UnityEvent<float>();
    }

    public class InputField : Behaviour
    {
        public string text { get; set; } = "";
        public Text placeholder { get; set; }
        public Text textComponent { get; set; }
        public int characterLimit { get; set; }
        public UnityEvent<string> onEndEdit { get; } = new UnityEvent<string>();
        public UnityEvent<string> onValueChanged { get; } = new UnityEvent<string>();
    }

    public class ScrollRect : Behaviour
    {
        public Transform content { get; set; }
        public bool vertical { get; set; } = true;
        public bool horizontal { get; set; }
    }

    public class LayoutGroup : Behaviour
    {
        public RectOffset padding { get; set; } = new RectOffset();
        public float spacing { get; set; }
        public TextAnchor childAlignment { get; set; }
    }

    public class HorizontalLayoutGroup : LayoutGroup
    {
        public bool childControlWidth { get; set; } = true;
        public bool childControlHeight { get; set; } = true;
        public bool childForceExpandWidth { get; set; } = true;
        public bool childForceExpandHeight { get; set; } = true;
    }

    public class VerticalLayoutGroup : LayoutGroup
    {
        public bool childControlWidth { get; set; } = true;
        public bool childControlHeight { get; set; } = true;
        public bool childForceExpandWidth { get; set; } = true;
        public bool childForceExpandHeight { get; set; } = true;
    }

    public class LayoutElement : Behaviour
    {
        public float preferredWidth { get; set; } = -1f;
        public float preferredHeight { get; set; } = -1f;
        public float minWidth { get; set; } = -1f;
        public float minHeight { get; set; } = -1f;
        public float flexibleWidth { get; set; } = -1f;
        public float flexibleHeight { get; set; } = -1f;
    }

    public class ContentSizeFitter : Behaviour
    {
        public enum FitMode
        {
            Unconstrained = 0,
            MinSize = 1,
            PreferredSize = 2,
        }

        public FitMode horizontalFit { get; set; }
        public FitMode verticalFit { get; set; }
    }

    public class Outline : Behaviour
    {
        public Color effectColor { get; set; }
        public Vector2 effectDistance { get; set; }
    }

    public class Mask : Behaviour
    {
        public bool showMaskGraphic { get; set; } = true;
    }
}

namespace UnityEngine.EventSystems
{
    public class EventSystem : Behaviour
    {
        public static EventSystem current { get; set; }
    }

    public class StandaloneInputModule : Behaviour
    {
    }
}
