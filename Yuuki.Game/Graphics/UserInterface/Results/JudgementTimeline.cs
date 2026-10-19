// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.DrawableExtensions;
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
/// Where in the song each judgement happened: the chart is split into columns, each colored and
/// sized by the worst judgement inside it, so the trouble spots (misses) stand out at a glance.
/// </summary>
public partial class JudgementTimeline : FlowContainer
{
    private const int column_count = 120;
    private const float strip_height = 36;

    private readonly Box?[] columns = new Box?[column_count];
    private readonly float[] columnHeights = new float[column_count];

    public JudgementTimeline(GameplayResult result, YuukiColorPalette palette)
    {
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 4);
        AutoSizeAxes = Axes.Y;

        Container strip;

        Children = new Drawable[]
        {
            strip = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = strip_height,
                Child = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Color = palette.Text,
                    Alpha = 0.3f
                }
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 18,
                Children = new Drawable[]
                {
                    timeLabel(0, Anchor.CentreLeft, palette),
                    timeLabel(result.ChartLength, Anchor.CentreRight, palette)
                }
            }
        };

        if (result.Judgements.Count == 0 || result.ChartLength <= 0)
            return;

        // The worst judgement per column (None = nothing happened there).
        var worst = new HitResult[column_count];

        foreach (JudgementResult judgement in result.Judgements)
        {
            int column = (int)(judgement.Note.StartTime / result.ChartLength * column_count);
            column = Math.Clamp(column, 0, column_count - 1);

            if (worst[column] == HitResult.None || judgement.Result > worst[column])
                worst[column] = judgement.Result;
        }

        for (int i = 0; i < column_count; i++)
        {
            if (worst[i] == HitResult.None)
                continue;

            columnHeights[i] = heightFor(worst[i]);

            strip.Add(columns[i] = new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                RelativePositionAxes = Axes.X,
                RelativeSizeAxes = Axes.Both,
                X = (float)i / column_count,
                Width = 0.7f / column_count,
                Height = 0,
                Color = JudgementColors.For(worst[i])
            });
        }
    }

    /// <summary>
    /// Grows the columns in from the start of the song to its end.
    /// </summary>
    public void AnimateIn()
    {
        for (int i = 0; i < column_count; i++)
        {
            Box? column = columns[i];
            column?.Wait(i * 4).ResizeTo(new Vector2(column.Width, columnHeights[i]), 400, Easing.OutQuint);
        }
    }

    /// <summary>
    /// Misses tower over clean hits, so they're the first thing the eye finds.
    /// </summary>
    private static float heightFor(HitResult result) => result switch
    {
        HitResult.Critical => 0.35f,
        HitResult.Break => 0.55f,
        HitResult.Hit => 0.75f,
        _ => 1f
    };

    private static SpriteText timeLabel(double time, Anchor anchor, YuukiColorPalette palette) => new SpriteText
    {
        Anchor = anchor,
        Origin = anchor,
        Text = TimeSpan.FromMilliseconds(Math.Max(0, time)).ToString(@"m\:ss"),
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Regular),
        Color = palette.TextMuted
    };
}
