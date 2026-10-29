using System.Collections.Generic;
using Kordon.Core.Ui;
using Xunit;

namespace Kordon.Core.Tests
{
    /// <summary>
    /// The bottom right HUD block: loadout card (which slot is in hand), primary
    /// magazines, sidearm magazines and the magazine inspection readout.
    ///
    /// The magazines living in the bottom centre is what made the 1.0.5 HUD read as
    /// broken - the strip collided with the interaction hint and moved whenever the row
    /// count changed.  These tests pin the geometry against real screen sizes.
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
            Assert.Equal(4, column.Count);

            for (int i = 1; i < column.Count; i++)
            {
                Assert.True(column[i].Bottom > column[i - 1].Top,
                    "row " + i + " does not sit above row " + (i - 1));
            }
        }

        [Fact]
        public void LoadoutCardSitsInTheCornerWithTheSharedInset()
        {
            HudRect card = HudLayout.LoadoutCard();

            Assert.Equal(HudLayout.EdgeInset, card.Bottom);
            Assert.Equal(HudLayout.EdgeInset, card.Right);
            Assert.Equal(HudLayout.ColumnWidth, card.Width);
            Assert.Equal(1920f - HudLayout.EdgeInset - HudLayout.ColumnWidth, card.Left(1920f));
        }

        [Fact]
        public void PrimaryMagazinesSitDirectlyAboveTheCard()
        {
            HudRect card = HudLayout.LoadoutCard();
            HudRect magazines = HudLayout.MagazineRow();

            Assert.Equal(card.Top + HudLayout.CardToMagazinesGap, magazines.Bottom);
            Assert.Equal(card.Width, magazines.Width);
            Assert.Equal(card.Right, magazines.Right);
        }

        [Fact]
        public void MagazineIconsAreTallerThanTheyAreWide()
        {
            // A horizontal bar reads as a progress bar, not as a magazine - the whole
            // reason the icons became silhouettes.
            Assert.True(HudLayout.MagazineIconHeight > HudLayout.MagazineIconWidth);
            Assert.True(HudLayout.SidearmIconHeight > HudLayout.SidearmIconWidth);
            Assert.True(HudLayout.SidearmIconWidth < HudLayout.MagazineIconWidth);
        }

        [Fact]
        public void SixPrimaryMagazinesStillFitTheRow()
        {
            float needed = HudLayout.MagazineRowWidth(6, HudLayout.MagazineIconWidth,
                HudLayout.MagazineIconGap);
            Assert.True(needed < HudLayout.ColumnWidth - 80f,
                "six magazines plus the row caption do not fit");
        }

        [Fact]
        public void ColumnStaysOutOfTheBottomCentreHint()
        {
            // 1280 is the narrowest supported window: the hint is centred there and the
            // block must still start to the right of it.
            const float width = 1280f;
            Assert.True(HudLayout.LoadoutCard().Left(width) > width * 0.5f + HudLayout.HintHalfWidth);
        }

        [Fact]
        public void ColumnIsShortEnoughToLeaveTheCentreOfTheScreenClear()
        {
            // Everything the block needs lives in the bottom 260 px; a 720 px window is
            // the smallest target and the aiming area starts far above that.
            Assert.True(HudLayout.ColumnHeight < 260f);
            Assert.True(HudLayout.ColumnHeight < 720f * 0.4f);
        }

        [Fact]
        public void SubtitlesClearTheBlock()
        {
            // Subtitles are centred and can be long; they must sit above every row of the
            // block instead of across the card.
            Assert.True(HudLayout.SubtitleBottom > HudLayout.ColumnHeight);
            Assert.True(HudLayout.SubtitleBottom < 720f * 0.5f);
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
            // A 1024 wide screen cannot hold the block next to the centred hint.
            List<string> problems = HudLayout.Validate(1024f, 600f);
            Assert.NotEmpty(problems);
        }
    }
}
