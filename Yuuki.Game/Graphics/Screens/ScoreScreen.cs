// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface.Results;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Screens;

/// <summary>
/// The result screen
/// </summary>
public partial class ScoreScreen : YuukiScreen, IHaveScreenAction
{
    /// <summary>
    /// Delay before the ring starts filling, letting the panels slide in first.
    /// </summary>
    private const double fill_delay = 500;

    private const double fill_duration = 1800;

    /// <summary>
    /// Gap between fill ticks. Ticks stop once the fill has visibly settled.
    /// </summary>
    private const double tick_interval = 45;

    private const float ring_size = 400;

    /// <summary>
    /// Gap between the rank landing and the music starting, when there's no badge.
    /// </summary>
    private const double music_delay_after_rank = 900;

    /// <summary>
    /// Gap between the FC/AP badge and the music, leaving room for its fanfare.
    /// </summary>
    private const double music_delay_after_badge = 1200;

    /// <summary>
    /// How long a lost rank's strike-off holds the stage before the earned rank lands.
    /// </summary>
    private const double rank_lost_duration = 650;

    // Set once the screen starts leaving, so the delayed music start can tell not to.
    private bool exiting;

    /// <summary>Suggestions closer than this to the offset already in use aren't worth offering.</summary>
    private const int minimum_offset_change = 3;

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool AllowTrackChange => false;

    public override bool HideFooter => false;

    public override bool AllowBackgroundBlur => true;

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    private readonly GameplayResult result;

    /// <summary>
    /// The run being shown.
    /// </summary>
    public GameplayResult Result => result;

    /// <summary>
    /// Whether the rank letter has landed (the end of the reveal sequence's main beat).
    /// </summary>
    public bool RankRevealed { get; private set; }

    private YuukiColorPalette palette;

    private Box dim = null!;
    private BeatmapInfoPanel header = null!;
    private Container ringArea = null!;
    private FlowContainer details = null!;

    // The stats column, scrolling when the canvas is too short for it (a larger UI scale).
    private YuukiScrollableContainer detailsScroll = null!;

    /// <summary>
    /// The ring area's x once it has slid in.
    /// </summary>
    private const float ring_area_x = 110;

    /// <summary>
    /// The ring area's top: below the song header.
    /// </summary>
    private const float ring_area_y = 312;

    private const float details_top = 48;
    private const float details_max_width = 1080;
    private const float details_min_width = 600;
    private LeftBottomShearButton backButton = null!;

    private Container actionButtons = null!;

    private MenuShearButton? retryButton;
    private MenuShearButton? replayButton;

    private static readonly Vector2 action_button_size = new Vector2(250, 66);

    /// <summary>
    /// Where the action buttons rest: as far in from the right edge as the corner buttons sit.
    /// </summary>
    private static readonly Vector2 action_buttons_position = new Vector2(-48, LeftBottomShearButton.DEFAULT_POSITION.Y);

    private readonly bool isNewBest;
    private readonly Action? onRetry;

    // The play as saved, with its replay; null when it wasn't saved (a generated chart).
    private readonly ScoreInfo? savedScore;
    private readonly ReplayData? replay;

    // Looking at a saved score again (from the Ranking tab), rather than a play that just ended.
    private readonly bool viewing;

    // A saved score is looked at on song select's music, which keeps looping under it.
    public override bool LoopPreview => viewing;

    // A replay with inputs to watch; scores saved before replays existed have none.
    private bool canWatch => replay is { Inputs.Count: > 0 };

    /// <summary>
    /// Whether there's a replay to watch from here.
    /// </summary>
    public bool CanWatchReplay => canWatch;

    [Resolved(canBeNull: true)]
    private YuukiScreenStack? screenStack { get; set; }

    private AccuracyCircle accuracyCircle = null!;
    private Container badge = null!;
    private SpriteText scoreText = null!;
    private SpriteText comboText = null!;
    private SpriteText accuracyText = null!;
    private JudgementBreakdown judgementBreakdown = null!;
    private HitTimingGraph timingGraph = null!;
    private JudgementTimeline timeline = null!;

    private ResultsSoundPlayer sounds = null!;

    private double revealStartTime = double.NaN;
    private double lastTickTime = double.NegativeInfinity;

    /// <summary>
    /// Shows a saved score again: no results music, no retry or offset suggestion, and a Replay button
    /// (locked when the score has no replay). The breakdown is full only when a replay was saved.
    /// </summary>
    /// <param name="score">The saved play.</param>
    /// <param name="replay">Its replay, if one was saved with it.</param>
    public ScoreScreen(ScoreInfo score, ReplayData? replay)
        : this(GameplayResult.FromScore(score, replay), score: score, replay: replay)
    {
        viewing = true;
    }

    /// <summary>A saved score's results screen, its replay read from <paramref name="scoreStore"/> if it has one.</summary>
    public static ScoreScreen ForSavedScore(ScoreInfo score, ScoreStore? scoreStore) => new ScoreScreen(score, scoreStore?.GetReplay(score))
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both
    };

    /// <param name="result">The run to show.</param>
    /// <param name="isNewBest">Whether it beat the previous best on this difficulty.</param>
    /// <param name="onRetry">
    /// Called when Retry is chosen, just before this screen exits; the gameplay screen then restarts
    /// the run instead of cascading back to song select. Null hides Retry.
    /// </param>
    /// <param name="score">The run as saved, if it was; with <paramref name="replay"/>, Replay can watch it.</param>
    /// <param name="replay">The run's replay, if it was saved.</param>
    public ScoreScreen(GameplayResult result, bool isNewBest = false, Action? onRetry = null, ScoreInfo? score = null, ReplayData? replay = null)
    {
        this.result = result;
        this.isNewBest = isNewBest;
        this.onRetry = onRetry;

        savedScore = score;
        this.replay = replay;

        ScreenActions = new List<ScreenAction>
        {
            new ScreenAction
            {
                Name = "Back",
                Action = onBack,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape))
            }
        };

        // R retries after a play; on a saved score (no retry) it watches the replay instead.
        if (onRetry != null)
        {
            ((List<ScreenAction>)ScreenActions).Add(new ScreenAction
            {
                Name = "Retry",
                Action = retry,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.R))
            });
        }
        else if (canWatch)
        {
            ((List<ScreenAction>)ScreenActions).Add(new ScreenAction
            {
                Name = "Replay",
                Action = WatchReplay,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.R))
            });
        }
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = "Results";
        ScreenHelperText.Value = savedScore != null ? $"Played {savedScore.Date.LocalDateTime:d MMM yyyy, HH:mm}"
            : result.IsAllPerfect ? "All Perfect!"
            : result.IsFullCombo ? "Full Combo!"
            : "Nice play!";

        palette = Palette;

        AddRange(new Drawable[]
        {
            sounds = new ResultsSoundPlayer(),
            // Keeps the panels readable over bright backgrounds.
            dim = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = palette.Backdrop,
                Alpha = 0
            },
            header = createHeader(),
            ringArea = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-ring_size, ring_area_y),
                Size = new Vector2(ring_size + 160, ring_size + 140),
                Alpha = 0,
                Children = new Drawable[]
                {
                    accuracyCircle = new AccuracyCircle(palette)
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(ring_size)
                    },
                    badge = createBadge()
                }
            },
            detailsScroll = new YuukiScrollableContainer
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(400, details_top),
                Size = new Vector2(details_max_width, 800),
                Alpha = 0,
                Child = details = new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 14),
                    Children = new Drawable[]
                    {
                        panel(createScoreSummary()),
                        panel(createJudgementSection()),
                        panel(createTimingSection())
                    }
                }
            },
            backButton = new LeftBottomShearButton
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y),
                Alpha = 0,
                Text = "Back",
                Action = onBack,
                BackgroundColor = palette.Surface
            },
            actionButtons = new Container
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = action_buttons_position + new Vector2(600, 0),
                AutoSizeAxes = Axes.Both,
                Alpha = 0
            }
        });

        // Replay first, then Retry at the far right when there is one (the usual next step after a play).
        float x = 0;

        // A saved score always offers it (locked without a replay); after a play, only once it's saved.
        if (viewing || canWatch)
        {
            actionButtons.Add(replayButton = new MenuShearButton
            {
                X = x,
                ButtonSize = action_button_size,
                Text = "Replay",
                Icon = canWatch ? IconUsage.PlayCircle : IconUsage.Lock,
                Locked = !canWatch,
                Action = WatchReplay,
                BackgroundColor = palette.Surface,
                FlashToBeat = false
            });
            x += action_button_size.X + 36;
        }

        if (onRetry != null)
        {
            actionButtons.Add(retryButton = new MenuShearButton
            {
                X = x,
                ButtonSize = action_button_size,
                Text = "Retry",
                Icon = IconUsage.Replay,
                Action = retry,
                BackgroundColor = palette.Surface,
                FlashToBeat = false
            });
        }

        accuracyCircle.ThresholdCrossed += sounds.PlayThreshold;
    }

    #region Layout

    /// <summary>
    /// The song this run was played on: song select's panel, compact (title and difficulty row).
    /// </summary>
    private BeatmapInfoPanel createHeader()
    {
        var panel = new BeatmapInfoPanel("Results", palette, BeatmapInfoPanel.PanelLayout.Compact)
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(-30, 48)
        };

        if (workingBeatmap?.Beatmap != null)
            panel.ShowBeatmap(workingBeatmap.Beatmap);
        else
            panel.ShowSet(workingBeatmap?.BeatmapSet);

        return panel;
    }

    /// <summary>
    /// The tag under the ring: All Perfect / Full Combo, or for a run a miss kept from S, the rule that
    /// cost it (the miss count is in the breakdown beside it). Empty (and never shown) otherwise.
    /// </summary>
    private Container createBadge()
    {
        var container = new Container
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.Centre,
            Y = -50,
            Size = new Vector2(300, 60),
            Alpha = 0
        };

        if (result.RankLostToMiss)
            return fillBadge(container, JudgementColors.MISS, $"{result.AccuracyRank} NEEDS NO MISS", 26);

        if (!result.IsFullCombo)
            return container;

        return fillBadge(container,
            result.IsAllPerfect ? JudgementColors.ALL_PERFECT : JudgementColors.FULL_COMBO,
            result.IsAllPerfect ? "ALL PERFECT" : "FULL COMBO");
    }

    /// <summary>
    /// A sheared, solid-colour tag with white text, filling <paramref name="container"/>.
    /// </summary>
    private static Container fillBadge(Container container, Color color, string text, int fontSize = 30)
    {
        container.Children = new Drawable[]
        {
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Shear = UserInterfaceConstant.Shear,
                CornerRadius = 6,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = color
                }
            },
            new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = text,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, fontSize, FontWeight.Bold),
                Color = Color.White
            }
        };

        return container;
    }

    /// <summary>
    /// Score, max combo and accuracy — the numbers that roll up with the ring.
    /// </summary>
    private Drawable createScoreSummary() => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = 104,
        Children = new Drawable[]
        {
            new FlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Children = new Drawable[]
                {
                    new FlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(12, 0),
                        Children = isNewBest
                            ? new Drawable[] { sectionTitle("Score"), newBestTag() }
                            : new Drawable[] { sectionTitle("Score") }
                    },
                    scoreText = new SpriteText
                    {
                        Text = formatScore(0),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 72, FontWeight.Bold),
                        Color = palette.Text
                    }
                }
            },
            new FlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(56, 0),
                Children = new Drawable[]
                {
                    statistic("Max combo", comboText = statisticValue()),
                    statistic("Accuracy", accuracyText = statisticValue())
                }
            }
        }
    };

    private Drawable createJudgementSection()
    {
        var section = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Children = new Drawable[]
            {
                judgementBreakdown = new JudgementBreakdown(result, palette)
                {
                    RelativeSizeAxes = Axes.X
                }
            }
        };

        string byType = describeNoteTypes();
        if (!string.IsNullOrEmpty(byType))
        {
            section.Add(new SpriteText
            {
                Text = byType,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                Color = palette.TextMuted
            });
        }

        return section;
    }

    private Drawable createTimingSection() => new FlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 8),
        Children = new Drawable[]
        {
            sectionTitle("Hit timing"),
            timingGraph = new HitTimingGraph(result, palette)
            {
                RelativeSizeAxes = Axes.X
            },
            createOffsetSuggestion(),
            sectionTitle("Timeline"),
            timeline = new JudgementTimeline(result, palette)
            {
                RelativeSizeAxes = Axes.X
            }
        }
    };

    /// <summary>
    /// Offers to correct the audio offset from this run's mean error
    /// </summary>
    private Drawable createOffsetSuggestion()
    {
        var row = new FlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(16, 0)
        };

        // A saved score's offset is history; only the play just finished is worth calibrating from.
        if (viewing || config == null || result.SuggestedOffset is not double suggested || result.MeanError is not double mean)
            return row;

        int target = (int)Math.Clamp(suggested, YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_AUDIO_OFFSET);
        if (Math.Abs(target - result.AudioOffset) < minimum_offset_change)
            return row;

        var offset = config.Get<int>(YuukiSetting.AudioOffset);

        PillButton apply = null!;

        row.Children = new Drawable[]
        {
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = $"You hit {Math.Abs(mean):0} ms {(mean > 0 ? "late" : "early")} on average.",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                Color = palette.Text
            },
            apply = new PillButton(palette.Control, palette.TextLight)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = $"Set audio offset to {formatOffset(target)}",
                Action = () =>
                {
                    offset.Value = target;
                    apply.Text = $"Audio offset set to {formatOffset(target)}";
                    apply.Enabled.Value = false;
                }
            }
        };

        return row;
    }

    private static string formatOffset(int ms) => ms > 0 ? $"+{ms} ms" : $"{ms} ms";

    /// <summary>
    /// Hits per note type, e.g. "Tap 120/124 · Hold 30/32 · Release 27/32", for the types the chart
    /// used. A hold's release is its own judgement, so it's listed apart from the hold's press.
    /// </summary>
    private string describeNoteTypes()
    {
        var parts = result.Judgements
                          .GroupBy(j => (j.Note.Type, j.IsHoldTail))
                          .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.IsHoldTail)
                          .Select(g => $"{(g.Key.IsHoldTail ? "Release" : g.Key.Type.ToString())} {g.Count(j => j.Result.IsHit())}/{g.Count()}")
                          .ToList();

        if (result.HoldBreakCount > 0)
            parts.Add($"Hold breaks {result.HoldBreakCount}");

        return string.Join("   ·   ", parts);
    }

    private Container panel(Drawable content) => new Container
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Masking = true,
        CornerRadius = 12,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = palette.Surface,
                Alpha = 0.92f
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Top = 16, Bottom = 18, Left = 28, Right = 28 },
                Child = content
            }
        }
    };

    private SpriteText sectionTitle(string text) => new SpriteText
    {
        Text = text.ToUpperInvariant(),
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold),
        Color = palette.TextMuted
    };

    private SpriteText statisticValue() => new SpriteText
    {
        Anchor = Anchor.TopRight,
        Origin = Anchor.TopRight,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 36, FontWeight.Bold),
        Color = palette.Text
    };

    private Drawable statistic(string title, SpriteText value) => new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 2),
        Children = new Drawable[] { sectionTitle(title), value }
    };

    /// <summary>
    /// Zero-padded to seven digits, matching the gameplay HUD's score readout.
    /// </summary>
    private static string formatScore(double score) => ((int)Math.Round(score)).ToString("D7");

    #endregion

    #region Reveal

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);

        this.FadeIn(400, Easing.OutQuint);
        dim.FadeTo(0.35f, 400, Easing.OutQuint);
        header.AnimateIn();
        ringArea.MoveToX(ring_area_x, 600, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        detailsScroll.MoveToX(-48, 600, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        backButton.MoveTo(LeftBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        actionButtons.MoveTo(action_buttons_position, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);


        revealStartTime = Clock.CurrentTime + fill_delay;
        updateRolling(0);

        Scheduler.AddDelayed(() =>
        {
            timingGraph.AnimateIn();
            timeline.AnimateIn();
        }, fill_delay + fill_duration * 0.4);

        double rankTime = fill_delay + fill_duration + 100;

        // A miss kept the run from the rank its accuracy reached: strike that off first, then land
        // the rank it did earn.
        if (result.RankLostToMiss)
        {
            Scheduler.AddDelayed(loseRank, rankTime);
            rankTime += rank_lost_duration;
        }

        Scheduler.AddDelayed(revealRank, rankTime);

        if (result.IsFullCombo)
            Scheduler.AddDelayed(revealBadge, rankTime + 500);

        // The music waits for the reveal to finish — rank, then the badge if there is one — so the
        // ticks and fanfare play out first; and for the play's song, still going when the chart ended
        // before it, to play out (BeginResultsMusic holds it until then).
        double revealEnd = rankTime + (result.IsFullCombo ? 500 + music_delay_after_badge : music_delay_after_rank);
        // A saved score is looked at, not celebrated: no music, the song select track plays on.
        if (!viewing)
        {
            Scheduler.AddDelayed(() =>
            {
                // Not if the player has left, or gone to watch the replay before it was due.
                if (!exiting && IsCurrentScreen)
                    audioManager?.BeginResultsMusic();
            }, revealEnd);
        }
    }

    public override void Update()
    {
        base.Update();

        fitToCanvas();

        if (double.IsNaN(revealStartTime) || RankRevealed)
            return;

        double elapsed = Clock.CurrentTime - revealStartTime;
        if (elapsed < 0)
            return;

        float linear = (float)Math.Min(1, elapsed / fill_duration);
        updateRolling(easeOut(linear));

        // Ticks only while the ring is visibly moving; the eased tail would otherwise tick on nothing.
        if (linear < 0.75f && Clock.CurrentTime - lastTickTime >= tick_interval)
        {
            lastTickTime = Clock.CurrentTime;
            sounds.PlayTick(linear);
        }
    }

    /// <summary>
    /// Keeps the layout inside the canvas, which shrinks as the UI scale grows: the stats column takes
    /// the width left beside the ring and scrolls if it's taller than the room above the buttons, and
    /// the ring area shrinks only when it would otherwise reach down into them.
    /// </summary>
    private void fitToCanvas()
    {
        float buttonsTop = DrawSize.Y + LeftBottomShearButton.DEFAULT_POSITION.Y - backButton.DrawSize.Y - 16;
        float ringRight = ring_area_x + ringArea.DrawSize.X;

        detailsScroll.Width = Math.Clamp(DrawSize.X - 48 - ringRight - 32, details_min_width, details_max_width);
        detailsScroll.Height = Math.Max(240, buttonsTop - details_top);

        float ringRoom = buttonsTop - ring_area_y;
        ringArea.Scale = new Vector2(Math.Clamp(ringRoom / ringArea.DrawSize.Y, 0.6f, 1f));
    }

    /// <summary>
    /// Sets every rolling readout to <paramref name="progress"/> (0–1) of its final value.
    /// </summary>
    private void updateRolling(float progress)
    {
        accuracyCircle.Progress = (float)result.Accuracy * progress;
        judgementBreakdown.Progress = progress;

        scoreText.Text = formatScore(result.Score * progress);
        comboText.Text = $"{(int)MathF.Round(result.MaxCombo * progress)}x / {result.TotalNotes}";
        accuracyText.Text = $"{result.Accuracy * 100 * progress:0.00}%";
    }

    private void revealRank()
    {
        RankRevealed = true;
        updateRolling(1);

        accuracyCircle.RevealRank(result.Rank);
        sounds.PlayRankReveal(result.Rank);
    }

    private void loseRank()
    {
        // The fill has settled by now; make sure it reads the final accuracy before striking.
        updateRolling(1);

        accuracyCircle.LoseRank(result.AccuracyRank, result.Rank);
        sounds.PlayRankLost();
        popBadge();
    }

    private void revealBadge()
    {
        popBadge();

        if (result.IsAllPerfect)
            sounds.PlayAllPerfect();
        else
            sounds.PlayFullCombo();
    }

    private void popBadge()
    {
        badge.Scale = new Vector2(1.6f);
        badge.ScaleTo(1f, 450, Easing.OutQuint).FadeIn(200, Easing.OutQuint);
    }

    // Quartic ease-out: fast at first, settling gently onto the final value.
    private static float easeOut(float t) => 1 - MathF.Pow(1 - t, 4);

    #endregion

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        // Leaving before the music was due: stop it from starting under song select.
        exiting = true;

        audioManager?.EndResultsMusic();

        dim.FadeOut(300, Easing.OutQuint);
        header.AnimateOut();
        ringArea.FadeOut(300, Easing.OutQuint);
        detailsScroll.MoveToX(400, 400, Easing.OutQuint).FadeOut(300, Easing.OutQuint);
        backButton.MoveTo(new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y), 400, Easing.OutQuint).FadeOut(400, Easing.OutQuint);
        actionButtons.MoveTo(action_buttons_position + new Vector2(600, 0), 400, Easing.OutQuint).FadeOut(400, Easing.OutQuint);
        this.FadeOut(300, Easing.OutQuint);
    }

    // Watching a replay covers this screen; it comes back as it was, without replaying the reveal.
    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        this.FadeOut(300, Easing.OutQuint);
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);
        this.FadeIn(300, Easing.OutQuint);

        // Back from a replay after a play: the results music picks up again.
        if (!viewing && RankRevealed)
            audioManager?.BeginResultsMusic();
    }

    /// <summary>
    /// Opens the play's replay (what the Replay button, and R on a saved score, do). Nothing without one.
    /// </summary>
    public void WatchReplay()
    {
        if (!canWatch || savedScore == null || replay == null || screenStack == null)
            return;

        replayButton?.Flash(Color.White);

        // The replay brings the song; the results music would play over it.
        audioManager?.EndResultsMusic();

        screenStack.Push(new ReplayScreen(savedScore, replay)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });
    }

    private void onBack()
    {
        backButton.Flash(Color.White);
        Exit();
    }

    private void retry()
    {
        if (onRetry == null)
            return;

        retryButton?.Flash(Color.White);
        onRetry();
        Exit();
    }

    /// <summary>
    /// A small gold "NEW BEST" tag beside the score title.
    /// </summary>
    private Drawable newBestTag() => new Container
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        AutoSizeAxes = Axes.Both,
        Masking = true,
        CornerRadius = 4,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = JudgementColors.CRITICAL
            },
            new SpriteText
            {
                Margin = new MarginPadding { Left = 8, Right = 8, Top = 2, Bottom = 3 },
                Text = "NEW BEST",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 14, FontWeight.Bold),
                Color = Color.White
            }
        }
    };
}
