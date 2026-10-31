// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The run's timing in four figures: the mean (how early or late), the unstable rate, how many were off early and late,
/// and the longest run of Criticals. With what's offered for the offsets under them, it answers "should I change my
/// offset?".
/// </summary>
public partial class TimingFigures : FlowContainer
{
    private readonly YuukiColorPalette palette;
    private readonly SpriteText mean, unstableRate, off, streak;

    public TimingFigures(YuukiColorPalette palette)
    {
        this.palette = palette;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(0, 6);

        Children = new Drawable[]
        {
            figure("Mean", mean = value()),
            figure("Unstable rate", unstableRate = value()),
            figure("Off early / late", off = value()),
            figure("Critical streak", streak = value())
        };
    }

    public void SetRun(PlayAnalysis run)
    {
        mean.Text = run.Result.MeanError is double m ? $"{(m > 0 ? "+" : m < 0 ? "−" : "±")}{Math.Abs(m):0} ms {(m >= 0 ? "late" : "early")}" : "—";
        unstableRate.Text = run.Result.UnstableRate is double ur ? ur.ToString("0", CultureInfo.InvariantCulture) : "—";
        off.Text = $"{run.EarlyOff} / {run.LateOff}";
        streak.Text = run.LongestCriticalStreak.ToString("N0", CultureInfo.InvariantCulture);
    }

    // Two to a row.
    private Drawable figure(string title, SpriteText shown) => new FlowContainer
    {
        RelativeSizeAxes = Axes.X,
        Width = 0.5f,
        AutoSizeAxes = Axes.Y,
        Direction = FlowDirection.Vertical,
        Children = new Drawable[]
        {
            new SpriteText
            {
                Text = title.ToUpperInvariant(),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 12, FontWeight.Bold),
                Color = ResultsInk.Secondary(palette)
            },
            shown
        }
    };

    private SpriteText value() => new SpriteText
    {
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20, FontWeight.ExtraBold),
        Color = palette.Text
    };
}
