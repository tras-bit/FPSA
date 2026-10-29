using System;

namespace Kordon.Core.Ui
{
    /// <summary>
    /// Deterministic top-down stack for the code built screens.
    ///
    /// The screens used to place every element with a hand written offset from a
    /// mixture of top, centre and bottom anchors. That cannot be kept correct: a
    /// label on a top anchor and a slider on a centre anchor end up on top of each
    /// other the moment either is tuned, which is exactly what happened on the
    /// settings screen. A stack allocates vertical space in call order and reports
    /// overflow instead of silently overlapping.
    ///
    /// Engine free on purpose: the layout arithmetic is unit tested in
    /// <c>UiLayoutTests</c>, without a Unity editor.
    /// </summary>
    public sealed class VerticalLayout
    {
        private bool _hasItems;

        public VerticalLayout(float height, float paddingTop = 0f, float paddingBottom = 0f,
            float spacing = 0f)
        {
            Height = height;
            PaddingTop = paddingTop;
            PaddingBottom = paddingBottom;
            Spacing = spacing;
            Cursor = paddingTop;
        }

        /// <summary>Total height of the container this stack fills.</summary>
        public float Height { get; }

        public float PaddingTop { get; }

        public float PaddingBottom { get; }

        /// <summary>Gap inserted between two allocated items.</summary>
        public float Spacing { get; }

        /// <summary>Distance from the top edge consumed so far, spacing included.</summary>
        public float Cursor { get; private set; }

        /// <summary>Height still available above the bottom padding.</summary>
        public float Remaining => Height - PaddingBottom - Cursor;

        /// <summary>
        /// Reserves a row.  Returns false - and does not advance - when the row does
        /// not fit, so a screen can react (log, shrink, scroll) instead of drawing
        /// one element over another.
        /// </summary>
        public bool TryAllocate(float height, out float centerFromTop)
        {
            centerFromTop = 0f;
            if (height < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            float gap = _hasItems ? Spacing : 0f;
            float needed = gap + height;
            if (needed > Remaining + 0.001f)
            {
                return false;
            }

            Cursor += gap;
            centerFromTop = Cursor + height * 0.5f;
            Cursor += height;
            _hasItems = true;
            return true;
        }

        /// <summary>Reserves a row, throwing when it does not fit (layout bugs).</summary>
        public float Allocate(float height)
        {
            if (!TryAllocate(height, out float center))
            {
                throw new InvalidOperationException(
                    "layout overflow: a " + height + " px row does not fit, " + Remaining +
                    " px left");
            }

            return center;
        }

        /// <summary>Adds raw space between rows, if it fits.</summary>
        public bool TrySpace(float height)
        {
            if (height < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (height > Remaining + 0.001f)
            {
                return false;
            }

            Cursor += height;
            _hasItems = true;
            return true;
        }

        /// <summary>Space left over at the bottom, for a footer that hugs the edge.</summary>
        public float Spare => Remaining;
    }
}
