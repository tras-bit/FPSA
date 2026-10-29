using Kordon.Core.Ui;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The layout arithmetic behind the code built screens. A stack that silently
    /// overflows draws one element over another - that is how the settings screen
    /// shipped with slider labels underneath the slider tracks.
    /// </summary>
    public sealed class UiLayoutTests
    {
        [Fact]
        public void RowsStackTopDownWithSpacingBetweenThem()
        {
            VerticalLayout layout = new VerticalLayout(300f, paddingTop: 20f, spacing: 10f);

            Assert.Equal(20f + 25f, layout.Allocate(50f));
            Assert.Equal(20f + 50f + 10f + 25f, layout.Allocate(50f));
            Assert.Equal(80f + 50f + 10f + 25f, layout.Allocate(50f));
            Assert.Equal(190f, layout.Cursor);
        }

        [Fact]
        public void RowsNeverOverlap()
        {
            VerticalLayout layout = new VerticalLayout(400f, 10f, 10f, 8f);
            float previousBottom = 0f;

            for (int i = 0; i < 5; i++)
            {
                float center = layout.Allocate(60f);
                float top = center - 30f;
                float bottom = center + 30f;

                Assert.True(top >= previousBottom, "row " + i + " overlaps the one above it");
                previousBottom = bottom;
            }
        }

        [Fact]
        public void OverflowIsReportedInsteadOfDrawn()
        {
            VerticalLayout layout = new VerticalLayout(120f, spacing: 10f);

            Assert.True(layout.TryAllocate(50f, out _));
            Assert.True(layout.TryAllocate(50f, out _));
            Assert.False(layout.TryAllocate(50f, out float center));
            Assert.Equal(0f, center);
            Assert.Equal(110f, layout.Cursor);
        }

        [Fact]
        public void AllocateThrowsOnOverflow()
        {
            VerticalLayout layout = new VerticalLayout(40f);

            layout.Allocate(40f);
            Assert.Throws<System.InvalidOperationException>(() => layout.Allocate(10f));
        }

        [Fact]
        public void BottomPaddingIsNeverUsed()
        {
            VerticalLayout layout = new VerticalLayout(100f, 0f, 20f);

            Assert.True(layout.TryAllocate(80f, out _));
            Assert.False(layout.TryAllocate(1f, out _));
            // Remaining is measured above the bottom padding, so it is 0, not 20.
            Assert.Equal(0f, layout.Remaining);
        }

        [Fact]
        public void SpacingIsNotAddedBeforeTheFirstRow()
        {
            VerticalLayout layout = new VerticalLayout(100f, 0f, 0f, 30f);

            Assert.Equal(10f, layout.Allocate(20f));
            Assert.Equal(60f, layout.Allocate(20f));
        }

        [Fact]
        public void NegativeHeightIsRejected()
        {
            VerticalLayout layout = new VerticalLayout(100f);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.Allocate(-1f));
        }

        /// <summary>
        /// The settings screen's real budget, mirroring the constants in
        /// SettingsScreen: two 940 px panels, the audio/video column and the 16 row
        /// controls list with its header, capture hint and footer.  If a future edit
        /// grows a row this fails instead of the screen overlapping.
        /// </summary>
        [Fact]
        public void SettingsScreenBudgetsFitTheirPanels()
        {
            // Left column: padding 24, spacing 8, 700 px panel.
            const float leftPanelHeight = 700f;
            const float rightPanelHeight = 820f;
            VerticalLayout left = new VerticalLayout(leftPanelHeight, 24f, 24f, 8f);
            left.Allocate(44f);   // title
            left.Allocate(20f);   // section
            left.Allocate(1f);    // rule
            left.Allocate(22f);   // slider caption + value
            left.Allocate(18f);   // track
            left.Allocate(20f);
            left.Allocate(1f);
            left.Allocate(22f);
            left.Allocate(18f);
            left.Allocate(22f);
            left.Allocate(18f);
            left.Allocate(44f);   // subtitles
            left.Allocate(20f);
            left.Allocate(1f);
            left.Allocate(44f);   // quality / fullscreen
            left.Allocate(44f);   // resolution
            left.Allocate(52f);   // apply
            left.Allocate(62f);   // note
            Assert.True(left.Spare >= 0f, "left column overflows by " + -left.Spare + " px");
            // A panel much taller than its content leaves a dead band at the bottom.
            Assert.True(left.Spare < 90f, "left panel has " + left.Spare + " px of empty space");

            // Right column: padding 24, spacing 5, 16 bindable rows of 33.
            VerticalLayout right = new VerticalLayout(rightPanelHeight, 24f, 24f, 5f);
            right.Allocate(44f);  // title
            right.Allocate(22f);  // hint
            for (int i = 0; i < 16; i++)
            {
                right.Allocate(33f);
            }

            right.Allocate(24f);  // capture hint
            right.Allocate(46f);  // reset / back
            Assert.True(right.Spare >= 0f, "controls column overflows by " + -right.Spare + " px");
            Assert.True(right.Spare < 90f, "controls panel has " + right.Spare + " px of empty space");
        }

        [Fact]
        public void OldSettingsLayoutWouldHaveOverflowed()
        {
            // The shipped 1.0.4 layout spaced 16 control rows 44 px apart inside an
            // 860 px panel and then put the footer at a fixed -330 offset, which is
            // why the last rows were hidden behind the buttons.
            VerticalLayout right = new VerticalLayout(860f, paddingTop: 120f, spacing: 10f);
            right.Allocate(22f);
            int placed = 0;
            for (int i = 0; i < 16; i++)
            {
                if (!right.TryAllocate(44f, out _))
                {
                    break;
                }

                placed++;
            }

            Assert.True(placed < 16, "the old row spacing should not fit 16 rows");
        }
    }
}
