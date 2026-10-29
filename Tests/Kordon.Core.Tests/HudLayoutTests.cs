using System.Collections.Generic;
using Kordon.Core.Ui;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The bottom right HUD column: weapon panel, primary magazines, sidearm magazines,
    /// grenade count and the magazine inspection readout.
    ///
    /// The magazines living in the bottom centre is what made the 1.0.5 HUD read as
    /// broken - the strip collided with the interaction hint and moved whenever the
    /// row count changed.  These tests pin the geometry against real screen sizes.
    /// </summary>
    public sealed class HudLayoutTests
    {
        public static IEnumerable<object[]> Screens => new[]
        {
            new object[] { 1280f, 720f },
            new object[] { 1366f, 768f },
            new object[] { 1600f, 900f },
            new object[] { 1920f, 1080f },
            new object[] { 2560f, 1440f },
            new object[] { 3440f, 1440f },
        };

        [Theory]
        [MemberData(nameof(Screens))]
        public void ColumnHasNoOverlapsOnRealScreens(float width, float height)
        {
            Assert.Empty(HudLayout.Validate(width, height));
        }

        [Fact]
        public void RowsStackUpwardInReadingOrder()
        {
            IReadOnlyList<HudRect> column = HudLayout.Column();
            Assert.Equal(5, column.Count);

            for (int i = 1; i < column.Count; i++)
            {
                Assert.True(column[i].Bottom > column[i - 1].Top,
                    "row " + i + " does not sit above row " + (i - 1));
            }
        }

        [Fact]
        public void WeaponPanelSitsInTheCornerWithTheSharedInset()
        {
            HudRect panel = HudLayout.WeaponPanel();

            Assert.Equal(HudLayout.EdgeInset, panel.Bottom);
            Assert.Equal(HudLayout.EdgeInset, panel.Right);
            Assert.Equal(HudLayout.ColumnWidth, panel.Width);
            Assert.Equal(1920f - HudLayout.EdgeInset - HudLayout.ColumnWidth,
                panel.Left(1920f));
        }

        [Fact]
        public void PrimaryMagazinesSitDirectlyAboveTheWeaponPanel()
        {
            HudRect panel = HudLayout.WeaponPanel();
            HudRect magazines = HudLayout.MagazineRow();

            Assert.Equal(panel.Top + HudLayout.RowGap, magazines.Bottom);
            Assert.Equal(panel.Width, magazines.Width);
            Assert.Equal(panel.Right, magazines.Right);
        }

        [Fact]
        public void ColumnStaysOutOfTheBottomCentreHint()
        {
            // 1280 is the narrowest supported window: the hint is centred there and the
            // column must still start to the right of it.
            const float width = 1280f;
            Assert.True(HudLayout.WeaponPanel().Left(width) > width * 0.5f + 260f);
        }

        [Fact]
        public void ColumnIsShortEnoughToLeaveTheCentreOfTheScreenClear()
        {
            // Everything the column needs lives in the bottom 240 px; a 720 px window is
            // the smallest target and the aiming reticle area starts far above that.
            Assert.True(HudLayout.ColumnHeight < 240f);
            Assert.True(HudLayout.ColumnHeight < 720f * 0.4f);
        }

        [Fact]
        public void OverlapTestIsNotVacuous()
        {
            HudRect left = new HudRect(300f, 100f, 200f, 50f);
            HudRect right = new HudRect(28f, 100f, 100f, 50f);
            Assert.False(left.Overlaps(right, 1920f));

            HudRect onTop = new HudRect(28f, 120f, 100f, 50f);
            Assert.True(right.Overlaps(onTop, 1920f));
        }

        [Fact]
        public void OverlappingRowsAreReported()
        {
            // A 200 px wide screen cannot hold the column next to the hint.
            List<string> problems = HudLayout.Validate(200f, 300f);
            Assert.NotEmpty(problems);
        }
    }
}
