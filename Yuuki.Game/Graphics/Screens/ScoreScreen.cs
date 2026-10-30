// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System;
using Sakura.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
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
/// The result screen: the run's accuracy filling a blossom ring, its rank landing in the middle with light and petals,
/// then the badge, how it compares with the best, the music, and Yuuki's voice (<see cref="RevealTimeline"/> times it).
/// A click, a tap or Space skips it, first to just before the rank lands, then to the end.
/// </summary>
public partial class ScoreScreen : YuukiScreen, IHaveScreenAction
{
    /// <summary>
    /// The ring's block, as the prototype's: the letter and the flash are sized for it.
    /// </summary>
    private const float ring_block_size = 440;

    /// <summary>
    /// The blossom ring drawn into the block: its units to the block's.
    /// </summary>
    private const float ring_scale = ring_block_size / BlossomRing.RING_SIZE;

    // Set once the screen starts leaving, so the music and the voice know not to start.
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

    /// <summary>
    /// How long after a fill's sweep through a rank its color takes over the ring, in ms.
    /// </summary>
    private const double color_blend = 300;

    private static readonly Color focus_dim = ColorExtensions.FromHex("#0B0F1C");
    private static readonly Color better = ColorExtensions.FromHex("#2F7A49");
    private static readonly Color wilted = ColorExtensions.FromHex("#8E8A99");

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
    /// Whether the rank the run earned has landed (the end of the reveal's main beat).
    /// </summary>
    public bool RankRevealed { get; private set; }

    /// <summary>
    /// When everything in the reveal happens.
    /// </summary>
    public RevealTimeline Timeline { get; }

    /// <summary>
    /// How far into the reveal it is, in ms (skips move it on).
    /// </summary>
    public double RevealTime => double.IsNaN(revealStart) ? 0 : Clock.CurrentTime - revealStart;

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
    private readonly int attempt;

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

    // Everything that shakes when a rank lands, and whose color drains while an S breaks.
    private BufferedContainer colorDrain = null!;
    private Container world = null!;
    private Box focusDimmer = null!;
    private PetalEffects petals = null!;
    private Box tint = null!;
    private EdgeGlow edgeGlow = null!;

    // The ring's block: the light behind it, the ring, what a landing sends out, the rank, the accuracy and the badge.
    private Container ringBlock = null!;
    private LightRays rays = null!;
    private BlossomRing blossom = null!;
    private LandingWaves waves = null!;
    private RankLetter letter = null!;
    private SpriteText ringAccuracy = null!;
    private ResultsBadge badge = null!;

    private Container scorePanel = null!;
    private ShineSweep scoreShine = null!;
    private SpriteText scoreText = null!;
    private SpriteText comboText = null!;
    private SpriteText accuracyText = null!;
    private Drawable? newBestChip;
    private SpriteText? scoreDelta;
    private SpriteText? accuracyDelta;
    private SpriteText? comboDelta;
    private JudgementBreakdown judgementBreakdown = null!;
    private HitTimingGraph timingGraph = null!;
    private JudgementTimeline timeline = null!;

    private ResultsSoundPlayer sounds = null!;
    private ResultsVoice voice = null!;

    // The play the run is compared with: the best on its difficulty before it (or, looking at a saved one, the best but it).
    private ScoreInfo? best;

    // The clock's time at the reveal's start, moved on by a skip; and how far into it the last frame was.
    private double revealStart = double.NaN;
    private double lastRevealTime = double.NegativeInfinity;
    private int skips;
    private bool earnedRankShown;
    private bool musicStarted;
    private bool voiceStarted;
    private bool graphsShown;

    // The reveal's own randomness (the trail of petals), the same every time.
    private readonly Random revealRandom = new Random(77);

    private string helperText = "";

    /// <summary>
    /// Shows a saved score again: no results music, voice, retry or offset suggestion, and a Replay button (locked when
    /// the score has no replay). The breakdown is full only when a replay was saved.
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
    /// <param name="attempt">Which try in a row on the chart it was (1 for the first), for Yuuki to say so.</param>
    public ScoreScreen(GameplayResult result, bool isNewBest = false, Action? onRetry = null, ScoreInfo? score = null, ReplayData? replay = null, int attempt = 1)
    {
        this.result = result;
        this.isNewBest = isNewBest;
        this.onRetry = onRetry;
        this.attempt = attempt;

        savedScore = score;
        this.replay = replay;

        Timeline = RevealTimeline.For(result);

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
        helperText = savedScore != null && viewing ? string.Create(CultureInfo.InvariantCulture, $"Played {savedScore.Date.LocalDateTime:d MMM yyyy, HH:mm}")
            : result.IsAllPerfect ? "All Perfect!"
            : result.IsFullCombo ? "Full Combo!"
            : "Nice play!";
        ScreenHelperText.Value = helperText;

        palette = Palette;
        best = findBest();

        AddRange(new Drawable[]
        {
            sounds = new ResultsSoundPlayer(),
            voice = new ResultsVoice(),
            // Keeps the panels readable over bright backgrounds.
            dim = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Bleed = Edges.All,
                Color = palette.Backdrop,
                Alpha = 0
            },
            colorDrain = new BufferedContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = world = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        header = createHeader(),
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
                                    scorePanel = panel(createScoreSummary()),
                                    panel(createJudgementSection()),
                                    panel(createTimingSection())
                                }
                            }
                        },
                        // As the ring charges, everything but it dims.
                        focusDimmer = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Bleed = Edges.All,
                            Color = focus_dim,
                            Alpha = 0,
                            AlwaysPresent = true
                        },
                        ringArea = new Container
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Position = new Vector2(-ring_block_size, ring_area_y),
                            Size = new Vector2(ring_block_size + 120, ring_block_size + 110),
                            Alpha = 0,
                            Children = new Drawable[]
                            {
                                ringBlock = createRingBlock(),
                                badge = new ResultsBadge
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.Centre,
                                    Y = 10 + ring_block_size + 48
                                }
                            }
                        }
                    }
                }
            },
            petals = new PetalEffects(),
            // A flash of the rank's color over everything as it lands.
            tint = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Bleed = Edges.All,
                Alpha = 0,
                AlwaysPresent = true
            },
            // The screen's edges glowing in the rank's color, as an arcade cabinet's lights.
            edgeGlow = new EdgeGlow(),
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

        scorePanel.Add(scoreShine = new ShineSweep());

        if (result.IsAllPerfect)
            badge.Look(JudgementColors.ALL_PERFECT, "ALL PERFECT");

        badge.Shining = result.IsAllPerfect;

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
    }

    /// <summary>
    /// The best play on this run's difficulty other than the run itself, to compare it with; none without the scores.
    /// </summary>
    private ScoreInfo? findBest()
    {
        if (scoreStore == null || workingBeatmap?.Beatmap is not Beatmap beatmap || (savedScore != null && savedScore.BeatmapId != beatmap.ID))
            return null;

        return scoreStore.GetRanking(beatmap).FirstOrDefault(s => !ReferenceEquals(s, savedScore));
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
    /// The ring and everything round it: the light behind, the blossom ring, a landing's flash and waves, the rank and the
    /// accuracy under it.
    /// </summary>
    private Container createRingBlock() => new Container
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.Centre,
        Y = 10 + ring_block_size / 2,
        Size = new Vector2(ring_block_size),
        Children = new Drawable[]
        {
            rays = new LightRays
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(760),
                Spokes = 20,
                SpokeWidth = LightRays.THIN,
                Color = SetlistColors.TierFill(result.IsAllPerfect ? JudgementColors.ALL_PERFECT : JudgementColors.For(result.Rank)),
                // Dark until the rank lands (its brightness, not its alpha: shown by alpha alone it wouldn't draw).
                Peak = 0
            },
            blossom = new BlossomRing
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(ring_scale)
            },
            waves = new LandingWaves
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            },
            letter = new RankLetter
            {
                Y = -26
            },
            // Light, on the dark bubble (the panels' text is dark, on their light faces).
            ringAccuracy = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Y = 70,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 34, FontWeight.ExtraBold),
                Color = palette.TextLight
            }
        }
    };

    /// <summary>
    /// Score, max combo and accuracy — the numbers that roll up with the ring — and, against the best on this difficulty,
    /// how each changed.
    /// </summary>
    private Drawable createScoreSummary()
    {
        var titleRow = new List<Drawable> { sectionTitle("Score") };

        if (isNewBest)
            titleRow.Add(newBestChip = newBestTag());

        if (best != null)
            titleRow.Add(scoreDelta = deltaText(result.Score - best.Score, d => $"{(d >= 0 ? "+" : "−")}{Math.Abs(d).ToString("N0", CultureInfo.InvariantCulture)}", 22));

        return scoreSummary = new Container
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
                            Children = titleRow
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
                        statistic("Max combo", comboText = statisticValue(),
                            best == null ? null : comboDelta = deltaText(result.MaxCombo - best.MaxCombo, d => d == 0 ? "±0" : $"{(d > 0 ? "+" : "−")}{Math.Abs(d)}", 16)),
                        statistic("Accuracy", accuracyText = statisticValue(),
                            best == null ? null : accuracyDelta = deltaText((result.Accuracy - best.Accuracy) * 100, d => $"{(d >= 0 ? "+" : "−")}{Math.Abs(d).ToString("0.00", CultureInfo.InvariantCulture)}%", 16))
                    }
                }
            }
        };
    }

    /// <summary>
    /// A change against the best: green up, red down. It comes in when the run is compared (<see cref="RevealTimeline.Best"/>).
    /// </summary>
    private static SpriteText deltaText(double change, Func<double, string> format, int size) => new SpriteText
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Text = format(change),
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, size, FontWeight.ExtraBold),
        Color = change >= 0 ? better : JudgementColors.MISS,
        Alpha = 0,
        AlwaysPresent = true
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

    // A statistic's title over its value, and under that, if given, how it changed against the best.
    private Drawable statistic(string title, SpriteText value, SpriteText? delta = null) => new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 2),
        Children = delta == null ? new Drawable[] { sectionTitle(title), value } : new Drawable[] { sectionTitle(title), value, delta }
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

        // Laid out first: sliding along X holds Y where it is when the slide starts, so the ring has to be at its own
        // height already (on a short canvas it's well above the default).
        fitToCanvas();

        this.FadeIn(400, Easing.OutQuint);
        dim.FadeTo(0.35f, 400, Easing.OutQuint);
        header.AnimateIn();
        ringArea.MoveToX(ring_area_x, 600, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        detailsScroll.MoveToX(-48, 600, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        backButton.MoveTo(LeftBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        actionButtons.MoveTo(action_buttons_position, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);

        revealStart = Clock.CurrentTime;
        lastRevealTime = double.NegativeInfinity;
        scheduleReveal();
        render(0);
    }

    /// <summary>
    /// Tells everything that plays itself out from a time when to: the rank, the badge, the flowers and the glints. Again
    /// after a skip, as the reveal's times have moved.
    /// </summary>
    private void scheduleReveal()
    {
        RevealTimeline t = Timeline;

        if (t.RankLost)
        {
            letter.Land(ScoreRank.S, JudgementColors.For(ScoreRank.S), at(t.FirstLanding));
            letter.Crack(at(t.Crack));
            waves.Land(JudgementColors.For(ScoreRank.S), at(t.FirstLanding));
            waves.Crack(at(t.Crack));
            blossom.WiltRank(ScoreRank.S, at(t.Crack));
            earnedRankShown = false;
        }
        else
        {
            letter.Land(result.Rank, JudgementColors.For(result.Rank), at(t.Landing));
            waves.Land(JudgementColors.For(result.Rank), at(t.Landing));
            earnedRankShown = true;
        }

        if (result.IsFullCombo)
            badge.Stamp(at(t.Badge));

        if (result.IsAllPerfect)
            blossom.OpenAllPerfect(at(t.Badge));

        scoreShine.Sweep(at(t.Landing + 60));
    }

    // A time in the reveal, on the clock.
    private double at(double revealTime) => revealStart + revealTime;

    public override void Update()
    {
        // Before the frame's transforms and matrices, so the layout shows the frame it's set.
        fitToCanvas();

        base.Update();

        if (double.IsNaN(revealStart))
            return;

        double now = RevealTime;

        fire(lastRevealTime, now);
        lastRevealTime = now;

        render(now);
    }

    /// <summary>
    /// What happens once, at its moment in the reveal: the sounds, the bursts of petals, the music and the voice. A skip
    /// passes over what it jumps.
    /// </summary>
    private void fire(double from, double to)
    {
        RevealTimeline t = Timeline;
        bool between(double time) => time > from && time <= to;

        foreach (double tick in t.Ticks.Where(between))
            sounds.PlayTick(t.FillProgress(tick));

        for (int i = 0; i < t.Crossings.Count; i++)
        {
            (ScoreRank rank, double time) = t.Crossings[i];

            if (!between(time))
                continue;

            sounds.PlayCrossing(i);
            petals.Burst(headInPetals(out float outward), PetalBurst.Crossing(revealRandom, rank, outward), at(time));
        }

        // A trail of petals shed from the fill's leading edge, thicker while it's quick.
        for (double time = RevealTimeline.FILL_START + 30; time < t.FillEnd - 150; time += 36)
        {
            if (!between(time))
                continue;

            float progress = t.FillProgress(time);
            petals.Burst(headInPetals(out float outward), PetalBurst.Trail(revealRandom, outward, progress < 0.45f ? 2 : 1), at(time));
        }

        if (between(t.ChargeStart))
            sounds.PlayCharge();

        if (between(t.FillEnd) && result.MissCount > 0)
            sounds.PlayMiss();

        if (t.RankLost)
        {
            if (between(t.FirstLanding))
                sounds.PlayLanding(ScoreRank.S);

            if (between(t.Crack))
            {
                sounds.PlayRankLost();
                petals.Burst(ringMiddleInPetals() - new Vector2(0, 30), PetalBurst.Shards(), at(t.Crack + 160));
                petals.Burst(ringMiddleInPetals() - new Vector2(0, 60), PetalBurst.Wilted(), at(t.Crack + 300));
            }
        }

        // What holds from the landing on, even when a skip jumps it; what's heard and thrown, only when it plays.
        if (!RankRevealed && to >= t.Landing)
        {
            RankRevealed = true;
            startFalling();
        }

        if (between(t.Landing))
            land();

        if (between(t.Landing + 120) && (result.IsAllPerfect || result.Rank >= ScoreRank.A))
            sounds.PlayPetals();

        if (result.IsFullCombo && between(t.Badge))
        {
            sounds.PlayBadge(result.IsAllPerfect);

            if (result.IsAllPerfect)
                petals.Burst(badgeInPetals(), PetalBurst.Sparkles(), at(t.Badge));
        }

        if (between(t.Best) && isNewBest)
            sounds.PlayNewBest();

        // A saved score is looked at, not celebrated: no music (the song select track plays on), and no voice. They come
        // in once their time is reached, skipped to or not.
        if (viewing || exiting || !IsCurrentScreen)
            return;

        // The music waits for the play's song, still going when the chart ended before it, to play out.
        if (!musicStarted && to >= t.Music)
        {
            musicStarted = true;
            audioManager?.BeginResultsMusic();
        }

        if (!voiceStarted && to >= t.Voice)
        {
            voiceStarted = true;

            if (config?.Get<bool>(YuukiSetting.ResultsVoice).Value != false)
                voice.Say(ResultsVoice.LinesFor(voiceContext(), new Random()));
        }
    }

    /// <summary>
    /// The rank the run earned lands: its hit, its petals, the bubble wobbing and a gust through the branch.
    /// </summary>
    private void land()
    {
        sounds.PlayLanding(result.Rank);

        Vector2 middle = ringMiddleInPetals();
        petals.Burst(middle, PetalBurst.Landing(result.Rank, result.IsFullCombo, result.IsAllPerfect, JudgementColors.For(result.Rank)), at(Timeline.Landing));

        blossom.Backdrop.Wob(at(Timeline.Landing), 1.6f);
        blossom.Branch.Gust(at(Timeline.Landing));
    }

    /// <summary>
    /// From the landing on, petals fall across the screen, more the better the rank.
    /// </summary>
    private void startFalling()
    {
        petals.Fall.Rate = result.IsAllPerfect ? 22 : result.Rank switch
        {
            ScoreRank.S => 14,
            ScoreRank.A => 6,
            ScoreRank.B => 2.5,
            _ => 0
        };
        petals.Fall.Colors = result.IsAllPerfect
            ? null
            : new List<Color>(PetalBurst.PINKS) { HeroColors.KAORUKO.Main, HeroColors.KAORUKO.Light, HeroColors.KAORUKO.Deep };
    }

    private VoiceContext voiceContext()
    {
        bool firstPlay = savedScore != null && workingBeatmap?.Beatmap is Beatmap beatmap && scoreStore?.GetPlayCount(beatmap) == 1;
        return VoiceContext.For(result, isNewBest, firstPlay, attempt, DateTime.Now);
    }

    /// <summary>
    /// Everything that follows the reveal's time from frame to frame.
    /// </summary>
    private void render(double now)
    {
        RevealTimeline t = Timeline;
        double sinceLanding = now - t.Landing;
        double sinceFirst = now - t.FirstLanding;
        double sinceCrack = t.RankLost ? now - t.Crack : double.NegativeInfinity;

        // The music's beat, once it's in: 1 on the beat, falling away through it.
        double? beatProgress = now >= t.Music ? audioManager?.ResultsMusicBeat : null;
        float beat = beatProgress is double b ? MathF.Exp((float)(-b * 5)) : 0;

        // The fill and the numbers rolling up with it.
        float fill = t.FillAt(now);
        float roll = t.RollAt(now);
        float shown = t.FillProgress(now) >= 1 ? (float)result.Accuracy : t.Target * roll;
        float charge = t.ChargeAt(now);

        Color color = rankColorAt(now, shown);

        blossom.Fill = fill;
        blossom.FillColor = color;
        blossom.Charge = charge;
        blossom.Beat = beat;

        float pulse = 0;
        foreach ((_, double time) in t.Crossings)
        {
            if (now >= time)
                pulse = Math.Max(pulse, MathF.Exp((float)(-(now - time) / 130)));
        }

        blossom.Pulse = pulse;

        bool moving = t.FillProgress(now) > 0 && t.FillProgress(now) < 1;
        blossom.HeadAlpha = moving ? 1 : Math.Max(0, 1 - clamp01((now - t.FillEnd) / 300));

        float glow = charge * 0.9f;
        glow = Math.Max(glow, 0.28f * beat);
        if (sinceFirst >= 0)
            glow = Math.Max(glow, 0.9f * MathF.Exp((float)(-Math.Min(sinceFirst, sinceLanding >= 0 ? sinceLanding : sinceFirst) / 200)));
        blossom.Glow = glow;

        // The bands: an all perfect's pulse with Yuuna's pink; a broken S's greying.
        for (int i = 0; i < blossom.BandColors.Length; i++)
        {
            Color band = HeroColors.ForRank(bandRank(i)).Main;

            if (result.IsAllPerfect && now >= t.Badge)
                band = band.Blend(HeroColors.YUUNA.Main, 0.5f + 0.5f * MathF.Sin((float)((now - t.Badge) / 260 + i * 1.2)));
            else if (bandRank(i) == ScoreRank.S && sinceCrack >= 0)
                band = band.Blend(wilted, clamp01(sinceCrack / 120));

            blossom.BandColors[i] = band;
        }

        // The ring swells as it charges, gives as a rank hits, and beats with the music.
        float scale = 1;
        double sinceHit = t.RankLost && now < t.Landing ? sinceFirst : sinceLanding;
        if (sinceHit < 0 && sinceFirst < 0)
            scale = 1 + 0.03f * charge;
        else if (sinceHit >= 0)
            scale = 1 + 0.03f * Math.Max(0, 1 - (float)(sinceHit / 70)) - 0.045f * MathF.Sin(clamp01(sinceHit / 220) * MathF.PI);
        if (beatProgress is double bp)
            scale *= 1 + 0.014f * MathF.Exp((float)(-bp * 6));
        ringBlock.Scale = new Vector2(scale);

        // The earned rank takes over from a broken S just before it lands.
        if (t.RankLost && !earnedRankShown && now >= t.Landing - RankLetter.FALL)
        {
            earnedRankShown = true;
            letter.Land(result.Rank, JudgementColors.For(result.Rank), at(t.Landing));
            waves.Land(JudgementColors.For(result.Rank), at(t.Landing));
        }

        Color landingColor = JudgementColors.For(t.RankLost && now < t.Landing - RankLetter.FALL ? ScoreRank.S : result.Rank);

        renderLight(now, color, landingColor, beat, charge, sinceCrack);
        renderNumbers(now, roll, shown);
        renderVoice(now);
    }

    /// <summary>
    /// The light round the ring and the screen: the rays behind the rank, the edges' glow, the flash of the rank's color,
    /// the dimming as it charges, the color draining while an S breaks, and the shake of a hit.
    /// </summary>
    private void renderLight(double now, Color fillColor, Color landingColor, float beat, float charge, double sinceCrack)
    {
        RevealTimeline t = Timeline;
        double sinceLanding = now - t.Landing;
        double sinceFirst = now - t.FirstLanding;
        bool sBreaking = t.RankLost && now >= t.Crack && now < t.Landing - RankLetter.FALL;

        // Rays behind the rank once it's landed, stronger the better it is.
        float strength = result.IsAllPerfect ? 0.8f : result.Rank switch
        {
            ScoreRank.S => 0.62f,
            ScoreRank.A => 0.32f,
            ScoreRank.B => 0.15f,
            _ => 0
        };

        rays.Peak = sinceLanding >= 0 ? strength * outCubic(clamp01(sinceLanding / 450)) * (0.82f + 0.18f * beat) : 0;
        rays.Scale = new Vector2(0.85f + 0.15f * outCubic(clamp01(sinceLanding / 600)));

        // The edges: faint as the fill runs, brighter at each rank crossed, as it charges, and as a rank lands, then
        // with the beat.
        float led = 0;
        Color ledColor = fillColor;

        if (now >= RevealTimeline.FILL_START)
        {
            led = 0.14f;

            foreach ((_, double time) in t.Crossings)
            {
                if (now >= time)
                    led = Math.Max(led, 0.16f + 0.5f * MathF.Exp((float)(-(now - time) / 220)));
            }

            led = Math.Max(led, 0.75f * charge);

            if (sinceFirst >= 0)
            {
                ledColor = landingColor;
                led = Math.Max(led, 0.3f + 0.8f * MathF.Exp((float)(-(sinceLanding >= 0 ? sinceLanding : sinceFirst) / 320)));
            }

            if (sBreaking)
            {
                ledColor = JudgementColors.MISS;
                led = Math.Max(led, 0.2f + 0.7f * MathF.Exp((float)(-sinceCrack / 260)));
            }

            if (now >= t.Music)
                led = Math.Max(led, 0.24f + 0.36f * beat);
        }

        edgeGlow.Intensity = led;
        edgeGlow.GlowColor = ledColor;

        // A flash of the rank's color across the screen as it hits.
        double sinceHit = t.RankLost && now < t.Landing ? sinceFirst : sinceLanding;
        tint.Color = landingColor;
        tint.Alpha = sinceHit >= 0 ? 0.16f * MathF.Exp((float)(-sinceHit / 170)) : 0;

        // Everything but the ring dims as it charges, and comes back after the hit.
        focusDimmer.Alpha = sinceFirst < 0 ? 0.32f * charge : 0.32f * (1 - clamp01(sinceFirst / 420));

        // The pain: color drains from the screen while the S breaks, and comes back with the rank earned.
        float drain = 0;
        if (t.RankLost && now >= t.Crack)
            drain = now < t.Landing - RankLetter.FALL ? 0.9f * clamp01((now - t.Crack) / 80) : 0.9f * (1 - clamp01((now - (t.Landing - RankLetter.FALL)) / 340));
        colorDrain.GrayscaleStrength = drain;

        // The shake of a hit: less for the S that's about to break; and a short, sharp one as it breaks.
        Vector2 shake = Vector2.Zero;

        if (sinceHit >= 0 && sinceHit < 360)
        {
            float amount = t.RankLost && now < t.Landing ? 4 : 7;
            float d = (float)sinceHit;
            shake = new Vector2(amount * MathF.Exp(-d / 80) * MathF.Sin(d / 11), amount * 0.7f * MathF.Exp(-d / 80) * MathF.Cos(d / 13));
        }

        if (sinceCrack >= 0 && sinceCrack < 300)
            shake.X += 5 * MathF.Exp((float)(-sinceCrack / 90)) * MathF.Sin((float)(sinceCrack / 9));

        world.Position = shake;

        // The petals falling sway with the beat, one way then the other.
        petals.Fall.Gust = now >= t.Music && audioManager?.ResultsMusicBeat is double phase
            ? 0.0004f * MathF.Exp((float)(-phase * 5)) * (Math.Floor(now / YuukiAudioManager.RESULTS_MUSIC_BEAT) % 2 == 0 ? 1 : -1)
            : 0;
    }

    /// <summary>
    /// The numbers rolling up with the fill, the score's pop and glint as the rank lands, and the comparison with the best.
    /// </summary>
    private void renderNumbers(double now, float roll, float shown)
    {
        RevealTimeline t = Timeline;

        judgementBreakdown.Progress = roll;
        scoreText.Text = formatScore(result.Score * roll);
        comboText.Text = $"{(int)MathF.Round(result.MaxCombo * roll)}x / {result.TotalNotes}";
        accuracyText.Text = ringAccuracy.Text = $"{shown * 100:0.00}%";

        if (!graphsShown && now >= RevealTimeline.FILL_START + RevealTimeline.FILL_DURATION * 0.4)
        {
            graphsShown = true;
            timingGraph.AnimateIn();
            timeline.AnimateIn();
        }

        double sinceLanding = now - t.Landing;
        scoreBlock.Scale = new Vector2(sinceLanding >= 0 ? 1 + 0.05f * MathF.Exp((float)(-sinceLanding / 130)) : 1);

        // The comparison with the best comes in; a new best stamps on.
        double sinceBest = now - t.Best;
        float comparing = outCubic(clamp01(sinceBest / 320));

        foreach (SpriteText? delta in new[] { scoreDelta, accuracyDelta, comboDelta })
        {
            if (delta == null)
                continue;

            delta.Alpha = comparing;
            delta.Y = 14 * (1 - comparing);
        }

        if (newBestChip != null)
        {
            newBestChip.Alpha = sinceBest < 0 ? 0 : clamp01(sinceBest / 120);
            newBestChip.Scale = new Vector2(sinceBest < 0 ? 1 : 1 + 0.4f * (1 - outCubic(clamp01(sinceBest / 300))));
        }
    }

    /// <summary>
    /// What the footer says: how to skip while the reveal plays, Yuuki's words while she speaks (whose voice they are is
    /// in the settings' About), and the run's own line after.
    /// </summary>
    private void renderVoice(double now)
    {
        if (audioManager != null)
            audioManager.ResultsMusicLowered = voice.IsSpeaking && !exiting;

        string tap = RuntimeInfo.IsMobile ? "Tap" : "Click";
        string line = voice.Subtitle is string words ? words
            : now < Timeline.FirstLanding - RevealTimeline.SKIP_LEAD ? $"{tap} to skip to the rank"
            : now < Timeline.End - RevealTimeline.SKIP_TAIL ? $"{tap} to skip to the end"
            : helperText;

        if (ScreenHelperText.Value != line)
            ScreenHelperText.Value = line;
    }

    /// <summary>
    /// Skips the reveal on (<see cref="RevealTimeline.SkipFrom"/>): what's passed over isn't played, but everything shows
    /// as it would be by then. False once there's nothing left to skip.
    /// </summary>
    public bool Skip()
    {
        if (double.IsNaN(revealStart))
            return false;

        double now = RevealTime;
        double to = Timeline.SkipFrom(now);

        if (to <= now)
            return false;

        // The petals of the fill's trail would still be flying; the hit throws its own.
        if (skips++ == 0)
            petals.ClearAll();

        revealStart -= to - now;
        lastRevealTime = to;
        scheduleReveal();
        render(to);
        return true;
    }

    public override bool OnClick(MouseButtonEvent e) => Skip() || base.OnClick(e);

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!e.IsRepeat && (e.Key == Key.Space || e.Key == Key.Enter) && Skip())
            return true;

        return base.OnKeyDown(e);
    }

    /// <summary>
    /// The ring's color at a moment: each rank's as the fill sweeps into it, a broken S fading into the rank earned.
    /// </summary>
    private Color rankColorAt(double now, float shownAccuracy)
    {
        RevealTimeline t = Timeline;

        if (t.RankLost && now >= t.Crack + 160)
            return JudgementColors.For(ScoreRank.S).Blend(JudgementColors.For(result.Rank), clamp01((now - t.Crack - 160) / color_blend));

        ScoreRank rank = ScoreRankExtensions.FromAccuracy(shownAccuracy);
        int index = Array.IndexOf(ScoreRankExtensions.THRESHOLD_RANKS, rank);

        if (index < 0)
            return JudgementColors.For(ScoreRank.D);

        ScoreRank below = index == 0 ? ScoreRank.D : ScoreRankExtensions.THRESHOLD_RANKS[index - 1];
        double crossed = t.Crossings.FirstOrDefault(c => c.Rank == rank).Time;

        return JudgementColors.For(below).Blend(JudgementColors.For(rank), clamp01((now - crossed) / color_blend));
    }

    // The ranks of the ring's bands, worst first (as BlossomRing's BandColors).
    private static ScoreRank bandRank(int band) => band == 0 ? ScoreRank.D : ScoreRankExtensions.THRESHOLD_RANKS[band - 1];

    // Where things are, in the petals' units.
    private Vector2 ringMiddleInPetals() => petals.ToLocalSpace(middleOf(blossom.ScreenSpaceDrawQuad));

    private Vector2 badgeInPetals() => petals.ToLocalSpace(middleOf(badge.ScreenSpaceDrawQuad));

    private static Vector2 middleOf(Quad quad) => (quad.TopLeft + quad.BottomRight) / 2;

    private Vector2 headInPetals(out float outward)
    {
        Vector2 head = blossom.HeadPosition;
        outward = MathF.Atan2(head.Y, head.X);
        return petals.ToLocalSpace(blossom.ToScreenSpace(new Vector2(BlossomRing.RING_SIZE / 2) + head));
    }

    private static float clamp01(double value) => (float)Math.Clamp(value, 0, 1);

    private static float outCubic(float x) => (float)EasingFunctions.Apply(Easing.OutCubic, x);

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

    #endregion

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        // Leaving before the music or the voice was due: stop them from starting under song select.
        exiting = true;

        voice.Stop();

        if (audioManager != null)
            audioManager.ResultsMusicLowered = false;

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

        // She'd talk over the replay.
        voice.Stop();
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);
        this.FadeIn(300, Easing.OutQuint);

        // Back from a replay after a play: the results music picks up again (or starts, if its time came meanwhile).
        if (!viewing && RevealTime >= Timeline.Music)
        {
            musicStarted = true;
            audioManager?.BeginResultsMusic();
        }
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
    /// A small gold "NEW BEST" tag beside the score title, sheared as the badge. It stamps on when the run is compared.
    /// </summary>
    private Drawable newBestTag() => new Container
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        AutoSizeAxes = Axes.Both,
        Alpha = 0,
        AlwaysPresent = true,
        Children = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 4,
                Shear = UserInterfaceConstant.Shear,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = JudgementColors.CRITICAL
                }
            },
            new SpriteText
            {
                Margin = new MarginPadding { Left = 12, Right = 12, Top = 3, Bottom = 4 },
                Text = "NEW BEST",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.ExtraBold),
                Color = Color.White
            }
        }
    };
}
