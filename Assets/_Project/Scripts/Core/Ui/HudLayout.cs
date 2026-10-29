using System.Collections.Generic;

namespace Kordon.Core.Ui
{
    /// <summary>A rectangle measured in pixels from the bottom right corner of the screen.</summary>
    public struct HudRect
    {
        public HudRect(float right, float bottom, float width, float height)
        {
            Right = right;
            Bottom = bottom;
            Width = width;
            Height = height;
        }

        /// <summary>Inset of the right edge from the screen's right edge.</summary>
        public float Right { get; }

        /// <summary>Distance of the bottom edge above the screen's bottom edge.</summary>
        public float Bottom { get; }

        public float Width { get; }

        public float Height { get; }

        public float Left(float screenWidth) => screenWidth - Right - Width;

        public float Top => Bottom + Height;

        /// <summary>Vertical band shared by both rectangles, in bottom-up pixels.</summary>
        public bool SharesBand(HudRect other) => Bottom < other.Top && other.Bottom < Top;

        public bool Overlaps(HudRect other, float screenWidth)
        {
            bool apartHorizontally = Left(screenWidth) >= other.Left(screenWidth) + other.Width ||
                                     other.Left(screenWidth) >= Left(screenWidth) + Width;
            bool apartVertically = Bottom >= other.Top || other.Bottom >= Top;
            return !apartHorizontally && !apartVertically;
        }
    }

    /// <summary>
    /// Geometry of the HUD's bottom right block, in one place:
    ///
    ///     inspect readout      (only while R is held)
    ///     sidearm magazines
    ///     primary magazines
    ///     loadout card         the three slots, the active one lit
    ///
    /// This replaces hand written offsets in <c>HudScreen</c> - the first version put
    /// the magazine strip in the bottom centre where it fought with the interaction
    /// hint, and every row is a number somebody has to keep true.  Here it is unit
    /// tested against real screen sizes instead.
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Inset of the whole block from the screen edges.</summary>
        public const float EdgeInset = 26f;

        /// <summary>Width of the card and of the rows above it.</summary>
        public const float ColumnWidth = 360f;

        public const float CardHeight = 116f;
        public const float MagazineRowHeight = 38f;
        public const float SidearmRowHeight = 28f;
        public const float InspectLineHeight = 28f;

        public const float CardToMagazinesGap = 8f;
        public const float MagazinesToSidearmGap = 6f;
        public const float SidearmToInspectGap = 8f;

        public const float MagazineIconWidth = 22f;
        public const float MagazineIconHeight = 36f;
        public const float MagazineIconGap = 6f;
        public const float SidearmIconWidth = 15f;
        public const float SidearmIconHeight = 24f;

        /// <summary>Half the width of the centred interaction hint at the bottom.</summary>
        public const float HintHalfWidth = 230f;

        public static HudRect LoadoutCard() =>
            new HudRect(EdgeInset, EdgeInset, ColumnWidth, CardHeight);

        public static HudRect MagazineRow() =>
            new HudRect(EdgeInset, LoadoutCard().Top + CardToMagazinesGap, ColumnWidth,
                MagazineRowHeight);

        public static HudRect SidearmRow() =>
            new HudRect(EdgeInset, MagazineRow().Top + MagazinesToSidearmGap, ColumnWidth,
                SidearmRowHeight);

        public static HudRect InspectLine() =>
            new HudRect(EdgeInset, SidearmRow().Top + SidearmToInspectGap, ColumnWidth,
                InspectLineHeight);

        /// <summary>Every rectangle in the block, bottom to top.</summary>
        public static IReadOnlyList<HudRect> Column() => new[]
        {
            LoadoutCard(), MagazineRow(), SidearmRow(), InspectLine()
        };

        /// <summary>Total height the block needs from the bottom edge of the screen.</summary>
        public static float ColumnHeight => InspectLine().Top;

        /// <summary>Lift of the subtitle line above the top of the block.</summary>
        public const float SubtitleLift = 16f;

        /// <summary>
        /// Bottom edge of the subtitle line.  Subtitles used to sit at 138 px from the
        /// bottom, which is inside the block's band on a 1280 wide screen - a long
        /// subtitle would have been drawn over the loadout card.
        /// </summary>
        public static float SubtitleBottom => ColumnHeight + SubtitleLift;

        /// <summary>How wide a row of n magazine icons is, including the gaps.</summary>
        public static float MagazineRowWidth(int count, float iconWidth, float gap) =>
            count <= 0 ? 0f : count * iconWidth + (count - 1) * gap;

        /// <summary>
        /// Problems with the block on a screen of this size: rows that overlap each other,
        /// a block wider than the screen, or a block that covers the centred hint.
        /// </summary>
        public static List<string> Validate(float screenWidth, float screenHeight)
        {
            List<string> problems = new List<string>();
            IReadOnlyList<HudRect> column = Column();

            for (int i = 0; i < column.Count; i++)
            {
                HudRect rect = column[i];
                if (rect.Left(screenWidth) < 0f)
                {
                    problems.Add("row " + i + " is wider than the screen");
                }

                if (rect.Top > screenHeight)
                {
                    problems.Add("row " + i + " is taller than the screen");
                }

                for (int j = i + 1; j < column.Count; j++)
                {
                    if (rect.Overlaps(column[j], screenWidth))
                    {
                        problems.Add("row " + i + " overlaps row " + j);
                    }
                }
            }

            if (screenWidth * 0.5f + HintHalfWidth > LoadoutCard().Left(screenWidth))
            {
                problems.Add("the bottom centre hint runs into the loadout card");
            }

            return problems;
        }
    }
}
