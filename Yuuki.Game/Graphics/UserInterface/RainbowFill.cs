// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Utilities;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Fills its box with the Hero Club rainbow and moving, use in BLOOM's chip and bar, the Full Bloom medal and badge. One run of
/// the colors is <see cref="RainbowColors.PATTERN_WIDTH"/> times as wide as the box, so a few heroes show at a time, and
/// the rest slide in.
/// </summary>
/// <remarks>
/// It draws only inside its box, so a masking parent can round or shear it (a chip's corners, a badge's slant).
/// </remarks>
public partial class RainbowFill : Container
{
    /// <summary>
    /// At most two whole stops and the pieces either side fit in the box at the pattern's width.
    /// </summary>
    private const int max_segments = 8;

    private readonly Box[] segments = new Box[max_segments];

    /// <summary>
    /// The colors it runs through, ending where they start: <see cref="RainbowColors.PASTEL"/> by default.
    /// </summary>
    public Color[] Stops { get; set; } = RainbowColors.PASTEL;

    /// <summary>
    /// How fast it moves, as a multiple of one run per <see cref="RainbowColors.PERIOD"/>; 0 holds it still.
    /// </summary>
    public double Speed { get; set; } = 1;

    /// <summary>
    /// When it started moving, on its own clock. It starts when it's first shown unless set; things that should move
    /// together share one.
    /// </summary>
    public double StartTime { get; set; } = double.NaN;

    public RainbowFill()
    {
        for (int i = 0; i < max_segments; i++)
            Add(segments[i] = new Box { RelativeSizeAxes = Axes.Y, Alpha = 0 });
    }

    public override bool HandlePositionalInput => false;

    public override void Update()
    {
        base.Update();

        if (double.IsNaN(StartTime))
            StartTime = Clock.CurrentTime;

        float width = DrawWidth;
        int used = 0;

        if (width > 0 && Stops.Length >= 2)
        {
            double pattern = width * RainbowColors.PATTERN_WIDTH;
            int stopCount = Stops.Length - 1;
            double stopWidth = pattern / stopCount;

            // How far the colors have slid left, in px: the color at x is the pattern's at x + offset.
            double offset = RainbowColors.Phase(Clock.CurrentTime - StartTime, Speed) * pattern;

            double from = offset / stopWidth;
            double end = (width + offset) / stopWidth;

            while (from < end && used < max_segments)
            {
                double to = Math.Min(Math.Floor(from) + 1, end);

                Box segment = segments[used++];
                segment.X = (float)(from * stopWidth - offset);
                segment.Width = (float)((to - from) * stopWidth);
                // A piece ending on a stop ends on that stop's color, the run's last at its end rather than its first again.
                Color right = Precision.AlmostEquals(to, Math.Floor(to)) ? Stops[((int)to - 1) % stopCount + 1] : RainbowColors.Sample(Stops, to / stopCount);
                segment.ColorInfo = ColorInfo.GradientHorizontal(RainbowColors.Sample(Stops, from / stopCount), right);
                segment.Alpha = 1;

                from = to;
            }
        }

        for (int i = used; i < max_segments; i++)
            segments[i].Alpha = 0;
    }
}
