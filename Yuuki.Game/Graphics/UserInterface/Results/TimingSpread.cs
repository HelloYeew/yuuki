// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;
using System.Linq;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// How the run's presses spread round on time, early to the left and late to the right: a bar for every few ms, in the
/// color of the judgement it gets, the mean marked, and under it the mean, the spread and how many were early or late. It
/// answers "early or late overall?".
/// </summary>
public partial class TimingSpread : FlowContainer
{
    private const float graph_height = 84;
    private const float label_height = 20;
    private const double bin_width = 6;

    private readonly YuukiColorPalette palette;
    private readonly PlotCanvas bars;
    private readonly Container labels;
    private readonly ScrollingText line;

    private PlayAnalysis? run;
    private float progress = 1;
    private float plottedProgress = -1;
    private float plottedWidth = -1;

    public TimingSpread(YuukiColorPalette palette)
    {
        this.palette = palette;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 6);

        Children = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = graph_height + label_height,
                Children = new Drawable[]
                {
                    bars = new PlotCanvas(),
                    labels = new Container { RelativeSizeAxes = Axes.Both }
                }
            },
            // Scrolling where a narrow card can't fit it.
            line = new ScrollingText
            {
                RelativeSizeAxes = Axes.X,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 15, FontWeight.Bold),
                Color = palette.Text
            }
        };
    }

    /// <summary>
    /// How far it has grown in, 0–1: the bars grow from on time outward.
    /// </summary>
    public float Progress
    {
        get => progress;
        set => progress = Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// The line under the graph (for tests).
    /// </summary>
    public string Line => line.Text;

    public void SetRun(PlayAnalysis analysis)
    {
        run = analysis;
        plottedWidth = -1;

        string mean = analysis.Result.MeanError is double m ? signed(m, "0.0") : "—";
        string deviation = analysis.StandardDeviation is double sd ? sd.ToString("0.0", CultureInfo.InvariantCulture) : "—";
        int early = analysis.TimingErrors.Count(e => e < 0), late = analysis.TimingErrors.Count(e => e > 0);

        line.Text = $"mean {mean} ms · spread ±{deviation} ms · early {early} · late {late}";
    }

    // The span drawn either side of on time: just past the last window that's a hit.
    private double range => (run?.Result.HitWindows.Hit ?? 135) + bin_width / 2;

    public override void Update()
    {
        float width = DrawWidth;

        if (run != null && width > 0 && (Math.Abs(width - plottedWidth) > 0.5f || Math.Abs(progress - plottedProgress) > 0.0001f))
        {
            if (Math.Abs(width - plottedWidth) > 0.5f)
                plotLabels(width);

            plottedWidth = width;
            plottedProgress = progress;
            plot(width);
        }

        base.Update();
    }

    private float xOf(double error, float width) => (float)((error + range) / (2 * range)) * width;

    private void plot(float width)
    {
        PlayAnalysis analysis = run!;
        HitWindows windows = analysis.Result.HitWindows;
        int count = (int)Math.Round(2 * range / bin_width);
        int[] bins = new int[count];

        foreach (double error in analysis.TimingErrors)
        {
            int bin = (int)Math.Floor((error + range) / bin_width);

            if (bin >= 0 && bin < count)
                bins[bin]++;
        }

        int most = Math.Max(1, bins.Max());
        float barWidth = width / count;
        bars.Clear();

        for (int i = 0; i < count; i++)
        {
            if (bins[i] == 0)
                continue;

            double middle = -range + (i + 0.5) * bin_width;
            double away = Math.Abs(middle);

            // Grown from on time outward.
            float distance = (float)(away / range);
            float grown = (float)EasingFunctions.Apply(Easing.OutCubic, Math.Clamp((progress - distance * 0.55f) / 0.45f, 0, 1));
            float height = bins[i] / (float)most * (graph_height - 6) * grown;

            if (height <= 0)
                continue;

            var color = away <= windows.Bloom ? JudgementColors.BLOOM
                : away <= windows.Critical ? JudgementColors.CRITICAL
                : away <= windows.Break ? JudgementColors.BREAK
                : JudgementColors.HIT;

            bars.Box(new RectangleF(i * barWidth + 1, graph_height - height, barWidth - 2, height), color);
        }

        bars.Line(new Vector2(xOf(0, width), 0), new Vector2(xOf(0, width), graph_height), 1, palette.Text.WithAlpha(0.4f), 3, 4);

        if (analysis.Result.MeanError is double mean && progress >= 1)
            bars.Line(new Vector2(xOf(mean, width), 0), new Vector2(xOf(mean, width), graph_height), 2, ColorExtensions.FromHex("#1B1622"));
    }

    private void plotLabels(float width)
    {
        labels.Clear();

        HitWindows windows = run!.Result.HitWindows;

        foreach (double at in new[] { -windows.Break, -windows.Critical, 0, windows.Critical, windows.Break })
            labels.Add(label(at == 0 ? "0" : $"{(at > 0 ? "+" : "−")}{Math.Abs(at):0}", xOf(at, width), Anchor.TopCentre));

        labels.Add(label("early", 0, Anchor.TopLeft));
        labels.Add(label("late", width, Anchor.TopRight));
    }

    private SpriteText label(string text, float x, Anchor origin) => new SpriteText
    {
        Origin = origin,
        Position = new Vector2(x, graph_height + 3),
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 12, FontWeight.Bold),
        Color = ResultsInk.Secondary(palette)
    };

    private static string signed(double value, string format) =>
        (value > 0 ? "+" : value < 0 ? "−" : "±") + Math.Abs(value).ToString(format, CultureInfo.InvariantCulture);
}
