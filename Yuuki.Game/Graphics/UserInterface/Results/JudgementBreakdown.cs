// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// One row per judgement: its count, how many of those were early or late, and a bar showing its
/// share of all notes. Counts and bars roll up together as <see cref="Progress"/> goes 0 -> 1.
/// </summary>
public partial class JudgementBreakdown : FlowContainer
{
    private static readonly HitResult[] shown_results = { HitResult.Critical, HitResult.Break, HitResult.Hit, HitResult.Miss };

    private readonly Row[] rows;

    public JudgementBreakdown(GameplayResult result, YuukiColorPalette palette)
    {
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 6);
        AutoSizeAxes = Axes.Y;

        rows = new Row[shown_results.Length];
        for (int i = 0; i < shown_results.Length; i++)
            Add(rows[i] = new Row(shown_results[i], result, palette));
    }

    /// <summary>
    /// How far the counts and bars have rolled up, 0–1.
    /// </summary>
    public float Progress
    {
        set
        {
            foreach (Row row in rows)
                row.Progress = value;
        }
    }

    private partial class Row : Container
    {
        private const float label_width = 130;
        private const float count_width = 90;
        private const float timing_width = 170;

        private readonly int count;
        private readonly float share;

        private readonly SpriteText countText;
        private readonly Box bar;

        public Row(HitResult hitResult, GameplayResult result, YuukiColorPalette palette)
        {
            RelativeSizeAxes = Axes.X;
            Height = 34;

            count = countOf(hitResult, result);
            share = result.TotalNotes == 0 ? 0 : (float)count / result.TotalNotes;

            Color color = JudgementColors.For(hitResult);

            // A miss has no meaningful press timing (it was never pressed), so it skips the split.
            string timing = hitResult == HitResult.Miss || result.Judgements.Count == 0
                ? ""
                : $"Early {result.EarlyCount(hitResult)}  ·  Late {result.LateCount(hitResult)}";

            Children = new Drawable[]
            {
                new Circle
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(14),
                    Color = color
                },
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 26,
                    Text = hitResult.ToString(),
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold),
                    Color = palette.Text
                },
                countText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreRight,
                    X = label_width + count_width,
                    Text = "0",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                    Color = color
                },
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = label_width + count_width + 20,
                    Text = timing,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                    Color = palette.TextMuted
                },
                // Share-of-notes bar, filling the rest of the row.
                new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 10,
                    Padding = new MarginPadding { Left = label_width + count_width + 20 + timing_width },
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 5,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = palette.Text,
                                Alpha = 0.15f
                            },
                            bar = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0,
                                Color = color
                            }
                        }
                    }
                }
            };
        }

        public float Progress
        {
            set
            {
                countText.Text = ((int)MathF.Round(count * value)).ToString("N0");
                bar.Width = share * value;
            }
        }

        private static int countOf(HitResult hitResult, GameplayResult result) => hitResult switch
        {
            HitResult.Critical => result.CriticalCount,
            HitResult.Break => result.BreakCount,
            HitResult.Hit => result.HitCount,
            HitResult.Miss => result.MissCount,
            _ => throw new ArgumentOutOfRangeException(nameof(hitResult))
        };
    }
}
