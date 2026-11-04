// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The results' More info page, one question a card: where the timing drifted through the song, where health nearly ran
/// out, how the presses spread, which lane is off, which kind of note, and whether the offsets want changing. A play just
/// finished has its judgements; a saved one has them once its replay is judged again (<see cref="ReplayJudge"/>), and
/// until then its graphs draw in behind the judging, and the cards that need every judgement wait under a shimmer.
/// </summary>
public partial class MoreInfoPage : FlowContainer
{
    private const float card_gap = 16;

    private const float card_margin = 14;
    private const float row_margin = 8;
    private const float health_card_height = 186;
    private const float row_height = 212;

    /// <summary>
    /// What the timing card has round its graph: its title above, and a little below.
    /// </summary>
    private const float timing_card_top = 58;

    private const float timing_card_bottom = 6;

    /// <summary>
    /// Where the zoom's controls are on the timing graph: just in from the top right corner of its plot.
    /// </summary>
    private const float zoom_controls_top = 40;

    private const float zoom_controls_inset = 20;

    /// <summary>
    /// The rest of the page but what's offered, whose heights are what they need: the timing card has what they leave of the screen.
    /// </summary>
    private const float rest_of_page = MoreInfoHeader.HEIGHT + card_margin + card_margin + health_card_height + row_margin + row_height;

    /// <summary>
    /// The timing card where the page scrolls whatever it does: easy to read, as there's nothing to gain from less.
    /// </summary>
    private const float timing_card_min = TimingScatter.HEIGHT + timing_card_top + timing_card_bottom;

    /// <summary>
    /// The least it gives up to, to have all the page on the screen rather than some of it off the bottom.
    /// </summary>
    private const float timing_card_least = 320;

    /// <summary>
    /// As tall as it fills a 4:3 screen: a window taller than that isn't made better by a graph taller than it is wide.
    /// </summary>
    private const float timing_card_max = 800;

    /// <summary>
    /// How long the graphs take to draw in, as the page opens, in ms.
    /// </summary>
    public const double DRAW_IN = 1100;

    /// <summary>
    /// How often the graphs are drawn again from what's been judged so far, while a replay is judged again, in ms.
    /// </summary>
    private const double judging_redraw = 120;

    private static readonly HitResult[] results = { HitResult.Bloom, HitResult.Critical, HitResult.Break, HitResult.Hit, HitResult.Miss };

    private readonly YuukiColorPalette palette;

    private readonly MoreInfoHeader header;
    private readonly ResultsCard timingCard, healthCard, spreadCard, lanesCard, typesCard, figuresCard;
    private readonly TimingScatter scatter;
    private readonly HealthComboGraph health;
    private readonly TimingSpread spread;
    private readonly LaneColumns lanes;
    private readonly NoteTypeBars types;
    private readonly TimingFigures figures;
    private readonly Container adviceStrip;
    private readonly FlowContainer advice;
    private readonly FlowContainer cards;
    private readonly SpriteText unavailable;
    private readonly HardestRow hardest;
    private readonly ResultsChip compareChip;
    private readonly ResultsChip zoomOutChip, zoomInChip, moveBackChip, moveOnChip;
    private readonly SpriteText zoomText;
    private readonly Drawable zoomControls;
    private readonly SongTimeView view = new SongTimeView();
    private int zoomShown = -1;
    private bool waitingShown;
    private readonly FlowContainer laneChips;
    private readonly Dictionary<HitResult, ResultsChip> resultChips = new Dictionary<HitResult, ResultsChip>();

    private GameplayResult? result;
    private PlayAnalysis? analysis;
    private ReplayJudge? judge;
    private int judgementsDrawn;
    private bool analysingSoFar;
    private double drawnAt = double.NegativeInfinity;
    private double openedAt = double.NegativeInfinity;

    /// <summary>
    /// Called with where in the song to open the replay at.
    /// </summary>
    public Action<double>? WatchFrom
    {
        get => scatter.WatchFrom;
        set
        {
            scatter.WatchFrom = value;
            health.WatchFrom = value;
        }
    }

    /// <summary>
    /// What the run is being drawn from; null before there's anything.
    /// </summary>
    public PlayAnalysis? Analysis => analysis;

    /// <summary>
    /// Whether it's waiting for the replay to be judged again.
    /// </summary>
    public bool Judging => judge is { IsDone: false };

    /// <summary>
    /// The timing graph (for tests).
    /// </summary>
    public TimingScatter Scatter => scatter;

    public HealthComboGraph Health => health;

    /// <summary>
    /// How tall the room it's shown in is. The presses crowd round on time, so the timing graph takes what the rest of the
    /// page leaves, and the page just fills it; where that's too little for it to be read, the page scrolls.
    /// </summary>
    public float ViewHeight { get; set; }

    public MoreInfoPage(YuukiColorPalette palette)
    {
        this.palette = palette;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FlowDirection.Vertical;

        Children = new Drawable[]
        {
            header = new MoreInfoHeader(palette),
            timingCard = new ResultsCard("Hit timing over the song", "Where did I drift?", palette)
            {
                RelativeSizeAxes = Axes.X,
                Height = timing_card_min,
                Margin = new MarginPadding { Top = card_margin }
            },
            healthCard = new ResultsCard("Health and combo", "Where did I nearly fail?", palette)
            {
                RelativeSizeAxes = Axes.X,
                Height = health_card_height,
                Margin = new MarginPadding { Top = card_margin }
            },
            cards = new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                Height = row_height,
                Margin = new MarginPadding { Top = row_margin },
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(card_gap, 0),
                Children = new Drawable[]
                {
                    spreadCard = new ResultsCard("Timing spread", "Early or late overall?", palette, true) { RelativeSizeAxes = Axes.Y },
                    lanesCard = new ResultsCard("Lanes", "Which finger is off?", palette, true) { RelativeSizeAxes = Axes.Y },
                    typesCard = new ResultsCard("Note types", "Holds or swipes?", palette, true) { RelativeSizeAxes = Axes.Y },
                    figuresCard = new ResultsCard("Timing", "Should I change my offset?", palette, true) { RelativeSizeAxes = Axes.Y }
                }
            }
        };

        timingCard.Body.Padding = new MarginPadding { Top = timing_card_top, Left = 24, Right = 24, Bottom = timing_card_bottom };
        timingCard.Body.AddRange(new Drawable[]
        {
            scatter = new TimingScatter(palette) { RelativeSizeAxes = Axes.X },
            unavailable = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.SemiBold),
                Color = ResultsInk.Secondary(palette),
                Alpha = 0
            }
        });

        healthCard.Body.Padding = new MarginPadding { Top = 52, Left = 24, Right = 24, Bottom = 6 };
        healthCard.Body.Add(health = new HealthComboGraph(palette) { RelativeSizeAxes = Axes.X });

        scatter.View = health.View = view;

        // In the plot's corner, as a map has them: along the top with the other controls, they run into its title on a phone.
        zoomControls = controlGroup(IconUsage.ZoomIn, true,
            zoomOutChip = zoomChip("−", view.ZoomOut),
            zoomText = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Margin = new MarginPadding { Left = 5, Right = 5 },
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 14, FontWeight.Bold),
                Color = palette.Text
            },
            zoomInChip = zoomChip("+", view.ZoomIn),
            moveBackChip = zoomChip("◀", () => view.MoveByView(-0.5)),
            moveOnChip = zoomChip("▶", () => view.MoveByView(0.5)));

        zoomControls.Anchor = Anchor.TopRight;
        zoomControls.Origin = Anchor.TopRight;
        zoomControls.Position = new Vector2(-zoom_controls_inset, zoom_controls_top);
        timingCard.Body.Add(zoomControls);

        spreadCard.Body.Add(spread = new TimingSpread(palette));
        lanesCard.Body.Add(lanes = new LaneColumns(palette) { Margin = new MarginPadding { Top = 4 } });
        typesCard.Body.Add(types = new NoteTypeBars(palette) { Margin = new MarginPadding { Top = 4 } });
        figuresCard.Body.Add(figures = new TimingFigures(palette) { Margin = new MarginPadding { Top = 4 } });

        // Across the page rather than under the figures: on a card's width, a line and a few buttons run to half its height
        // again, and the three cards beside it would stand half empty. Not in the page until there's something to say.
        adviceStrip = new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Margin = new MarginPadding { Top = row_margin },
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
                advice = new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Padding = new MarginPadding { Left = 24, Right = 24, Top = 12, Bottom = 12 }
                }
            }
        };

        // The timing graph's: a chip a judgement to show or hide it, the lanes to pick, and the best to compare with.
        foreach (HitResult shown in results)
        {
            var chip = new ResultsChip(shown.ToString().ToLowerInvariant(), palette, JudgementColors.For(shown))
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                On = true,
                Toggled = on => scatter.Show(shown, on)
            };

            resultChips[shown] = chip;
            timingCard.Controls.Add(chip);
        }

        timingCard.Controls.Add(controlGroup(IconUsage.ViewColumn, false, laneChips = new FlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(2, 0)
        }));

        timingCard.Controls.Add(compareChip = new ResultsChip("compare with your best", palette, palette.Text)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Alpha = 0,
            Toggled = on => scatter.Compare = on
        });

        healthCard.Controls.AddRange(new Drawable[]
        {
            legend(),
            hardest = new HardestRow(palette)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Margin = new MarginPadding { Left = 18 },
                Hovered = hovered =>
                {
                    HardestStretch? shown = hovered ? analysis?.Hardest : null;
                    scatter.Highlight = shown;
                    health.Highlight = shown;
                },
                Watch = from => WatchFrom?.Invoke(from)
            }
        });

        lanes.LaneHovered = lane => scatter.BroughtOut = lane;
    }

    /// <summary>
    /// Shows the song played at the top, and when.
    /// </summary>
    public void ShowSong(Beatmap? beatmap, BeatmapSet? set, DateTimeOffset? played = null) => header.ShowSong(beatmap, set, played);

    /// <summary>
    /// Shows <paramref name="run"/>: its totals at once, and the rest from its judgements, if it has them, once they're
    /// worked out on a thread from the pool. Without them, the cards wait (for <see cref="JudgeFrom"/>), or say why there
    /// are none (<see cref="ShowUnavailable"/>).
    /// </summary>
    public void SetRun(GameplayResult run)
    {
        result = run;
        bool judged = run.Judgements.Count > 0;

        header.ShowRun(run, judged ? run.UnstableRate : null);

        foreach (HitResult shown in results)
            resultChips[shown].Text = $"{shown.ToString().ToLowerInvariant()} {countOf(run, shown).ToString("N0", CultureInfo.InvariantCulture)}";

        setWaiting(true);

        if (!judged)
        {
            analysis = null;
            return;
        }

        if (IsLoaded)
            analyse(run);
    }

    // Given its run before it loaded (as the results screen does, from its own load), it works it out now.
    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (result is { Judgements.Count: > 0 } && analysis == null)
            analyse(result);
    }

    private void analyse(GameplayResult run)
    {
        var scheduler = Scheduler;

        Task.Run(() => new PlayAnalysis(run)).ContinueWith(worked =>
        {
            if (worked.IsFaulted)
            {
                Logger.Error("Failed to work out a run's graphs.", worked.Exception!);
                return;
            }

            scheduler.Add(() =>
            {
                // Another run may have come since.
                if (ReferenceEquals(result, run))
                    show(worked.Result);
            });
        });
    }

    private void show(PlayAnalysis worked)
    {
        analysis = worked;

        scatter.SetRun(worked);
        health.SetRun(worked);
        spread.SetRun(worked);
        lanes.SetRun(worked);
        types.SetRun(worked);
        figures.SetRun(worked);
        hardest.SetStretch(worked.Hardest);
        lanesCard.Question = LaneColumns.Verdict(worked);

        // None picked shows every lane, so none picked looks like it.
        laneChips.Children = worked.Lanes.Select(l => (Drawable)new ResultsChip($"{l.Lane + 1}", palette, chipHeight: 24)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            OnAnyway = true,
            Toggled = on =>
            {
                scatter.ShowLane(l.Lane, on);
                updateLaneChips();
            }
        }).ToArray();

        unavailable.Alpha = 0;
        scatter.Alpha = 1;
        zoomControls.Alpha = 1;
        setWaiting(judge is { IsDone: false });
    }

    private void updateLaneChips()
    {
        bool nonePicked = scatter.Lanes.Count == 0;

        foreach (ResultsChip chip in laneChips.Children.OfType<ResultsChip>())
            chip.OnAnyway = nonePicked;
    }

    /// <summary>
    /// The best it's compared with (its judgements, judged again from its replay too); null where there's none.
    /// </summary>
    public void SetBest(PlayAnalysis? best)
    {
        scatter.SetBest(best);
        compareChip.Alpha = best == null ? 0 : 1;
    }

    /// <summary>
    /// The run's replay is being judged again by <paramref name="judging"/>: the graphs draw in behind it as it goes, and
    /// the cards that need every judgement wait until it's done.
    /// </summary>
    public void JudgeFrom(ReplayJudge judging)
    {
        judge = judging;
        setWaiting(!judging.IsDone);
    }

    /// <summary>
    /// There are no judgements, and none to be had: says why, in place of the graphs.
    /// </summary>
    public void ShowUnavailable(string reason)
    {
        judge = null;
        unavailable.Text = reason;
        unavailable.Alpha = 1;
        scatter.Alpha = 0;
        zoomControls.Alpha = 0;
        timingCard.Waiting = false;

        foreach (ResultsCard card in new[] { healthCard, spreadCard, lanesCard, typesCard, figuresCard })
            card.Waiting = false;

        healthCard.Alpha = cards.Alpha = 0.5f;
    }

    /// <summary>
    /// What part of the song the graphs show.
    /// </summary>
    public SongTimeView View => view;

    /// <summary>
    /// What's offered for the offsets: in a strip of its own at the foot of the page, or none where there's nothing.
    /// </summary>
    public void SetAdvice(IEnumerable<Drawable> lines)
    {
        Drawable[] said = lines.ToArray();
        advice.Children = said;

        bool shown = adviceStrip.Parent != null;

        if (said.Length > 0 && !shown)
            Add(adviceStrip);
        else if (said.Length == 0 && shown)
            Remove(adviceStrip, false);
    }

    /// <summary>
    /// What's offered for the offsets, while there is some (for tests).
    /// </summary>
    public Drawable? AdviceStrip => adviceStrip.Parent != null ? adviceStrip : null;

    /// <summary>
    /// The answer, in the timing card, where it doesn't take <see cref="SetAdvice"/>'s strip to give one.
    /// </summary>
    public void SayOfTiming(string verdict) => figuresCard.Question = verdict;

    /// <summary>
    /// The page is opened: its cards come in one after another, and its graphs draw in.
    /// </summary>
    public void Open()
    {
        openedAt = Clock.CurrentTime;
        scatter.DismissWatch();
        health.DismissWatch();
    }

    private void setWaiting(bool waiting)
    {
        // The graphs draw in behind the judging rather than waiting.
        foreach (ResultsCard card in new[] { spreadCard, lanesCard, typesCard, figuresCard })
            card.Waiting = waiting;

        waitingShown = waiting;
        updateTimingQuestion();
    }

    private void updateTimingQuestion() =>
        timingCard.Question = waitingShown ? "Reading your replay…" : view.Zoom > 1 ? "Drag to move along the song" : "Where did I drift?";

    // What the zoom's buttons can do from here, and how far it's zoomed: dragging changes it too.
    private void updateZoomControls()
    {
        zoomText.Text = $"{view.Zoom}×";
        zoomOutChip.On = view.CanZoomOut;
        zoomInChip.On = view.CanZoomIn;
        moveBackChip.On = view.CanMoveBack;
        moveOnChip.On = view.CanMoveOn;
        updateTimingQuestion();
    }

    public override void Update()
    {
        if (view.Version != zoomShown)
        {
            zoomShown = view.Version;
            updateZoomControls();
        }

        float width = DrawWidth;
        float cardWidth = Math.Max(0, (width - card_gap * 3) / 4);

        foreach (Drawable card in cards.Children)
            card.Width = cardWidth;

        float offered = adviceStrip.Parent != null ? adviceStrip.DrawHeight + row_margin : 0;

        // In whole pixels, and rounded down so a page that just fits has nothing to scroll.
        float room = MathF.Floor(ViewHeight - rest_of_page - offered);
        float timingHeight = room >= timing_card_least ? Math.Min(room, timing_card_max) : timing_card_min;
        timingCard.Height = timingHeight;
        scatter.Height = timingHeight - timing_card_top - timing_card_bottom;

        double sinceOpened = Clock.CurrentTime - openedAt;

        // One after another, up into place.
        Drawable[] entering = { header, timingCard, healthCard, spreadCard, lanesCard, typesCard, figuresCard, adviceStrip };

        for (int i = 0; i < entering.Length; i++)
        {
            float shown = (float)EasingFunctions.Apply(Easing.OutQuint, Math.Clamp((sinceOpened - i * 60) / 480, 0, 1));
            entering[i].Alpha = shown < 0.001f ? 0 : shown > 0.999f ? 1 : shown;
        }

        if (judge is { IsDone: false } judging)
        {
            // What's been judged so far, drawn in up to the playhead.
            if (result != null && !analysingSoFar && judging.JudgementCount != judgementsDrawn && Clock.CurrentTime - drawnAt >= judging_redraw)
            {
                judgementsDrawn = judging.JudgementCount;
                drawnAt = Clock.CurrentTime;
                analysingSoFar = true;

                (JudgementResult[] judged, double[] holdBreaks) = judging.Snapshot();
                GameplayResult sofar = result.WithJudgements(judged, holdBreaks, judging.Chart.LaneCount, judging.Chart.LastNoteTime);
                var scheduler = Scheduler;

                Task.Run(() => new PlayAnalysis(sofar)).ContinueWith(worked => scheduler.Add(() =>
                {
                    analysingSoFar = false;

                    // The whole run may have come in since.
                    if (worked.IsFaulted || judge is not { IsDone: false })
                        return;

                    scatter.SetRun(worked.Result);
                    health.SetRun(worked.Result);
                }));
            }

            var playhead = (judging.JudgedTime, judging.JudgedNotes, judging.TotalNotes);
            scatter.Judging = playhead;
            health.Judging = playhead;
            spread.Progress = 0;
        }
        else
        {
            scatter.Judging = null;
            health.Judging = null;

            float drawn = (float)Math.Clamp(sinceOpened / DRAW_IN, 0, 1);
            scatter.Reveal = health.Reveal = (float)EasingFunctions.Apply(Easing.OutCubic, drawn);
            spread.Progress = drawn;
        }

        base.Update();
    }

    private static int countOf(GameplayResult run, HitResult shown) => shown switch
    {
        HitResult.Bloom => run.BloomCount,
        HitResult.Critical => run.CriticalCount,
        HitResult.Break => run.BreakCount,
        HitResult.Hit => run.HitCount,
        _ => run.MissCount
    };

    // A set of the timing card's controls, an icon saying what they're for, in a line round them. Over the graph they have
    // a backing of the card's own color, to stay readable over what's under them.
    private Drawable controlGroup(IconUsage icon, bool overGraph, params Drawable[] controls)
    {
        var row = new FlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(2, 0),
            Padding = new MarginPadding { Left = 8, Right = 3, Top = 2, Bottom = 2 },
            Child = new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Icon = icon,
                IconSize = 16,
                Color = ResultsInk.Secondary(palette),
                Margin = new MarginPadding { Right = 4 }
            }
        };

        row.AddRange(controls);

        var group = new Container
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            AutoSizeAxes = Axes.Both,
            Margin = overGraph ? default : new MarginPadding { Left = 8, Right = 8 },
            Masking = true,
            CornerRadius = 9,
            BorderThickness = 1.5f,
            BorderColor = palette.Text.WithAlpha(0.35f)
        };

        if (overGraph)
            group.Add(new Box { RelativeSizeAxes = Axes.Both, Color = palette.Surface, Alpha = 0.9f });

        group.Add(row);
        return group;
    }

    // A button rather than a toggle: it looks on while there's anything for it to do.
    private ResultsChip zoomChip(string text, Action action) => new ResultsChip(text, palette, chipHeight: 24)
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Action = action
    };

    // What the health card's lines and marks are.
    private Drawable legend()
    {
        var row = new FlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(16, 0)
        };

        row.Add(key(new Box { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(16, 5), Color = palette.Text }, "health"));
        row.Add(key(new Box { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(16, 10), Color = HealthComboGraph.COMBO.WithAlpha(0.9f) }, "combo"));
        var mark = new PlotCanvas { RelativeSizeAxes = Axes.None, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Size = new Vector2(10, 9) };
        mark.Triangle(new Vector2(0, 9), new Vector2(10, 9), new Vector2(5, 0), JudgementColors.MISS);
        row.Add(key(mark, "combo break"));
        return row;

        Drawable key(Drawable swatch, string text) => new FlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new[]
            {
                swatch,
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = text,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 13, FontWeight.Bold),
                    Color = ResultsInk.Secondary(palette)
                }
            }
        };
    }

    /// <summary>
    /// The hardest stretch, said in a line, and a button to watch it; pointing at it shades it on the graphs.
    /// </summary>
    private partial class HardestRow : FlowContainer
    {
        private readonly SpriteText text;

        // What shows the button or not: it sets its own alpha as it loads.
        private readonly Container button;
        private HardestStretch? stretch;

        public Action<bool>? Hovered { get; init; }

        public Action<double>? Watch { get; init; }

        public HardestRow(YuukiColorPalette palette)
        {
            AutoSizeAxes = Axes.Both;
            Direction = FlowDirection.Horizontal;
            Spacing = new Vector2(12, 0);

            Children = new Drawable[]
            {
                text = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold),
                    Color = palette.Text
                },
                // Not until there's a stretch to watch.
                button = new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Alpha = 0,
                    Child = new PillButton(palette.Control, palette.TextLight)
                    {
                        Text = "▶  watch this part",
                        Action = () =>
                        {
                            if (stretch is HardestStretch s)
                                Watch?.Invoke(Math.Max(0, s.Start - SongTimeGraph.WATCH_LEAD));
                        }
                    }
                }
            };
        }

        public void SetStretch(HardestStretch? hardest)
        {
            stretch = hardest;

            if (hardest is not HardestStretch s)
            {
                text.Text = "Nothing lost anywhere";
                button.Alpha = 0;
                return;
            }

            string misses = s.Misses == 1 ? "1 miss" : $"{s.Misses} misses";
            text.Text = $"Hardest 8 s: {time(s.Start)}–{time(s.End)} · {(s.Accuracy * 100).ToString("0.00", CultureInfo.InvariantCulture)}% · {misses}";
            button.Alpha = 1;
        }

        private static string time(double ms)
        {
            var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
            return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
        }

        public override bool OnHover(MouseEvent e)
        {
            Hovered?.Invoke(true);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            Hovered?.Invoke(false);
            return base.OnHoverLost(e);
        }
    }
}
