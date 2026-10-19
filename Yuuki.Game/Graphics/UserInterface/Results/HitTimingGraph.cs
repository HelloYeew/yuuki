// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
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
/// The hit timing distribution
/// </summary>
public partial class HitTimingGraph : FlowContainer
{
    private const int bin_count = 41;
    private const float graph_height = 120;

    private readonly Box[] bars = new Box[bin_count];
    private readonly float[] barHeights = new float[bin_count];

    public HitTimingGraph(GameplayResult result, YuukiColorPalette palette)
    {
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 6);
        AutoSizeAxes = Axes.Y;

        HitWindows windows = result.HitWindows;
        double range = windows.MissWindow;

        // Bin every hit press's timing error across [−range, +range] (see AffectsTimingStatistics).
        int[] counts = new int[bin_count];
        int early = 0, late = 0;

        foreach (JudgementResult judgement in result.Judgements)
        {
            if (!GameplayResult.AffectsTimingStatistics(judgement))
                continue;

            int bin = (int)((judgement.TimingError + range) / (2 * range) * bin_count);
            counts[Math.Clamp(bin, 0, bin_count - 1)]++;

            if (judgement.TimingError < 0)
                early++;
            else if (judgement.TimingError > 0)
                late++;
        }

        int maxCount = 0;
        foreach (int count in counts)
            maxCount = Math.Max(maxCount, count);

        Container graph;

        Children = new Drawable[]
        {
            graph = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = graph_height,
                Children = new Drawable[]
                {
                    // Judgement zones, widest first so the narrower ones layer on top.
                    zone(1, JudgementColors.HIT),
                    zone((float)(windows.Break / range), JudgementColors.BREAK),
                    zone((float)(windows.Critical / range), JudgementColors.CRITICAL),
                    new Box
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        RelativeSizeAxes = Axes.Y,
                        Width = 2,
                        Color = palette.Text,
                        Alpha = 0.5f
                    }
                }
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 20,
                Children = new Drawable[]
                {
                    axisLabel($"Early  −{range:0} ms", Anchor.CentreLeft, palette),
                    axisLabel("0", Anchor.Centre, palette),
                    axisLabel($"+{range:0} ms  Late", Anchor.CentreRight, palette)
                }
            },
            new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(40, 0),
                Margin = new MarginPadding { Top = 6 },
                Children = new Drawable[]
                {
                    statistic("Mean error", formatMean(result.MeanError), palette),
                    statistic("Unstable rate", result.UnstableRate is double ur ? $"{ur:0.00}" : "—", palette),
                    statistic("Early / Late", $"{early} / {late}", palette)
                }
            }
        };

        if (maxCount == 0)
        {
            graph.Add(new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "No hits to chart",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.Regular),
                Color = palette.Text
            });
            return;
        }

        for (int i = 0; i < bin_count; i++)
        {
            double binCentre = ((i + 0.5) / bin_count * 2 - 1) * range;
            barHeights[i] = (float)counts[i] / maxCount;

            graph.Add(bars[i] = new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomCentre,
                RelativePositionAxes = Axes.X,
                RelativeSizeAxes = Axes.Both,
                X = (i + 0.5f) / bin_count,
                Width = 0.6f / bin_count,
                Height = 0,
                Color = JudgementColors.For(windows.ResultFor(binCentre))
            });
        }
    }

    /// <summary>
    /// Grows the bars up from the baseline, center outwards.
    /// </summary>
    public void AnimateIn()
    {
        for (int i = 0; i < bin_count; i++)
        {
            if (bars[i] == null)
                continue;

            int distanceFromCentre = Math.Abs(i - bin_count / 2);
            bars[i].Wait(distanceFromCentre * 12)
                   .ResizeTo(new Vector2(bars[i].Width, Math.Max(barHeights[i], 0.02f)), 500, Easing.OutQuint);
        }
    }

    private static string formatMean(double? mean)
    {
        if (mean is not double value)
            return "—";

        string side = value < 0 ? "early" : value > 0 ? "late" : "";
        return $"{Math.Abs(value):0.00} ms {side}".TrimEnd();
    }

    private static Box zone(float width, Color color) => new Box
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both,
        Width = width,
        Color = color,
        Alpha = 0.12f
    };

    private static SpriteText axisLabel(string text, Anchor anchor, YuukiColorPalette palette) => new SpriteText
    {
        Anchor = anchor,
        Origin = anchor,
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Regular),
        Color = palette.TextMuted
    };

    private static Drawable statistic(string title, string value, YuukiColorPalette palette) => new FlowContainer()
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 2),
        Children = new Drawable[]
        {
            new SpriteText
            {
                Text = title,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Regular),
                Color = palette.TextMuted
            },
            new SpriteText
            {
                Text = value,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                Color = palette.Text
            }
        }
    };
}
