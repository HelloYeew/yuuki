// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;
using System.Linq;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The run lane by lane, a column each as the lanes stand: filled as high as its accuracy (from 80% up), with its
/// accuracy, how early or late it was pressed, and its misses. Pointing at one brings it out on the timing graph. It
/// answers "which finger is off?".
/// </summary>
public partial class LaneColumns : FlowContainer
{
    public const float HEIGHT = 118;

    /// <summary>
    /// How far a lane's mean has to be from the others' before it's said to be off, in ms.
    /// </summary>
    public const double OFF_BY = 4;

    private readonly YuukiColorPalette palette;

    // How high a lane's accuracy fills it.
    private static readonly Color lane_fill = ColorExtensions.FromHex("#C4ACCC");

    /// <summary>
    /// Called with the lane pointed at, or null as the pointer leaves them.
    /// </summary>
    public Action<int?>? LaneHovered { get; set; }

    public LaneColumns(YuukiColorPalette palette)
    {
        this.palette = palette;

        RelativeSizeAxes = Axes.X;
        Height = HEIGHT;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(12, 0);
    }

    public void SetRun(PlayAnalysis run)
    {
        Children = run.Lanes.Select(lane => (Drawable)new Column(lane, palette)
        {
            Hovered = hovered => LaneHovered?.Invoke(hovered ? lane.Lane : null)
        }).ToArray();
    }

    public override void Update()
    {
        // The row shared out between them, less the gaps.
        float width = Children.Count == 0 ? 0 : (DrawWidth - Spacing.X * (Children.Count - 1)) / Children.Count;

        foreach (Drawable column in Children)
            column.Width = Math.Max(0, width);

        base.Update();
    }

    /// <summary>
    /// What to ask, once the run is in: which lane runs off from the rest, if one does.
    /// </summary>
    public static string Verdict(PlayAnalysis run)
    {
        double[] means = run.Lanes.Where(l => l.MeanError != null).Select(l => l.MeanError!.Value).ToArray();

        if (means.Length < 2)
            return "Which finger is off? Your lanes are even.";

        double average = means.Average();
        LaneFigures worst = run.Lanes.Where(l => l.MeanError != null).MaxBy(l => Math.Abs(l.MeanError!.Value - average));
        double off = worst.MeanError!.Value - average;

        return Math.Abs(off) >= OFF_BY
            ? $"Lane {worst.Lane + 1} runs {Math.Abs(off):0} ms {(off > 0 ? "later" : "earlier")} than the rest."
            : "Which finger is off? Your lanes are even.";
    }

    private partial class Column : Container
    {
        private readonly Box background;
        private readonly YuukiColorPalette palette;

        public Action<bool>? Hovered { get; init; }

        public Column(LaneFigures lane, YuukiColorPalette palette)
        {
            this.palette = palette;

            RelativeSizeAxes = Axes.Y;
            Masking = true;
            CornerRadius = 10;

            float filled = Math.Clamp((float)((lane.Accuracy - 0.8) / 0.2), 0, 1);
            string timing = lane.MeanError is double mean ? $"{(mean > 0 ? "+" : mean < 0 ? "−" : "±")}{Math.Abs(mean):0} ms" : "—";
            string misses = lane.Misses == 0 ? "no miss" : lane.Misses == 1 ? "1 miss" : $"{lane.Misses} misses";

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Text.WithAlpha(0.07f)
                },
                new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.Both,
                    Height = filled,
                    Color = lane_fill.WithAlpha(0.75f)
                },
                new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 3,
                    Y = -20,
                    Color = palette.Text.WithAlpha(0.5f)
                },
                text($"LANE {lane.Lane + 1}", 8, 13, FontWeight.ExtraBold, ResultsInk.Secondary(palette)),
                text(lane.Count == 0 ? "—" : (lane.Accuracy * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%", 28, 19, FontWeight.ExtraBold, palette.Text),
                text(timing, 54, 13, FontWeight.Bold, palette.Text),
                text(misses, 71, 13, FontWeight.Bold, palette.Text)
            };
        }

        private static SpriteText text(string value, float y, int size, FontWeight weight, Color color) => new SpriteText
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Y = y,
            Text = value,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, size, weight),
            Color = color
        };

        public override bool OnHover(MouseEvent e)
        {
            background.Color = palette.Text.WithAlpha(0.15f);
            Hovered?.Invoke(true);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            background.Color = palette.Text.WithAlpha(0.07f);
            Hovered?.Invoke(false);
            return base.OnHoverLost(e);
        }
    }
}
