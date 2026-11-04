// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// Where the run stands among the plays of its difficulty, along the bottom of the results' summary: the best's mark (the
/// run's stamping over it when it's a new best), its place of how many and when it was played (the footer that says so
/// is hidden on a phone), and the best it's compared with. A run that failed isn't placed: it says where it ran out, and
/// that the best stays as it was (<see cref="Failed"/>).
/// </summary>
public partial class PlaysStrip : Container
{
    // The marks' room at the left, and the space before the lines.
    private const float mark_width = 150;
    private const float mark_spacing = 20;

    private readonly BestMark before;
    private readonly BestMark after;
    private readonly bool newBest;
    private readonly bool hadBest;

    /// <param name="palette">Its colors.</param>
    /// <param name="run">The run, as saved.</param>
    /// <param name="best">The best it's compared with: the best before it, or (a saved play looked at) the best but it.</param>
    /// <param name="isNewBest">Whether the run beat <paramref name="best"/>.</param>
    /// <param name="place">Its place among the difficulty's plays, from 1.</param>
    /// <param name="plays">How many plays of the difficulty there are, the run's among them.</param>
    public PlaysStrip(YuukiColorPalette palette, ScoreInfo run, ScoreInfo? best, bool isNewBest, int place, int plays)
        : this(palette, best ?? (isNewBest ? null : run), run, isNewBest,
            (isNewBest ? $"New best · #1 of {plays} plays" : $"#{place} of {plays} plays") + $" · played {DateUtility.FormatPlayed(run.Date)}",
            best == null ? isNewBest || place == 1 ? "Your first play of this difficulty" : ""
            : $"{(isNewBest ? "Before" : "Your best")}: {((int)best.Score).ToString("D7")} · {(best.Accuracy * 100).ToString("0.00", CultureInfo.InvariantCulture)}% · {DateUtility.Ago(best.Date)}")
    {
    }

    /// <summary>
    /// A run that ran out of health: not placed among the plays, so it says where it ran out and how many tries the song
    /// has had today, and the best's mark stays (FAILED where there's no best yet).
    /// </summary>
    /// <param name="palette">Its colors.</param>
    /// <param name="run">The run, as saved.</param>
    /// <param name="best">The best, which the run leaves as it was; null where there's none.</param>
    /// <param name="failedAt">Where health ran out, in ms of the song; null where that isn't known.</param>
    /// <param name="triesToday">How many plays of the difficulty there have been today, the run's among them.</param>
    public static PlaysStrip Failed(YuukiColorPalette palette, ScoreInfo run, ScoreInfo? best, double? failedAt, int triesToday) =>
        new PlaysStrip(palette, best ?? run, run, false, FailedRun.Standing(failedAt), FailedRun.Tries(triesToday, best?.Score, best?.Accuracy));

    private PlaysStrip(YuukiColorPalette palette, ScoreInfo? shown, ScoreInfo run, bool isNewBest, string where, string compared)
    {
        newBest = isNewBest;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        // The mark shown at first is the best's; a new best's own stamps over it.
        before = new BestMark(17) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft };
        after = new BestMark(17) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, AlwaysPresent = true };
        before.SetBest(shown);
        after.SetBest(run);
        after.Alpha = 0;
        hadBest = before.Alpha > 0;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1.5f,
                Color = palette.Text.WithAlpha(0.14f)
            },
            new Container
            {
                Y = 22,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(mark_width, 34),
                        Children = new Drawable[] { before, after }
                    },
                    // The rest of the width, the lines scrolling where a narrow column cuts them short.
                    new FlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Left = mark_width + mark_spacing },
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new ScrollingText
                            {
                                RelativeSizeAxes = Axes.X,
                                Text = where,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 19, FontWeight.Bold),
                                Color = palette.Text
                            },
                            new ScrollingText
                            {
                                RelativeSizeAxes = Axes.X,
                                Text = compared,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.SemiBold),
                                Color = ResultsInk.Secondary(palette)
                            }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// How far the run has been compared with the best, 0–1: a new best's mark stamps over the old one.
    /// </summary>
    public void Compare(float progress)
    {
        if (!newBest)
            return;

        float stamped = (float)EasingFunctions.Apply(Easing.OutCubic, Math.Clamp(progress, 0, 1));
        after.Alpha = Math.Clamp(progress * 3, 0, 1);
        after.Scale = new Vector2(1 + 0.4f * (1 - stamped));
        if (hadBest)
            before.Alpha = 1 - after.Alpha;
    }
}
