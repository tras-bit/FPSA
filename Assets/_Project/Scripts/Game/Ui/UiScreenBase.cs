using Kordon.Game.Match;
using UnityEngine;

namespace Kordon.Game.Ui
{
    /// <summary>
    /// Shared plumbing for every full screen: its own canvas, built once on Init
    /// (never in Awake, because screens need the GameFlow that created them), and
    /// hidden until the director shows it.
    /// </summary>
    public abstract class UiScreenBase : MonoBehaviour, IUiScreen
    {
        public abstract string Name { get; }

        protected virtual int SortOrder => 20;

        protected GameFlow Flow { get; private set; }
        protected bool Built { get; private set; }

        private Canvas _canvas;

        public RectTransform Root => (RectTransform)_canvas.transform;

        protected virtual void Awake()
        {
            _canvas = UiBuilder.CreateCanvas("Screen_" + Name, SortOrder);
            _canvas.gameObject.SetActive(false);
        }

        public void Init(GameFlow flow)
        {
            Flow = flow;
            if (Built)
            {
                return;
            }

            Build();
            Built = true;
        }

        protected abstract void Build();

        public virtual void OnShow()
        {
        }

        public virtual void OnHide()
        {
        }

        protected void Back() => Flow.Back();

        protected static void Place(RectTransform rect, Vector2 anchor, Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
