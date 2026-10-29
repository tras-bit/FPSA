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

        public bool Overlaps(HudRect other, float screenWidth)
        {
            bool apartHorizontally = Left(screenWidth) >= other.Left(screenWidth) + other.Width ||
                                     other.Left(screenWidth) >= Left(screenWidth) + Width;
            bool apartVertically = Bottom >= other.Top || other.Bottom >= Top;
            return !apartHorizontally && !apartVertically;
        }
    }

    /// <summary>
    /// Geometry of the HUD's bottom right column: the weapon panel and the two
    /// magazine rows sit above it, the grenade line and the inspect line above those.
    ///
    /// These numbers used to live as hand written offsets inside <c>HudScreen</c>, and
    /// the strip that showed the magazines was anchored bottom centre where it fought
    /// with the interaction hint.  Keeping the arithmetic here means it is unit tested
    /// against real screen sizes instead of being eyeballed on a build nobody can open
    /// in this environment.
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Inset of the whole column from the screen edges.</summary>
        public const float EdgeInset = 28f;

        /// <summary>Width of the weapon panel and of the magazine rows above it.</summary>
        public const float ColumnWidth = 336f;

        public const float WeaponPanelHeight = 96f;
        public const float MagazineRowHeight = 16f;
        public const float SidearmRowHeight = 13f;
        public const float GrenadeLineHeight = 24f;
        public const float InspectLineHeight = 26f;

        /// <summary>Gap between stacked rows of the column.</summary>
        public const float RowGap = 8f;

        public static HudRect WeaponPanel() =>
            new HudRect(EdgeInset, EdgeInset, ColumnWidth, WeaponPanelHeight);

        /// <summary>Primary magazines, directly above the weapon panel.</summary>
        public static HudRect MagazineRow() =>
            new HudRect(EdgeInset, WeaponPanel().Top + RowGap, ColumnWidth, MagazineRowHeight);

        /// <summary>Sidearm magazines, above the primary row.</summary>
        public static HudRect SidearmRow() =>
            new HudRect(EdgeInset, MagazineRow().Top + RowGap, ColumnWidth, SidearmRowHeight);

        /// <summary>Grenade count, above the sidearm row.</summary>
        public static HudRect GrenadeLine() =>
            new HudRect(EdgeInset, SidearmRow().Top + RowGap, ColumnWidth, GrenadeLineHeight);

        /// <summary>Magazine inspection readout, above the grenade line.</summary>
        public static HudRect InspectLine() =>
            new HudRect(EdgeInset, GrenadeLine().Top + RowGap, ColumnWidth, InspectLineHeight);

        /// <summary>Every rectangle in the column, bottom to top.</summary>
        public static IReadOnlyList<HudRect> Column() => new[]
        {
            WeaponPanel(), MagazineRow(), SidearmRow(), GrenadeLine(), InspectLine()
        };

        /// <summary>Total height the column needs from the bottom edge of the screen.</summary>
        public static float ColumnHeight => InspectLine().Top;

        /// <summary>
        /// Problems with the column on a screen of this size: rows that overlap each
        /// other, a column wider than the screen, or a column that would cover the
        /// centre where the interaction hint and the subtitles live.
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

            // The hint is centred at the bottom of the screen, half of its width to each
            // side; it must stay clear of the weapon panel.
            // The hint is a 520 px wide, centred label at the bottom of the screen, so
            // it reaches 260 px to each side of the centre.
            const float hintHalfWidth = 260f;
            if (screenWidth * 0.5f + hintHalfWidth > WeaponPanel().Left(screenWidth))
            {
                problems.Add("the bottom centre hint runs into the weapon panel");
            }

            return problems;
        }
    }
}
