// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// A mean error past this is more likely a new delay (other headphones, another screen) than the player's own lean,
    /// so calibrating is offered too: only that puts the notes back in time with the music.
    /// </summary>
    private const double calibrate_error_ms = 40;

    /// <summary>
    /// Criticals a run's lean cost before it's worth saying so, and pointing to how the offsets fix it.
    /// </summary>
    private const int criticals_worth_mentioning = 3;

    /// <summary>
    /// How far a run has to lean, in ms, before what it cost is worth pointing out. A smaller lean comes and goes from
    /// play to play with the song and the player, and over a long song still costs a few Criticals at the window's edge.
    /// </summary>
    private const double lean_worth_mentioning = 15;

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

    [Resolved(canBeNull: true)]
    private OffsetManager? offsets { get; set; }

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

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
    /// The ring area's top: below the song header. On a short canvas it moves up toward the header.
    /// </summary>
    private const float ring_area_y = 312;

    /// <summary>
    /// Where the song header sits, and how wide it is at scale 1.
    /// </summary>
    private static readonly Vector2 header_position = new Vector2(-30, 48);

    private const float header_width = 680;

    /// <summary>
    /// The score summary's width below which max combo and accuracy go under the score instead of
    /// beside it.
    /// </summary>
    private const float summary_one_line_width = 800;

    private const float summary_height = 104;
    private const float summary_stacked_height = 180;

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

    private Container scoreSummary = null!;
    private Drawable scoreBlock = null!;
    private Drawable statsBlock = null!;
    private bool summaryStacked;

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
        ScreenHelperText.Value = savedScore != null ? string.Create(CultureInfo.InvariantCulture, $"Played {savedScore.Date.LocalDateTime:d MMM yyyy, HH:mm}")
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
                Bleed = Edges.All,
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
            Position = header_position
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
    private Drawable createScoreSummary() => scoreSummary = new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = summary_height,
        Children = new Drawable[]
        {
            scoreBlock = new FlowContainer
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
            statsBlock = new FlowContainer
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

        IReadOnlyList<string> byType = describeNoteTypes();
        if (byType.Count > 0)
        {
            // Wraps between note types when the column is narrow.
            section.Add(new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(0, 2),
                Children = byType.Select((part, i) => new SpriteText
                {
                    Text = i < byType.Count - 1 ? part + "   ·   " : part,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                    Color = palette.TextMuted
                }).ToArray()
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
    /// Offers to centre this run's taps by moving the judging (<see cref="GameplayResult.SuggestedOffsets"/>), and for a
    /// large error, to calibrate. Where a lean past <see cref="lean_worth_mentioning"/> cost Criticals, says how many, and
    /// offers the guide to the offsets.
    /// </summary>
    private Drawable createOffsetSuggestion()
    {
        Drawable suggestion = createOffsetButtons();

        int lost = viewing || config == null || Math.Abs(result.MeanError ?? 0) < lean_worth_mentioning ? 0 : result.CriticalsLostToLean;

        if (lost < criticals_worth_mentioning)
            return suggestion;

        return new FlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Children = new[]
            {
                suggestion,
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(16, 0),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = $"On time on average, {lost} more would have been Critical. A lean this big every play is your device's delay, not you.",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                            Color = palette.Text
                        },
                        new PillButton(palette.Control, palette.TextLight)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "How offsets work",
                            Action = openOffsetGuide
                        }
                    }
                }
            }
        };
    }

    private Drawable createOffsetButtons()
    {
        var row = new FlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(16, 0)
        };

        // A saved score's offsets are history; only the play just finished is worth calibrating from.
        if (viewing || config == null || result.SuggestedOffsets == null || result.MeanError is not double mean)
            return row;

        OffsetSuggestion suggestion = suggestOffset(mean);
        bool offerCalibration = Math.Abs(mean) > calibrate_error_ms;

        if (suggestion.Kind == OffsetSuggestionKind.None && !offerCalibration)
            return row;

        PillButton? apply = null;
        int lean = suggestion.Lean;

        row.Add(new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = suggestion switch
            {
                { Kind: OffsetSuggestionKind.Song, Plays: 1 } => $"You hit this song {describeLean(lean)} compared with your other songs.",
                { Kind: OffsetSuggestionKind.Song } => $"Over {suggestion.Plays} plays you hit this song {describeLean(lean)} compared with your other songs.",
                { Kind: OffsetSuggestionKind.Timing, Plays: > 1 } => $"Over your last {suggestion.Plays} plays you hit {describeLean(lean)}.",
                _ => $"You hit {describeLean(mean)}."
            },
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
            Color = palette.Text
        });

        if (suggestion.Kind == OffsetSuggestionKind.Timing)
        {
            row.Add(apply = new PillButton(palette.Control, palette.TextLight)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = $"Shift timing {Math.Abs(lean)} ms {(lean > 0 ? "later" : "earlier")} · every device",
                Action = () =>
                {
                    shiftTiming(lean);
                    apply!.Text = $"Audio {formatOffset(config.Get<int>(YuukiSetting.AudioOffset).Value)} · Visual {formatOffset(config.Get<int>(YuukiSetting.VisualOffset).Value)}";
                    apply.Enabled.Value = false;
                }
            });
        }
        else if (suggestion.Kind == OffsetSuggestionKind.Song && offsets != null && savedScore != null)
        {
            int beatmapId = savedScore.BeatmapId;
            string? difficulty = workingBeatmap?.Beatmap is { ID: var id } beatmap && id == beatmapId ? beatmap.DifficultyName : null;

            // A song hit late has its notes early against the music: lowering its offset moves them later.
            row.Add(apply = new PillButton(palette.Control, palette.TextLight)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = $"Fix this song: notes {Math.Abs(lean)} ms {(lean > 0 ? "later" : "earlier")} · "
                       + (string.IsNullOrEmpty(difficulty) ? "this difficulty only" : $"{difficulty} only"),
                Action = () =>
                {
                    offsets.SetSongOffset(beatmapId, offsets.GetSongOffset(beatmapId) - lean);
                    apply!.Text = $"Song offset {formatOffset(offsets.GetSongOffset(beatmapId))}";
                    apply.Enabled.Value = false;
                }
            });
        }

        if (offerCalibration)
        {
            row.Add(new PillButton(palette.Control, palette.TextLight)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = "Calibrate",
                Action = () =>
                {
                    // Calibrating sets both offsets afresh, and this run's figures were taken against the old ones.
                    if (apply != null)
                        apply.Enabled.Value = false;

                    calibrate();
                }
            });
        }

        return row;
    }

    private static string formatOffset(int ms) => ms > 0 ? $"+{ms} ms" : $"{ms} ms";

    private static string describeLean(double ms) => $"{Math.Abs(ms):0} ms {(ms > 0 ? "late" : "early")} on average";

    /// <summary>
    /// What this play says against the plays before it (<see cref="TimingHistory"/>). Without the score history, or
    /// for a play that wasn't saved, it's this play alone.
    /// </summary>
    private OffsetSuggestion suggestOffset(double mean)
    {
        // Without the devices' offsets there's only this play, taken as it was played.
        var history = new TimingHistory(device => offsets?.AudioOffsetFor(device) ?? (int)Math.Round(result.AudioOffset),
            id => offsets?.GetSongOffset(id) ?? (int)Math.Round(result.SongOffset));

        ScoreInfo play = savedScore ?? new ScoreInfo
        {
            MeanError = mean,
            AudioOffset = (int)Math.Round(result.AudioOffset),
            VisualOffset = (int)Math.Round(result.VisualOffset),
            SongOffset = (int)Math.Round(result.SongOffset),
            AudioDevice = offsets?.AudioDevice
        };

        IEnumerable<ScoreInfo> earlier = savedScore != null && scoreStore != null ? scoreStore.Scores : Array.Empty<ScoreInfo>();
        return history.Suggest(play, earlier);
    }

    /// <summary>
    /// Moves the judging <paramref name="shift"/> ms later on every device, the notes kept where they're seen.
    /// </summary>
    private void shiftTiming(int shift)
    {
        if (offsets != null)
        {
            offsets.ShiftTiming(shift);
            return;
        }

        // Without the devices' offsets, the settings' own, from the run's.
        (int audio, int visual) = result.OffsetsShiftedBy(shift);
        config!.Get<int>(YuukiSetting.AudioOffset).Value = audio;
        config.Get<int>(YuukiSetting.VisualOffset).Value = visual;
    }

    /// <summary>
    /// Hits per note type, e.g. "Tap 120/124 · Hold 30/32 · Release 27/32", for the types the chart
    /// used. A hold's release is its own judgement, so it's listed apart from the hold's press.
    /// </summary>
    private IReadOnlyList<string> describeNoteTypes()
    {
        var parts = result.Judgements
                          .GroupBy(j => (j.Note.Type, j.IsHoldTail))
                          .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.IsHoldTail)
                          .Select(g => $"{(g.Key.IsHoldTail ? "Release" : g.Key.Type.ToString())} {g.Count(j => j.Result.IsHit())}/{g.Count()}")
                          .ToList();

        if (result.HoldBreakCount > 0)
            parts.Add($"Hold breaks {result.HoldBreakCount}");

        return parts;
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
    /// Keeps the layout inside the canvas, which is smaller on a phone and shrinks as the UI scale grows.
    /// The song header and the ring share the left, giving up width before the stats column goes below
    /// its narrowest; the ring moves up under the header only as far as it has to, then shrinks to fit
    /// above the buttons. The stats column scrolls if it's taller than the room above the buttons.
    /// </summary>
    private void fitToCanvas()
    {
        float buttonsTop = DrawSize.Y + LeftBottomShearButton.DEFAULT_POSITION.Y - backButton.DrawSize.Y - 16;
        float leftRoom = DrawSize.X - 48 - 32 - details_min_width;

        float headerScale = Math.Clamp((leftRoom - header_position.X) / header_width, 0.5f, 1);
        setScale(header, headerScale);
        float headerBottom = header_position.Y + header.DrawSize.Y * headerScale;

        Vector2 ringSize = ringArea.DrawSize;
        float ringTop = Math.Clamp(buttonsTop - ringSize.Y, headerBottom + 12, ring_area_y);
        float ringScale = Math.Clamp(Math.Min((buttonsTop - ringTop) / ringSize.Y, (leftRoom - ring_area_x) / ringSize.X), 0.35f, 1);
        ringArea.Y = ringTop;
        setScale(ringArea, ringScale);

        float leftRight = Math.Max(header_position.X + header_width * headerScale, ring_area_x + ringSize.X * ringScale);
        detailsScroll.Width = Math.Clamp(DrawSize.X - 48 - leftRight - 32, details_min_width, details_max_width);
        detailsScroll.Height = Math.Max(240, buttonsTop - details_top);

        bool stacked = scoreSummary.DrawWidth < summary_one_line_width;

        if (stacked != summaryStacked)
        {
            summaryStacked = stacked;
            layOutSummary();
        }
    }

    private static void setScale(Drawable drawable, float scale)
    {
        if (Math.Abs(drawable.Scale.X - scale) > 0.001f)
            drawable.Scale = new Vector2(scale);
    }

    /// <summary>
    /// Score with max combo and accuracy beside it, or (in a narrow column) under it.
    /// </summary>
    private void layOutSummary()
    {
        scoreSummary.Height = summaryStacked ? summary_stacked_height : summary_height;
        scoreBlock.Anchor = scoreBlock.Origin = summaryStacked ? Anchor.TopLeft : Anchor.CentreLeft;
        statsBlock.Anchor = statsBlock.Origin = summaryStacked ? Anchor.BottomLeft : Anchor.CentreRight;
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

    private void openOffsetGuide()
    {
        if (screenStack == null)
            return;

        // Coming back, the results music starts again, as from calibrating, which the guide leads to.
        audioManager?.EndResultsMusic();
        screenStack.Push(new OffsetGuideScreen());
    }

    private void calibrate()
    {
        if (screenStack == null)
            return;

        // The clicks are all there is to hear: the song playing out, or the results music, would be heard over them.
        // Coming back, the results music starts again.
        audioManager?.EndResultsMusic();
        audioManager?.GameplayChannel?.Stop();

        screenStack.Push(new CalibrationScreen());
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
