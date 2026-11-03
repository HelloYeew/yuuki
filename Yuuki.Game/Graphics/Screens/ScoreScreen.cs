// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System;
using System.Threading.Tasks;
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
using Sakura.Framework.Logging;
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
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.Screens;

/// <summary>
/// The result screen: the run's accuracy filling a blossom ring, its rank landing in the middle with light and petals,
/// then the badge, how it compares with the best, the music, and Yuuki's voice (<see cref="RevealTimeline"/> times it).
/// A click, a tap or Space skips it, first to just before the rank lands, then to the end. Its second page, More info
/// (Tab, or the tabs along the bottom), has every graph (<see cref="MoreInfoPage"/>); a saved play's come from judging
/// its replay again, as its judgements aren't kept.
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

    [Resolved(canBeNull: true)]
    private BeatmapNoteStore? noteStore { get; set; }

    // The run: a saved play's gains its judgements once its replay is judged again.
    private GameplayResult result;

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

    private const float details_top = 48;

    /// <summary>
    /// Where More info's page starts, and how far in from the bottom left the tabs between the pages sit.
    /// </summary>
    private const float more_info_top = 34;

    private const float tabs_x = 270;
    private const float details_max_width = 1080;
    private const float details_min_width = 600;

    /// <summary>
    /// How far the song header shrinks, at most, to give the stats column the width for the score's statistics as rows
    /// (on a canvas a step smaller than a phone's at UI scale 1). Past that it stays as it is and they go under the score.
    /// </summary>
    private const float header_shrink_floor = 0.85f;

    // The panels' padding at each side.
    private const float panel_side_padding = 28;
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
    private Drawable scoreTitleRow = null!;
    private FlowContainer statsBlock = null!;
    private readonly List<Statistic> statistics = new List<Statistic>();
    private ScoreSummaryArrangement summaryArrangement = ScoreSummaryArrangement.OneLine;

    // How wide the summary's pieces are once the numbers have rolled up; measured again when a value's end changes.
    private SummaryStatisticWidths[]? statisticWidths;
    private float finalScoreWidth;

    /// <summary>
    /// How the score summary sits in its column, by the column's width.
    /// </summary>
    public ScoreSummaryArrangement SummaryArrangement => summaryArrangement;

    /// <summary>
    /// The score and its statistics in the summary, to see they don't overlap.
    /// </summary>
    public (Drawable Score, Drawable Statistics) SummaryBlocks => (scoreBlock, statsBlock);

    /// <summary>
    /// Whether the stats column has room for everything in it, without scrolling.
    /// </summary>
    public bool DetailsFit => details.DrawHeight <= detailsScroll.DrawHeight + 0.5f;

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
    private FullBloomRings bloomRings = null!;
    private FullBloomCutIn cutIn = null!;
    private RankLetter letter = null!;
    private SpriteText ringAccuracy = null!;
    private SpriteText ringReached = null!;
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
    private SpriteText unstableRateText = null!;
    private JudgementBreakdown judgementBreakdown = null!;
    private PlaysStrip? playsStrip;

    // The two pages: the summary (the ring and the numbers) and More info (every graph).
    private PageTabs tabs = null!;
    private YuukiScrollableContainer moreInfoScroll = null!;
    private MoreInfoPage moreInfo = null!;

    // The chart a saved play's replay is judged again on, the judging, and the best's to compare with.
    private PlayableBeatmap? chart;
    private ReplayJudge? judge;
    private ReplayJudge? bestJudge;

    /// <summary>
    /// Whether More info is showing, rather than the summary.
    /// </summary>
    public bool ShowingMoreInfo { get; private set; }

    /// <summary>
    /// The More info page.
    /// </summary>
    public MoreInfoPage MoreInfo => moreInfo;

    /// <summary>
    /// Whether a saved play's replay is still being judged again.
    /// </summary>
    public bool Judging => judge is { IsDone: false };

    private ResultsSoundPlayer sounds = null!;
    private ResultsVoice voice = null!;

    // The play the run is compared with: the best on its difficulty before it (or, looking at a saved one, the best but it).
    private ScoreInfo? best;

    // The clock's time at the reveal's start, moved on by a skip; and how far into it the last frame was.
    private double revealStart = double.NaN;
    private double lastRevealTime = double.NegativeInfinity;
    private int skips;
    private bool earnedRankShown;
    private bool bloomBadgeShown;
    private bool musicStarted;
    private bool voiceStarted;

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
        helperText = savedScore != null && viewing ? $"Played {DateUtility.FormatPlayed(savedScore.Date)}"
            : result.Failed ? FailedRun.Footer(result.Reached)
            : result.IsFullBloom ? "Full Bloom!"
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
                                    panel(createJudgementSection())
                                }
                            }
                        },
                        moreInfoScroll = new YuukiScrollableContainer
                        {
                            Position = new Vector2(48, more_info_top),
                            Alpha = 0,
                            Child = moreInfo = new MoreInfoPage(palette)
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
            // A Full Bloom's, across the screen.
            cutIn = new FullBloomCutIn(),
            petals = new PetalEffects(),
            // A flash of the rank's color over everything as it lands.
            tint = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Bleed = Edges.All,
                Alpha = 0,
                AlwaysPresent = true
            },
            edgeGlow = new EdgeGlow
            {
                Bleed = Edges.All
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
            tabs = new PageTabs(palette, "Summary", "More info")
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(tabs_x, LeftBottomShearButton.DEFAULT_POSITION.Y + 6),
                Alpha = 0,
                PagePicked = page => ShowPage(page == 1)
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

        unstableRateText.Text = formatUnstableRate();

        moreInfo.ShowSong(workingBeatmap?.Beatmap, workingBeatmap?.BeatmapSet, savedScore?.Date ?? DateTimeOffset.Now);
        moreInfo.SetRun(result);
        moreInfo.SetAdvice(createOffsetAdvice());
        moreInfo.WatchFrom = WatchReplay;

        if (result.IsAllPerfect)
            badge.Look(JudgementColors.ALL_PERFECT, "ALL PERFECT");

        badge.Shining = result.IsAllPerfect;

        // A run that ran out of health: its colors go grey, and it says how far it got.
        blossom.Greyed = result.Failed;
        showReach();

        // Replay first, then Retry at the far right when there is one (the usual next step after a play).
        float x = 0;

        // A saved score always offers it (locked without a replay); after a play, only once it's saved.
        if (viewing || canWatch)
        {
            actionButtons.Add(replayButton = new MenuShearButton
            {
                X = x,
                ButtonSize = action_button_size,
                // A run that failed is watched from a little before it ran out.
                Text = result.Failed && !viewing ? "Watch the fall" : "Replay",
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
                // After a run that failed, the way on is another try.
                BackgroundColor = result.Failed ? palette.Accent : palette.Surface,
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
            bloomRings = new FullBloomRings
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
                Y = result.Failed ? failed_accuracy_y : 70,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 34, FontWeight.ExtraBold),
                Color = palette.TextLight
            },
            // How far a run that failed got, under its accuracy.
            ringReached = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Y = failed_accuracy_y + 44,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Bold),
                Color = palette.TextLight.WithAlpha(0.75f)
            }
        }
    };

    /// <summary>
    /// A run that failed has its accuracy set higher in the ring, FAILED being shorter than a rank, so how far it got fits
    /// under it inside the fill.
    /// </summary>
    private const float failed_accuracy_y = 20;

    /// <summary>
    /// For a run that failed: how far it got, under its accuracy and in the footer, once that's known (a saved play's,
    /// once its replay is judged again). No badge: there's nothing to award.
    /// </summary>
    private void showReach()
    {
        if (!result.Failed || result.Reached is not double reached || result.FailedAt is not double failedAt)
            return;

        ringReached.Text = FailedRun.Ring(failedAt, result.ChartLength);
        helperText = viewing ? helperText : FailedRun.Footer(reached);
    }

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

        statistics.Add(new Statistic("Accuracy", accuracyText = statisticValue(),
            best == null ? null : accuracyDelta = deltaText((result.Accuracy - best.Accuracy) * 100, d => $"{(d >= 0 ? "+" : "−")}{Math.Abs(d).ToString("0.00", CultureInfo.InvariantCulture)}%", 16),
            () => formatAccuracy(result.Accuracy), sectionTitle));
        statistics.Add(new Statistic("Max combo", comboText = statisticValue(),
            best == null ? null : comboDelta = deltaText(result.MaxCombo - best.MaxCombo, d => d == 0 ? "±0" : $"{(d > 0 ? "+" : "−")}{Math.Abs(d)}", 16),
            () => formatCombo(result.MaxCombo), sectionTitle));
        // A saved play's comes in once its replay is judged again.
        statistics.Add(new Statistic("Unstable rate", unstableRateText = statisticValue(), null, formatUnstableRate, sectionTitle));

        return scoreSummary = new Container
        {
            RelativeSizeAxes = Axes.X,
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
                        scoreTitleRow = new FlowContainer
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
                    Spacing = new Vector2(ScoreSummaryLayout.COLUMN_SPACING, 0),
                    Children = statistics.Select(s => (Drawable)s.Flow).ToArray()
                }
            }
        };
    }

    private static string formatAccuracy(double accuracy) => $"{accuracy * 100:0.00}%";

    private string formatCombo(int combo) => $"{combo}x / {result.TotalNotes}";

    /// <summary>
    /// One of the score's statistics: its title over its value and change, or beside them in a row. The value is laid out by
    /// the text it ends on, so the arrangement doesn't change while it rolls up.
    /// </summary>
    private sealed class Statistic
    {
        public readonly FlowContainer Flow;
        public readonly SpriteText Title;
        public readonly SpriteText Value;
        public readonly SpriteText? Delta;

        private readonly Container titleSlot;
        private readonly Func<string> final;

        public Statistic(string title, SpriteText value, SpriteText? delta, Func<string> final, Func<string, SpriteText> titleText)
        {
            Title = titleText(title);
            Value = value;
            Delta = delta;
            this.final = final;

            titleSlot = new Container { AutoSizeAxes = Axes.Both, Child = Title };
            Flow = new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Children = delta == null ? new Drawable[] { titleSlot, value } : new Drawable[] { titleSlot, value, delta }
            };
            LayOut(ScoreSummaryArrangement.OneLine, 0);
        }

        /// <summary>
        /// How wide its pieces are, with the value as it ends and at its full size (on one line).
        /// </summary>
        public SummaryStatisticWidths Measure()
        {
            string shown = Value.Text;
            Value.Text = final();
            float value = Value.ContentSize.X * ScoreSummaryLayout.VALUE_SIZE / Value.Font.Size;
            Value.Text = shown;

            return new SummaryStatisticWidths(Title.ContentSize.X, value, Delta?.ContentSize.X);
        }

        /// <summary>
        /// Over each other in a column, or along a row whose titles' column is <paramref name="titleColumn"/> wide.
        /// </summary>
        public void LayOut(ScoreSummaryArrangement arrangement, float titleColumn)
        {
            bool row = arrangement == ScoreSummaryArrangement.Rows;

            Flow.Direction = row ? FlowDirection.Horizontal : FlowDirection.Vertical;
            Flow.Spacing = row ? new Vector2(ScoreSummaryLayout.ROW_SPACING, 0) : new Vector2(0, 2);

            titleSlot.AutoSizeAxes = row ? Axes.Y : Axes.Both;
            if (row)
                titleSlot.Width = titleColumn;
            titleSlot.Anchor = titleSlot.Origin = row ? Anchor.CentreLeft : Anchor.TopLeft;

            // Under its title it's set against the right; along a row it follows the titles' column.
            Value.Anchor = Value.Origin = row ? Anchor.CentreLeft : Anchor.TopRight;
            Value.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, row ? ScoreSummaryLayout.ROW_VALUE_SIZE : ScoreSummaryLayout.VALUE_SIZE, FontWeight.Bold);
        }
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

    /// <summary>
    /// The judgements, row by row, and under them where the run stands among the plays of its difficulty.
    /// </summary>
    private Drawable createJudgementSection()
    {
        var section = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 20),
            Children = new Drawable[]
            {
                judgementBreakdown = new JudgementBreakdown(result, palette)
                {
                    RelativeSizeAxes = Axes.X
                }
            }
        };

        if (createPlaysStrip() is PlaysStrip strip)
            section.Add(playsStrip = strip);

        return section;
    }

    /// <summary>
    /// Where the run stands among its difficulty's plays; none for a play that wasn't saved, or without the scores.
    /// </summary>
    private PlaysStrip? createPlaysStrip()
    {
        if (savedScore == null || scoreStore == null || workingBeatmap?.Beatmap is not Beatmap beatmap || beatmap.ID != savedScore.BeatmapId)
            return null;

        // A run that failed isn't ranked among them.
        if (result.Failed)
            return PlaysStrip.Failed(palette, savedScore, best, result.FailedAt, scoreStore.GetPlaysToday(beatmap));

        IReadOnlyList<ScoreInfo> ranking = scoreStore.GetRanking(beatmap);
        int place = ranking.ToList().IndexOf(savedScore) + 1;

        return new PlaysStrip(palette, savedScore, best, isNewBest, place > 0 ? place : ranking.Count + 1, scoreStore.GetPlayCount(beatmap));
    }

    private string formatUnstableRate() => result.UnstableRate is double unstableRate ? unstableRate.ToString("0", CultureInfo.InvariantCulture) : "—";

    /// <summary>
    /// What's offered for the offsets, in a strip of More info of its own: to centre this run's taps by moving the judging
    /// (<see cref="GameplayResult.SuggestedOffsets"/>), and for a large error, to calibrate. Where a lean past
    /// <see cref="lean_worth_mentioning"/> cost Criticals, says how many, and offers the guide to the offsets. A saved
    /// play's offsets are history: only the play just finished is worth calibrating from.
    /// </summary>
    private IEnumerable<Drawable> createOffsetAdvice()
    {
        if (viewing || config == null || result.MeanError is not double mean)
            yield break;

        var said = new List<string>();
        var buttons = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(8, 6)
        };

        if (result.SuggestedOffsets != null)
        {
            OffsetSuggestion suggestion = suggestOffset(mean);
            bool offerCalibration = Math.Abs(mean) > calibrate_error_ms;

            if (suggestion.Kind != OffsetSuggestionKind.None || offerCalibration)
            {
                said.Add(suggestion switch
                {
                    { Kind: OffsetSuggestionKind.Song, Plays: 1 } => $"You hit this song {describeLean(suggestion.Lean)} compared with your other songs.",
                    { Kind: OffsetSuggestionKind.Song } => $"Over {suggestion.Plays} plays you hit this song {describeLean(suggestion.Lean)} compared with your other songs.",
                    { Kind: OffsetSuggestionKind.Timing, Plays: > 1 } => $"Over your last {suggestion.Plays} plays you hit {describeLean(suggestion.Lean)}.",
                    _ => $"You hit {describeLean(mean)}."
                });

                addOffsetButtons(buttons, suggestion, offerCalibration);
            }
        }

        int lost = Math.Abs(mean) < lean_worth_mentioning ? 0 : result.CriticalsLostToLean;

        if (lost >= criticals_worth_mentioning)
        {
            said.Add($"On time on average, {lost} more would have been Critical. A lean this big every play is your device's delay, not you.");
            buttons.Add(new PillButton(palette.Control, palette.TextLight)
            {
                Text = "How offsets work",
                Action = openOffsetGuide
            });
        }

        // Nothing to do isn't worth a strip of the page: its card says so.
        if (said.Count == 0)
        {
            moreInfo.SayOfTiming("Your timing is centered.");
            yield break;
        }

        yield return new TextFlowContainer(word =>
        {
            word.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.SemiBold);
            word.Color = palette.Text;
        })
        {
            RelativeSizeAxes = Axes.X,
            Spacing = new Vector2(0, 2),
            Text = string.Join(" ", said)
        };

        if (buttons.Children.Count > 0)
            yield return buttons;
    }

    private void addOffsetButtons(FlowContainer buttons, OffsetSuggestion suggestion, bool offerCalibration)
    {
        PillButton? apply = null;
        int lean = suggestion.Lean;

        if (suggestion.Kind == OffsetSuggestionKind.Timing)
        {
            buttons.Add(apply = new PillButton(palette.Control, palette.TextLight)
            {
                Text = $"Shift timing {Math.Abs(lean)} ms {(lean > 0 ? "later" : "earlier")} · every device",
                Action = () =>
                {
                    shiftTiming(lean);
                    apply!.Text = $"Audio {formatOffset(config!.Get<int>(YuukiSetting.AudioOffset).Value)} · Visual {formatOffset(config.Get<int>(YuukiSetting.VisualOffset).Value)}";
                    apply.Enabled.Value = false;
                }
            });
        }
        else if (suggestion.Kind == OffsetSuggestionKind.Song && offsets != null && savedScore != null)
        {
            int beatmapId = savedScore.BeatmapId;
            string? difficulty = workingBeatmap?.Beatmap is { ID: var id } beatmap && id == beatmapId ? beatmap.DifficultyName : null;

            // A song hit late has its notes early against the music: lowering its offset moves them later.
            buttons.Add(apply = new PillButton(palette.Control, palette.TextLight)
            {
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
            buttons.Add(new PillButton(palette.Control, palette.TextLight)
            {
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
                Padding = new MarginPadding { Top = 16, Bottom = 18, Left = panel_side_padding, Right = panel_side_padding },
                Child = content
            }
        }
    };

    private SpriteText sectionTitle(string text) => new SpriteText
    {
        Text = text.ToUpperInvariant(),
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold),
        Color = ResultsInk.Secondary(palette)
    };

    // Its place and size are the Statistic's to set, by the arrangement.
    private SpriteText statisticValue() => new SpriteText { Color = palette.Text };

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
        tabs.FadeIn(500, Easing.OutQuint);
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

        // A run that failed: FAILED comes in quietly. Nothing else.
        if (t.Failed)
        {
            letter.LandFailed(at(t.Landing));
            earnedRankShown = true;
            return;
        }

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

        // A Full Bloom: the all perfect's badge goes under the cut-in, and stamps again as FULL BLOOM after it.
        if (t.FullBloom)
        {
            badge.Look(JudgementColors.ALL_PERFECT, "ALL PERFECT");
            badge.FadeOutAt(at(t.Bloom));
            bloomBadgeShown = false;

            bloomRings.Gather(at(t.Gather), RevealTimeline.GATHER_DURATION - 80);
            bloomRings.Bloom(at(t.Bloom));
            cutIn.Play(at(t.Bloom));
            blossom.BloomWave(at(t.Bloom));
        }

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

        // A run that failed: the ticks as it fills, and FAILED coming in; none of the rest.
        for (int i = 0; i < t.Crossings.Count && !t.Failed; i++)
        {
            (ScoreRank rank, double time) = t.Crossings[i];

            if (!between(time))
                continue;

            sounds.PlayCrossing(i);
            petals.Burst(headInPetals(out float outward), PetalBurst.Crossing(revealRandom, rank, outward), at(time));
        }

        // A trail of petals shed from the fill's leading edge, thicker while it's quick.
        for (double time = RevealTimeline.FILL_START + 30; time < t.FillEnd - 150 && !t.Failed; time += 36)
        {
            if (!between(time))
                continue;

            float progress = t.FillProgress(time);
            petals.Burst(headInPetals(out float outward), PetalBurst.Trail(revealRandom, outward, progress < 0.45f ? 2 : 1), at(time));
        }

        if (between(t.ChargeStart) && !t.Failed)
            sounds.PlayCharge();

        if (between(t.FillEnd) && result.MissCount > 0 && !t.Failed)
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

        if (between(t.Landing + 120) && !t.Failed && (result.IsAllPerfect || result.Rank >= ScoreRank.A))
            sounds.PlayPetals();

        if (result.IsFullCombo && between(t.Badge))
        {
            sounds.PlayBadge(result.IsAllPerfect);

            if (result.IsAllPerfect)
                petals.Burst(badgeInPetals(), PetalBurst.Sparkles(), at(t.Badge));
        }

        if (t.FullBloom)
        {
            if (between(t.Gather))
                sounds.PlayBloomGather();

            if (between(t.Bloom))
            {
                sounds.PlayFullBloom();
                petals.Burst(ringMiddleInPetals(), PetalBurst.Storm(), at(t.Bloom));
                petals.Fall.Blast(ringMiddleInPetals(), 0.9f);
            }

            if (between(t.BloomBadge))
                sounds.PlayBloomStamp();
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
        // FAILED: no hit, nothing thrown.
        if (Timeline.Failed)
        {
            sounds.PlayFail();
            return;
        }

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
        if (result.Failed)
            return;

        petals.Fall.Rate = result.IsAllPerfect ? 22 : result.Rank switch
        {
            ScoreRank.SS or ScoreRank.S => 14,
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

        // The fill and the numbers rolling up with it.
        float fill = t.FillAt(now);
        float roll = t.RollAt(now);
        float shown = t.FillProgress(now) >= 1 ? (float)result.Accuracy : t.Target * roll;
        float charge = t.ChargeAt(now);

        Color color = rankColorAt(now, shown);

        // A run that failed fills in its ranks' colors, dulled.
        if (t.Failed)
            color = color.Blend(wilted, 0.65f);

        // A Full Bloom: how far the ring has gathered itself (0–1), and how long since everything bloomed (−1 before).
        // Gathering drains the colors to white; the bloom brings them all back at once, the heroes' in turn.
        float gather = t.GatherAt(now);
        double sinceBloom = t.FullBloom && now >= t.Bloom ? now - t.Bloom : -1;

        if (gather > 0)
            color = color.Blend(Color.White, 0.55f * gather);
        else if (sinceBloom >= 0)
            color = HeroColors.Cycle(sinceBloom * 1.4, white: 0);

        blossom.Fill = fill;
        blossom.FillColor = color;
        blossom.Charge = charge;

        float pulse = 0;
        foreach ((_, double time) in t.Crossings)
        {
            if (now >= time && !t.Failed)
                pulse = Math.Max(pulse, MathF.Exp((float)(-(now - time) / 130)));
        }

        blossom.Pulse = pulse;

        bool moving = t.FillProgress(now) > 0 && t.FillProgress(now) < 1;
        blossom.HeadAlpha = moving ? 1 : Math.Max(0, 1 - clamp01((now - t.FillEnd) / 300));

        float glow = charge * 0.9f;
        if (sinceFirst >= 0)
            glow = Math.Max(glow, 0.9f * MathF.Exp((float)(-Math.Min(sinceFirst, sinceLanding >= 0 ? sinceLanding : sinceFirst) / 200)));
        if (gather > 0)
            glow = Math.Max(glow, 0.95f * gather);
        if (sinceBloom >= 0)
            glow = Math.Max(glow, 0.95f * MathF.Exp((float)(-sinceBloom / 500)));

        // FAILED brings only a faint, slow glow.
        if (t.Failed)
            glow = sinceLanding >= 0 ? 0.18f * MathF.Exp((float)(-sinceLanding / 220)) : 0;

        blossom.Glow = glow;

        // The bands: an all perfect's pulse with Yuuna's pink; a broken S's greying.
        for (int i = 0; i < blossom.BandColors.Length; i++)
        {
            Color band = HeroColors.ForRank(bandRank(i)).Main;

            if (sinceBloom >= 0)
                band = HeroColors.Cycle(sinceBloom * 1.4, i * 60, 0.1f);
            else if (result.IsAllPerfect && now >= t.Badge)
                band = band.Blend(HeroColors.YUUNA.Main, 0.5f + 0.5f * MathF.Sin((float)((now - t.Badge) / 260 + i * 1.2)));

            if (gather > 0)
                band = band.Blend(Color.White, 0.6f * gather);
            else if (bandRank(i) == ScoreRank.S && sinceCrack >= 0)
                band = band.Blend(wilted, clamp01(sinceCrack / 120));

            blossom.BandColors[i] = band;
        }

        // The ring swells as it charges, and gives as a rank hits.
        float scale = 1;
        double sinceHit = t.RankLost && now < t.Landing ? sinceFirst : sinceLanding;
        // (A run that failed has nothing charging or hitting it.)
        if (!t.Failed && sinceHit < 0 && sinceFirst < 0)
            scale = 1 + 0.03f * charge;
        else if (!t.Failed && sinceHit >= 0)
            scale = 1 + 0.03f * Math.Max(0, 1 - (float)(sinceHit / 70)) - 0.045f * MathF.Sin(clamp01(sinceHit / 220) * MathF.PI);

        // Gathering, it draws in on itself, trembling; blooming, it bursts out and rings.
        if (gather > 0)
            scale *= 1 - 0.05f * gather * gather + 0.004f * gather * MathF.Sin((float)(now / 9));
        else if (sinceBloom >= 0)
            scale *= 1 + 0.12f * MathF.Exp((float)(-sinceBloom / 140)) * MathF.Cos((float)(sinceBloom / 70));

        ringBlock.Scale = new Vector2(scale);

        letter.Rainbow = sinceBloom >= 0;

        // The badge stamps again as FULL BLOOM once the cut-in has gone: bigger, in the heroes' rainbow.
        if (t.FullBloom && !bloomBadgeShown && now >= t.BloomBadge)
        {
            bloomBadgeShown = true;
            badge.LookRainbow("FULL BLOOM");
            badge.Stamp(at(t.BloomBadge), 2);
        }

        // The earned rank takes over from a broken S just before it lands.
        if (t.RankLost && !earnedRankShown && now >= t.Landing - RankLetter.FALL)
        {
            earnedRankShown = true;
            letter.Land(result.Rank, JudgementColors.For(result.Rank), at(t.Landing));
            waves.Land(JudgementColors.For(result.Rank), at(t.Landing));
        }

        Color landingColor = t.Failed ? wilted : JudgementColors.For(t.RankLost && now < t.Landing - RankLetter.FALL ? ScoreRank.S : result.Rank);

        if (t.Failed)
            renderFailedLight(now);
        else
            renderLight(now, color, landingColor, charge, sinceCrack, gather, sinceBloom);

        renderNumbers(now, roll, shown);
        renderVoice(now);
    }

    /// <summary>
    /// The light round the ring and the screen: the rays behind the rank, the edges' glow, the flash of the rank's color,
    /// the dimming as it charges, the color draining while an S breaks, and the shake of a hit.
    /// </summary>
    private void renderLight(double now, Color fillColor, Color landingColor, float charge, double sinceCrack, float gather, double sinceBloom)
    {
        RevealTimeline t = Timeline;
        double sinceLanding = now - t.Landing;
        double sinceFirst = now - t.FirstLanding;
        bool sBreaking = t.RankLost && now >= t.Crack && now < t.Landing - RankLetter.FALL;

        // Rays behind the rank once it's landed, stronger the better it is.
        float strength = result.IsAllPerfect ? 0.69f : result.Rank switch
        {
            ScoreRank.SS or ScoreRank.S => 0.53f,
            ScoreRank.A => 0.27f,
            ScoreRank.B => 0.13f,
            _ => 0
        };

        rays.Peak = sinceLanding >= 0 ? strength * outCubic(clamp01(sinceLanding / 450)) : 0;
        rays.Scale = new Vector2(0.85f + 0.15f * outCubic(clamp01(sinceLanding / 600)));

        // A Full Bloom's: broad, in every hero's color, flaring with the bloom.
        rays.HeroColored = sinceBloom >= 0;

        if (sinceBloom >= 0)
        {
            rays.SpokeWidth = LightRays.WIDE;
            rays.Spokes = 18;
            rays.Peak = Math.Min(1, 0.85f + 0.6f * MathF.Exp((float)(-sinceBloom / 400)));
            rays.Scale = new Vector2(1 + 0.25f * outCubic(clamp01(sinceBloom / 500)));
        }

        // The edges: faint as the fill runs, brighter at each rank crossed, as it charges, and as a rank lands, then
        // steady once the music's in.
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
                led = Math.Max(led, 0.3f);
        }

        if (gather > 0)
        {
            ledColor = Color.White;
            led = Math.Max(led, 0.2f + 0.75f * gather * (0.8f + 0.2f * MathF.Sin((float)(now / 26))));
        }
        else if (sinceBloom >= 0)
        {
            ledColor = HeroColors.Cycle(sinceBloom * 1.4);
            led = Math.Max(led, 0.3f + 0.9f * MathF.Exp((float)(-sinceBloom / 500)));
        }

        edgeGlow.Intensity = led;
        edgeGlow.GlowColor = ledColor;

        // A flash of the rank's color across the screen as it hits.
        double sinceHit = t.RankLost && now < t.Landing ? sinceFirst : sinceLanding;
        tint.Color = landingColor;
        tint.Alpha = sinceHit >= 0 ? 0.16f * MathF.Exp((float)(-sinceHit / 170)) : 0;

        // The bloom whites the screen out for a moment.
        if (sinceBloom >= 0 && sinceBloom < 900)
        {
            tint.Color = Color.White;
            tint.Alpha = 0.92f * MathF.Exp((float)(-sinceBloom / 150));
        }

        // Everything but the ring dims as it charges, and comes back after the hit; and again as it gathers itself.
        float dimming = sinceFirst < 0 ? 0.32f * charge : 0.32f * (1 - clamp01(sinceFirst / 420));
        if (gather > 0)
            dimming = Math.Max(dimming, 0.5f * gather);
        else if (sinceBloom >= 0)
            dimming = Math.Max(dimming, 0.5f * (1 - clamp01(sinceBloom / 220)));
        focusDimmer.Alpha = dimming;

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

        // Trembling as it gathers itself; a hard shake as it blooms.
        if (gather > 0)
            shake += new Vector2(2 * gather * MathF.Sin((float)(now / 7)), 1.5f * gather * MathF.Cos((float)(now / 9)));
        else if (sinceBloom >= 0 && sinceBloom < 500)
        {
            float d = (float)sinceBloom;
            shake += new Vector2(14 * MathF.Exp(-d / 110) * MathF.Sin(d / 10), 10 * MathF.Exp(-d / 110) * MathF.Cos(d / 12));
        }

        world.Position = shake;

        // The petals already falling are drawn in while it gathers itself; a storm falls after it blooms, then a
        // steady fall.
        if (gather > 0)
            petals.Fall.Attract(ringMiddleInPetals(), 0.0008f, Clock.ElapsedFrameTime);

        if (sinceBloom >= 0)
        {
            petals.Fall.Rate = sinceBloom < 2600 ? 60 : 30;
            petals.Fall.Limit = 340;
        }
    }

    /// <summary>
    /// A run that failed has no light: no rays, no glow at the edges, no flash, no dimming and no shake. Only a little of
    /// the screen's color goes as FAILED comes in.
    /// </summary>
    private void renderFailedLight(double now)
    {
        rays.Peak = 0;
        edgeGlow.Intensity = 0;
        tint.Alpha = 0;
        focusDimmer.Alpha = 0;
        colorDrain.GrayscaleStrength = 0.35f * clamp01((now - Timeline.Landing) / 600);
        world.Position = Vector2.Zero;
    }

    /// <summary>
    /// The numbers rolling up with the fill, the score's pop and glint as the rank lands, and the comparison with the best.
    /// </summary>
    private void renderNumbers(double now, float roll, float shown)
    {
        RevealTimeline t = Timeline;

        judgementBreakdown.Progress = roll;
        scoreText.Text = formatScore(result.Score * roll);
        comboText.Text = formatCombo((int)MathF.Round(result.MaxCombo * roll));
        accuracyText.Text = ringAccuracy.Text = $"{shown * 100:0.00}%";

        double sinceLanding = now - t.Landing;
        scoreBlock.Scale = new Vector2(sinceLanding >= 0 && !t.Failed ? 1 + 0.05f * MathF.Exp((float)(-sinceLanding / 130)) : 1);

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

        playsStrip?.Compare(clamp01(sinceBest / 300));
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

    // The reveal is the summary's: on More info a click is the graphs'.
    public override bool OnClick(MouseButtonEvent e) => (!ShowingMoreInfo && Skip()) || base.OnClick(e);

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.IsRepeat)
            return base.OnKeyDown(e);

        if (e.Key == Key.Tab)
        {
            ShowPage(!ShowingMoreInfo);
            return true;
        }

        if ((e.Key == Key.Space || e.Key == Key.Enter) && !ShowingMoreInfo && Skip())
            return true;

        return base.OnKeyDown(e);
    }

    /// <summary>
    /// Shows More info, or the summary again. The reveal plays out first: its light and petals would go over the graphs.
    /// </summary>
    public void ShowPage(bool showMoreInfo)
    {
        if (showMoreInfo == ShowingMoreInfo)
            return;

        ShowingMoreInfo = showMoreInfo;
        tabs.Current = showMoreInfo ? 1 : 0;

        if (showMoreInfo)
        {
            while (Skip())
            {
            }

            moreInfo.Open();
        }

        float summary = showMoreInfo ? 0 : 1;
        header.FadeTo(summary, 300, Easing.OutQuint);
        ringArea.FadeTo(summary, 300, Easing.OutQuint);
        detailsScroll.FadeTo(summary, 300, Easing.OutQuint);
        moreInfoScroll.FadeTo(1 - summary, 300, Easing.OutQuint);

        // Falling still, but fainter over the graphs.
        petals.FadeTo(showMoreInfo ? 0.4f : 1, 300, Easing.OutQuint);
    }

    /// <summary>
    /// The ring's color at a moment: each rank's as the fill sweeps into it, a broken S fading into the rank earned.
    /// </summary>
    private Color rankColorAt(double now, float shownAccuracy)
    {
        RevealTimeline t = Timeline;

        if (t.RankLost && now >= t.Crack + 160)
            return JudgementColors.For(ScoreRank.S).Blend(JudgementColors.For(result.Rank), clamp01((now - t.Crack - 160) / color_blend));

        // SS is no band of the ring's: the fill runs into S's color, which becomes SS's as it lands.
        if (result.Rank == ScoreRank.SS && now >= t.Landing)
            return JudgementColors.For(ScoreRank.S).Blend(JudgementColors.For(ScoreRank.SS), clamp01((now - t.Landing) / color_blend));

        ScoreRank rank = ScoreRankExtensions.FromAccuracy(shownAccuracy);
        if (rank == ScoreRank.SS)
            rank = ScoreRank.S;

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
    /// above the buttons. The stats column scrolls if it's taller than the room above the buttons, once
    /// its judgement rows have closed up.
    /// </summary>
    private void fitToCanvas()
    {
        measureSummary();

        float buttonsTop = DrawSize.Y + LeftBottomShearButton.DEFAULT_POSITION.Y - backButton.DrawSize.Y - 16;
        float narrowest = narrowestColumn();
        float leftRoom = DrawSize.X - 48 - 32 - narrowest;

        float headerScale = Math.Clamp((leftRoom - header_position.X) / header_width, 0.5f, 1);
        setScale(header, headerScale);
        float headerBottom = header_position.Y + header.DrawSize.Y * headerScale;

        Vector2 ringSize = ringArea.DrawSize;
        float ringTop = Math.Clamp(buttonsTop - ringSize.Y, headerBottom + 12, ring_area_y);
        float ringScale = Math.Clamp(Math.Min((buttonsTop - ringTop) / ringSize.Y, (leftRoom - ring_area_x) / ringSize.X), 0.35f, 1);
        ringArea.Y = ringTop;
        setScale(ringArea, ringScale);

        float leftRight = Math.Max(header_position.X + header_width * headerScale, ring_area_x + ringSize.X * ringScale);
        detailsScroll.Width = Math.Clamp(DrawSize.X - 48 - leftRight - 32, narrowest, details_max_width);
        detailsScroll.Height = Math.Max(240, buttonsTop - details_top);

        moreInfoScroll.Width = Math.Max(0, DrawSize.X - 96);
        moreInfoScroll.Height = Math.Max(240, buttonsTop - more_info_top);
        moreInfo.ViewHeight = moreInfoScroll.Height;

        fitSummary();

        // Judged by its height with the rows as they are, so closing them up doesn't open them again.
        float fullHeight = details.DrawHeight - judgementBreakdown.DrawHeight + judgementBreakdown.FullHeight;
        judgementBreakdown.Compact = fullHeight > detailsScroll.Height + 0.5f;
    }

    /// <summary>
    /// How narrow the stats column may go: as wide as the score's statistics need to go beside it as rows, where the header
    /// (and the ring, if it's the wider) only has to give up a little for that, to <see cref="header_shrink_floor"/>.
    /// Otherwise its usual narrowest, with them under the score.
    /// </summary>
    private float narrowestColumn()
    {
        float rows = ScoreSummaryLayout.RowsNeed(summaryScoreWidth, statisticWidths!) + 2 * panel_side_padding + 1;

        if (rows <= details_min_width)
            return details_min_width;

        float leftRoom = DrawSize.X - 48 - 32 - rows;
        return leftRoom >= header_position.X + header_width * header_shrink_floor ? rows : details_min_width;
    }

    /// <summary>
    /// How wide the summary's pieces are once the numbers have rolled up, measured once (and again when a value's end
    /// changes) by showing each its last text.
    /// </summary>
    private void measureSummary()
    {
        if (statisticWidths != null)
            return;

        statisticWidths = statistics.Select(s => s.Measure()).ToArray();
        string shown = scoreText.Text;
        scoreText.Text = formatScore(result.Score);
        finalScoreWidth = scoreText.ContentSize.X;
        scoreText.Text = shown;
    }

    // The score's title row or its number, whichever is wider.
    private float summaryScoreWidth => Math.Max(scoreTitleRow.DrawWidth, finalScoreWidth);

    /// <summary>
    /// The score summary in the arrangement that fits its column, chosen by the numbers it ends on, and as tall as what's
    /// in it.
    /// </summary>
    private void fitSummary()
    {
        ScoreSummaryArrangement arrangement = ScoreSummaryLayout.Choose(scoreSummary.DrawWidth, summaryScoreWidth, statisticWidths!);

        if (arrangement != summaryArrangement)
        {
            summaryArrangement = arrangement;
            layOutSummary();
        }

        float height = ScoreSummaryLayout.Height(summaryArrangement, scoreBlock.DrawHeight, statsBlock.DrawHeight);

        if (Math.Abs(scoreSummary.Height - height) > 0.5f)
            scoreSummary.Height = height;
    }

    private static void setScale(Drawable drawable, float scale)
    {
        if (Math.Abs(drawable.Scale.X - scale) > 0.001f)
            drawable.Scale = new Vector2(scale);
    }

    /// <summary>
    /// The score with its statistics beside it in a line or as rows, or (in the narrowest column) in a line under it.
    /// </summary>
    private void layOutSummary()
    {
        bool stacked = summaryArrangement == ScoreSummaryArrangement.Stacked;
        bool rows = summaryArrangement == ScoreSummaryArrangement.Rows;

        scoreBlock.Anchor = scoreBlock.Origin = stacked ? Anchor.TopLeft : Anchor.CentreLeft;
        statsBlock.Anchor = statsBlock.Origin = stacked ? Anchor.BottomLeft : Anchor.CentreRight;
        statsBlock.Direction = rows ? FlowDirection.Vertical : FlowDirection.Horizontal;
        statsBlock.Spacing = rows
            ? new Vector2(0, ScoreSummaryLayout.ROWS_SPACING)
            : new Vector2(stacked ? ScoreSummaryLayout.STACKED_COLUMN_SPACING : ScoreSummaryLayout.COLUMN_SPACING, 0);

        float titleColumn = ScoreSummaryLayout.TitleColumnWidth(statisticWidths ?? Array.Empty<SummaryStatisticWidths>());

        foreach (Statistic statistic in statistics)
            statistic.LayOut(summaryArrangement, titleColumn);
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
        moreInfoScroll.FadeOut(300, Easing.OutQuint);
        tabs.FadeOut(300, Easing.OutQuint);
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
    /// Opens the play's replay (what the Replay button, and R on a saved score, do): from the start, or for a run that
    /// just failed ("Watch the fall"), from <see cref="FALL_LEAD"/> before it ran out. Nothing without one.
    /// </summary>
    public void WatchReplay()
    {
        if (canWatch)
            replayButton?.Flash(Color.White);

        WatchReplay(result.FailedAt is double failedAt && !viewing ? Math.Max(0, failedAt - FALL_LEAD) : 0);
    }

    /// <summary>
    /// How long before a failed run ran out its replay starts, from the results ("Watch the fall"): enough to see it go.
    /// </summary>
    public const double FALL_LEAD = 8000;

    /// <summary>
    /// Opens the play's replay at <paramref name="from"/>, in ms of the song (More info's "watch from"). Nothing without one.
    /// </summary>
    public void WatchReplay(double from)
    {
        if (!canWatch || savedScore == null || replay == null || screenStack == null)
            return;

        // The replay brings the song; the results music would play over it.
        audioManager?.EndResultsMusic();

        screenStack.Push(new ReplayScreen(savedScore, replay)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            StartAt = from
        });
    }

    #region Judging again

    protected override void LoadComplete()
    {
        base.LoadComplete();
        startJudging();
    }

    /// <summary>
    /// A saved play keeps its totals and its replay, not its judgements: they're worked out again here, from the
    /// replay's inputs on the chart, and More info and the rows fill in once they're in. Where they can't be (no replay,
    /// the chart changed or isn't here), More info says why. The best's replay is judged too, to compare with. All of it
    /// on threads from the pool: the files read here, and the judging (<see cref="ReplayJudge"/>).
    /// </summary>
    private void startJudging()
    {
        bool judgeOwn = result.Judgements.Count == 0;

        if (judgeOwn && replay is not { Inputs.Count: > 0 })
            moreInfo.ShowUnavailable("This play was saved without a replay, so only its totals are kept.");

        if (savedScore == null || workingBeatmap?.Beatmap is not Beatmap beatmap || beatmap.ID != savedScore.BeatmapId)
        {
            if (judgeOwn && replay is { Inputs.Count: > 0 })
                moreInfo.ShowUnavailable("This play's chart isn't here, so its replay can't be judged again.");

            return;
        }

        ScoreInfo? compared = best;
        var scheduler = Scheduler;

        Task.Run(() => (Chart: ReplayJudge.ChartFor(beatmap, noteStore), BestReplay: compared == null ? null : scoreStore?.GetReplay(compared)))
            .ContinueWith(read =>
            {
                if (read.IsFaulted)
                    Logger.Error("Failed to read a chart to judge a replay on.", read.Exception!);

                scheduler.Add(() => startJudgingOn(read.IsFaulted ? null : read.Result.Chart, read.IsFaulted ? null : read.Result.BestReplay));
            });
    }

    private void startJudgingOn(PlayableBeatmap? loadedChart, ReplayData? bestReplay)
    {
        if (exiting)
            return;

        chart = loadedChart;

        if (result.Judgements.Count == 0 && replay is { Inputs.Count: > 0 })
        {
            if (chart == null)
                moreInfo.ShowUnavailable("This play's chart isn't here, so its replay can't be judged again.");
            else if (!replay.CanPlayOn(chart))
                moreInfo.ShowUnavailable("The beatmap's notes have changed since this play, so its replay can't be judged again.");
            else
            {
                ReplayJudge judging = judge = judgeFor(replay, savedScore != null && BloomlessJudgement.AppliesTo(savedScore));
                judging.Completed += () =>
                {
                    if (judging.Faulted)
                    {
                        moreInfo.ShowUnavailable("This play's replay couldn't be judged again.");
                        return;
                    }

                    result = result.WithJudgements(judging.Judgements, judging.HoldBreakTimes, chart.LaneCount, chart.LastNoteTime, judging.FailedAt);
                    showJudgements();
                    showReach();
                };

                Add(judging);
                moreInfo.JudgeFrom(judging);
            }
        }

        if (best == null || chart == null || bestReplay == null || !bestReplay.CanPlayOn(chart))
            return;

        ReplayJudge judgingBest = bestJudge = judgeFor(bestReplay, BloomlessJudgement.AppliesTo(best));
        ScoreInfo comparedWith = best;
        judgingBest.Completed += () =>
        {
            if (judgingBest.Faulted)
                return;

            GameplayResult bestResult = GameplayResult.FromScore(comparedWith, bestReplay).WithJudgements(judgingBest.Judgements, judgingBest.HoldBreakTimes, chart.LaneCount, chart.LastNoteTime);
            var scheduler = Scheduler;

            Task.Run(() => new PlayAnalysis(bestResult)).ContinueWith(worked =>
            {
                if (!worked.IsFaulted)
                    scheduler.Add(() => moreInfo.SetBest(worked.Result));
            });
        };

        Add(judgingBest);
    }

    // A play saved without Blooms (before there were any) is judged without them, so it agrees with its counts.
    private ReplayJudge judgeFor(ReplayData played, bool bloomless) => new ReplayJudge(chart!, played)
    {
        Preempt = GameplayScreen.ScrollPreempt(config, chart!),
        StartTime = -GameplayScreen.LeadInFor(chart!),
        CountsBloomAsCritical = bloomless
    };

    // The judgements are in: everything that waited for them.
    private void showJudgements()
    {
        moreInfo.SetRun(result);
        judgementBreakdown.ShowTiming(result);
        unstableRateText.Text = formatUnstableRate();

        // The unstable rate it ends on has changed: the summary is measured again for it.
        statisticWidths = null;
    }

    #endregion

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
    /// <remarks>Slanted about its middle, so it's centered on its words, and its corners lean out past them evenly: its
    /// margins keep them off what's beside it.</remarks>
    private Drawable newBestTag() => new Container
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        AutoSizeAxes = Axes.Both,
        Margin = new MarginPadding { Left = 10, Right = 10 },
        Alpha = 0,
        AlwaysPresent = true,
        Children = new Drawable[]
        {
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
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
                Margin = new MarginPadding { Left = 14, Right = 14, Top = 3, Bottom = 4 },
                Text = "NEW BEST",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.ExtraBold),
                Color = Color.White
            }
        }
    };
}
