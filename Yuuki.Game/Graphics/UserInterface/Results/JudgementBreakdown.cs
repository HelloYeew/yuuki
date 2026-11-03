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
    private static readonly HitResult[] shown_results = { HitResult.Bloom, HitResult.Critical, HitResult.Break, HitResult.Hit, HitResult.Miss };

    /// <summary>
    /// A row's height and the space between rows, and the same when <see cref="Compact"/>: still the height of the
    /// tallest text in a row (the count's).
    /// </summary>
    public const float ROW_HEIGHT = 34;

    public const float ROW_SPACING = 6;
    public const float COMPACT_ROW_HEIGHT = 30;
    public const float COMPACT_ROW_SPACING = 4;

    private readonly Row[] rows;
    private bool compact;

    public JudgementBreakdown(GameplayResult result, YuukiColorPalette palette)
    {
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, ROW_SPACING);
        AutoSizeAxes = Axes.Y;

        rows = new Row[shown_results.Length];
        for (int i = 0; i < shown_results.Length; i++)
            Add(rows[i] = new Row(shown_results[i], result, palette));
    }

    /// <summary>
    /// Rows a little shorter and closer together, for a column without the room for them as they are.
    /// </summary>
    public bool Compact
    {
        get => compact;
        set
        {
            if (compact == value)
                return;

            compact = value;
            Spacing = new Vector2(0, value ? COMPACT_ROW_SPACING : ROW_SPACING);

            foreach (Row row in rows)
                row.Height = value ? COMPACT_ROW_HEIGHT : ROW_HEIGHT;
        }
    }

    /// <summary>
    /// How tall it is when it isn't <see cref="Compact"/>, whether it is or not.
    /// </summary>
    public float FullHeight => rows.Length * ROW_HEIGHT + Math.Max(0, rows.Length - 1) * ROW_SPACING;

    /// <summary>
    /// Fills in how many of each were early or late, from <paramref name="result"/>'s judgements (a saved play's come in
    /// once its replay is judged again).
    /// </summary>
    public void ShowTiming(GameplayResult result)
    {
        foreach (Row row in rows)
            row.ShowTiming(result);
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

        private readonly HitResult hitResult;
        private readonly int count;
        private readonly float share;

        private readonly SpriteText countText;
        private readonly SpriteText timingText;
        private readonly Drawable bar;

        public Row(HitResult hitResult, GameplayResult result, YuukiColorPalette palette)
        {
            RelativeSizeAxes = Axes.X;
            Height = ROW_HEIGHT;

            this.hitResult = hitResult;
            count = countOf(hitResult, result);
            share = result.TotalNotes == 0 ? 0 : (float)count / result.TotalNotes;

            Color color = JudgementColors.For(hitResult);

            // A Bloom's dot and bar take the heroes' rainbow, moving.
            bool rainbow = hitResult == HitResult.Bloom;

            Children = new Drawable[]
            {
                rainbow
                    ? new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(14),
                        Masking = true,
                        CornerRadius = 7,
                        Child = new RainbowFill { RelativeSizeAxes = Axes.Both }
                    }
                    : new Circle
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
                timingText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = label_width + count_width + 20,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                    Color = ResultsInk.Secondary(palette)
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
                            bar = rainbow
                                ? new RainbowFill
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0
                                }
                                : new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Width = 0,
                                    Color = color
                                }
                        }
                    }
                }
            };

            ShowTiming(result);
        }

        // A miss has no meaningful press timing (it was never pressed), so it skips the split.
        public void ShowTiming(GameplayResult result) => timingText.Text = hitResult == HitResult.Miss || result.Judgements.Count == 0
            ? ""
            : $"Early {result.EarlyCount(hitResult)}  ·  Late {result.LateCount(hitResult)}";

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
            HitResult.Bloom => result.BloomCount,
            HitResult.Critical => result.CriticalCount,
            HitResult.Break => result.BreakCount,
            HitResult.Hit => result.HitCount,
            HitResult.Miss => result.MissCount,
            _ => throw new ArgumentOutOfRangeException(nameof(hitResult))
        };
    }
}
