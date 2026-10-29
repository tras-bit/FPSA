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
            Assert.Equal(250f, layout.Cursor);
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
            Assert.Equal(20f, layout.Remaining);
        }

        [Fact]
        public void SpacingIsNotAddedBeforeTheFirstRow()
        {
            VerticalLayout layout = new VerticalLayout(100f, 0f, 0f, 30f);

            Assert.Equal(10f, layout.Allocate(20f));
            Assert.Equal(45f, layout.Allocate(20f));
        }

        [Fact]
        public void NegativeHeightIsRejected()
        {
            VerticalLayout layout = new VerticalLayout(100f);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.Allocate(-1f));
        }

        /// <summary>
        /// The settings screen's real budget: two 860 px panels, a 16 row controls
        /// list with its header and footer, and the audio/video column.  If a future
        /// edit grows a row, this test fails instead of the screen overlapping.
        /// </summary>
        [Fact]
        public void SettingsScreenBudgetsFitTheirPanels()
        {
            const float panelHeight = 860f;

            // Audio and video column.
            VerticalLayout left = new VerticalLayout(panelHeight, paddingTop: 96f,
                paddingBottom: 28f, spacing: 14f);
            left.Allocate(40f);    // title
            left.Allocate(18f);    // section: input
            left.Allocate(24f);    // look sensitivity label + value
            left.Allocate(16f);    // slider
            left.Allocate(18f);    // section: audio
            left.Allocate(24f);    // master volume label + value
            left.Allocate(16f);    // slider
            left.Allocate(24f);    // effects label + value
            left.Allocate(16f);    // slider
            left.Allocate(48f);    // subtitles
            left.Allocate(18f);    // section: video
            left.Allocate(46f);    // quality / fullscreen pair
            left.Allocate(46f);    // resolution
            left.Allocate(56f);    // apply and save
            left.Allocate(52f);    // note
            Assert.True(left.Spare >= 0f, "left column overflows by " + -left.Spare + " px");

            // Controls column: header, hint, 16 rebindable rows, capture hint, footer.
            VerticalLayout right = new VerticalLayout(panelHeight, paddingTop: 96f,
                paddingBottom: 28f, spacing: 6f);
            right.Allocate(40f);   // title
            right.Allocate(24f);   // hint
            right.Allocate(16f);   // rule
            for (int i = 0; i < 16; i++)
            {
                right.Allocate(34f);
            }

            right.Allocate(24f);   // capture hint
            right.Allocate(48f);   // reset / back
            Assert.True(right.Spare >= 0f, "controls column overflows by " + -right.Spare + " px");
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
