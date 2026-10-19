// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapInfoPanel : FlowContainer
{
    public enum PanelLayout
    {
        /// <summary>
        /// Everything: source, song stats, difficulty row and the tabs (song select, setup).
        /// </summary>
        Full,

        /// <summary>
        /// Just the title and the difficulty row (results, where the rest of the screen is the point).
        /// </summary>
        Compact
    }

    public enum InfoTab
    {
        Detail,
        Boss,
        Ranking
    }

    /// <summary>
    /// Room after the screen's name (the tag) to its slanted end, at its middle: what "Solo Play" had in
    /// the tag's old fixed width, now kept for any name as the tag fits it.
    /// </summary>
    private const float tag_trailing = 46;
    private const float title_width = 640;
    private const float title_height = 90;
    private const float row_height = 30;
    private const float content_inset = 50;
    /// <summary>The cover in the title row: 13 px clear above and below in the 90 px row.</summary>
    private const float cover_size = 64;

    /// <summary>Gap between the cover and the title beside it.</summary>
    private const float cover_gap = 16;
    private const float detail_width = 680;

    /// <summary>
    /// Scores the Ranking tab shows when nothing says how far down it may reach.
    /// </summary>
    private const int ranking_default_rows = 4;
    private const float score_row_height = 60;
    private const float score_row_gap = 8;

    /// <summary>
    /// A score panel's width, when there's room for it.
    /// </summary>
    private const float score_row_width = 800;

    /// <summary>
    /// The personal best's band: its label above its panel, on a dark strip.
    /// </summary>
    private const float best_band_height = 108;

    /// <summary>
    /// Kept clear between the Ranking tab and whatever is below it (the back button, the strip).
    /// </summary>
    private const float ranking_bottom_margin = 16;

    /// <summary>
    /// The leaderboard leans gently. Each panel sits further left than the one above it by this per unit of height.
    /// </summary>
    private static Vector2 leaderboardShear => UserInterfaceConstant.GentleShear;

    private static float leaderboardSlope => -leaderboardShear.X;

    /// <summary>How far a panel fades out as it scrolls past either end of the list.</summary>
    private const float score_fade_distance = 40;

    /// <summary>
    /// Each row a little shorter than the one above, cascading like the design.
    /// </summary>
    private const float source_row_width = 600;
    private const float set_row_width = 570;
    private const float beatmap_row_width = 540;

    private const double slide_duration = 700;

    /// <summary>How long rows take to leave when the view or tab changes, before the new ones arrive.</summary>
    private const double leave_duration = 250;

    /// <summary>
    /// How far each row's sheared background reaches past its left edge, off-screen. The rows sit
    /// flush against the screen edge, so without this even a small overshoot would bring the
    /// background's slanted end into view.
    /// </summary>
    private const float tail_cover = 40;
    private const double stagger = 55;

    private static readonly Color bullet_color = ColorExtensions.FromHex("#7A3030");

    [Resolved(canBeNull: true)]
    private BeatmapNoteStore? noteStore { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    private readonly YuukiColorPalette palette;
    private readonly PanelLayout layout;
    private readonly bool showCover;

    private readonly List<PanelRow> rows = new List<PanelRow>();

    private PanelRow titleRow = null!;
    private IconRow sourceRow = null!;
    private IconRow setRow = null!;
    private IconRow beatmapRow = null!;
    private PanelRow tabsRow = null!;
    private PanelRow loadingRow = null!;
    private PanelRow notesRow = null!;
    private PanelRow chartRow = null!;
    private PanelRow bossRow = null!;
    private PanelRow rankingRow = null!;

    private ScrollingText titleText = null!;
    private ScrollingText artistText = null!;
    private DelayedLoadCover? cover;
    private BeatmapSet? coverSet;

    private readonly Dictionary<InfoTab, TabButton> tabButtons = new Dictionary<InfoTab, TabButton>();
    private FlowContainer notesFlow = null!;
    private Container rankingContent = null!;
    private FlowContainer chartStats = null!;
    private Container densityGraph = null!;

    /// <summary>A score in the Ranking tab was clicked: the host screen opens it.</summary>
    public event Action<ScoreInfo>? ScoreSelected;

    /// <summary>
    /// The screen-space height the Ranking tab may reach down to (the top of the back button, say). The
    /// list fills down to it and the personal best sits at its foot. Unset, it shows a few scores with the best right under them.
    /// </summary>
    public Func<float>? RankingBottom { get; set; }

    /// <summary>
    /// The screen-space x the Ranking tab's panels must stay left of (the settings column, the song
    /// list). The widest panels stop there, so on a smaller canvas (a larger UI scale) they don't run
    /// into it. Unset, they're only limited by the shear line.
    /// </summary>
    public Func<float>? RankingMaxRight { get; set; }

    private YuukiScrollableContainer? rankingScroll;
    private readonly List<ScoreRow> rankingRows = new List<ScoreRow>();
    private ScoreRow? bestRow;
    private Container? bestBand;
    private float rankingHeight;

    private BeatmapSet? set;
    private Beatmap? beatmap;
    private bool useUnicode;
    private InfoTab tab = InfoTab.Detail;

    // Whether the panel is currently shown; rows only animate their own comings and goings while it is.
    private bool shown;

    /// <summary>
    /// The shortest the spinner shows when switching beatmaps, so a fast load still reads as a beat of
    /// loading rather than a flicker.
    /// </summary>
    private const double minimum_loading_time = 220;

    // Between picking a beatmap and its Detail / Boss / Ranking content being ready: the old content
    // has stepped aside and the spinner is up.
    private bool loading;

    // Bumped per beatmap switch, so a load overtaken by a newer one is dropped.
    private int loadVersion;

    /// <summary>Whether a beatmap switch is still loading its tab content (diagnostics/tests).</summary>
    public bool IsLoading => loading;

    // Bumped for every summary request so a slow read for an earlier beatmap can't land on a later one.
    private int summaryRequest;

    // Bumped for every visibility change, so a change still waiting for rows to leave is dropped
    // when a newer one (another tab click, say) comes in first.
    private int transitionVersion;

    public BeatmapInfoPanel(string tag, YuukiColorPalette palette, PanelLayout layout = PanelLayout.Full, bool showCover = false)
    {
        this.palette = palette;
        this.layout = layout;
        this.showCover = showCover;

        AutoSizeAxes = Axes.Both;
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 8);

        // As wide as the screen's name, which starts where the title's text does.
        addRow(new PanelRow(50, palette.Surface.Darken(0.1f), new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Margin = new MarginPadding { Left = content_inset, Right = tag_trailing },
            Text = tag.ToUpperInvariant(),
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
            Color = palette.Text
        }));

        addRow(titleRow = new PanelRow(new Vector2(title_width, title_height), palette.Surface.Lighten(0.2f), createTitle()));

        addRow(sourceRow = new IconRow(source_row_width, palette));
        addRow(setRow = new IconRow(set_row_width, palette));
        addRow(beatmapRow = new IconRow(beatmap_row_width, palette));

        addRow(tabsRow = new PanelRow(new Vector2(detail_width, 40), null, createTabs()));
        addRow(loadingRow = new PanelRow(new Vector2(detail_width, 76), null, createLoading()));
        addRow(notesRow = new PanelRow(new Vector2(detail_width, 76), palette.Surface.Lighten(0.2f), notesFlow = statFlow()));
        addRow(chartRow = new PanelRow(new Vector2(detail_width - 40, 96), palette.Surface.Lighten(0.2f), createChart()));
        addRow(bossRow = new PanelRow(new Vector2(detail_width, 96), palette.Surface.Lighten(0.2f),
            placeholder(IconUsage.Skull, "No boss in this beatmap", "Boss phases are on the way.")));
        // No shared background: each score is a panel of its own (the empty state draws its own).
        addRow(rankingRow = new PanelRow(new Vector2(detail_width, 96), null,
            rankingContent = new Container { RelativeSizeAxes = Axes.Both }));

        refresh(animate: false);
    }

    #region Building

    private void addRow(PanelRow row)
    {
        rows.Add(row);
        Add(row);
    }

    private Drawable createTitle()
    {
        // The cover starts where the rows below start their icons, so the block shares one left edge.
        float textX = content_inset + (showCover ? cover_size + cover_gap : 0);
        float textWidth = title_width - textX - 70;

        var container = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new FlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = textX,
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 2),
                    Children = new Drawable[]
                    {
                        titleText = new ScrollingText
                        {
                            Size = new Vector2(textWidth, 40),
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 32, FontWeight.Bold),
                            Color = palette.Text,
                            ScrollSpeed = 100
                        },
                        artistText = new ScrollingText
                        {
                            Size = new Vector2(textWidth, 27),
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Regular),
                            Color = palette.Text
                        }
                    }
                }
            }
        };

        if (showCover)
        {
            container.Add(new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = content_inset,
                Size = new Vector2(cover_size),
                Masking = true,
                CornerRadius = 10,
                Child = cover = new DelayedLoadCover
                {
                    RelativeSizeAxes = Axes.Both,
                    DecodeTargetSize = new Vector2(cover_size * 2),
                    PlaceholderColor = palette.Surface.Darken(0.2f)
                }
            });
        }

        return container;
    }

    private Drawable createTabs()
    {
        var flow = new FlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            X = 48,
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Horizontal,
            Spacing = new Vector2(10, 0)
        };

        foreach (InfoTab t in Enum.GetValues<InfoTab>())
        {
            var button = new TabButton(t.ToString(), palette)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Action = () => SelectTab(t)
            };
            tabButtons[t] = button;
            flow.Add(button);
        }

        return flow;
    }

    private Drawable createChart() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Children = new Drawable[]
        {
            chartStats = new FlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = content_inset,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 6)
            },
            densityGraph = new Container
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -90,
                Size = new Vector2(200, 50)
            }
        }
    };

    private FlowContainer statFlow() => new FlowContainer
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        X = content_inset,
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Horizontal,
        Spacing = new Vector2(36, 0)
    };

    /// <summary>A labelled number: icon and label in bold, the value underneath.</summary>
    private Drawable statItem(IconUsage icon, string label, string value, Color? color = null) => new FlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 2),
        Children = new Drawable[]
        {
            new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Children = new Drawable[]
                {
                    new IconSprite
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = icon,
                        IconSize = 20,
                        Filled = true,
                        Color = color ?? palette.Text
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = label,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
                        Color = color ?? palette.Text
                    }
                }
            },
            new SpriteText
            {
                Text = value,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                Color = palette.Text
            }
        }
    };

    /// <summary>While a beatmap's tab content loads: just a spinner, in the middle of where the content goes.</summary>
    private Drawable createLoading() => new LoadingSpinner(palette.Control, 36)
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre
    };

    private Drawable placeholder(IconUsage icon, string title, string description) => new FlowContainer
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        X = content_inset,
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Horizontal,
        Spacing = new Vector2(14, 0),
        Children = new Drawable[]
        {
            new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Icon = icon,
                IconSize = 40,
                Filled = true,
                Color = palette.Text
            },
            new FlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Text = title,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold),
                        Color = palette.Text
                    },
                    new SpriteText
                    {
                        Text = description,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Regular),
                        Color = palette.Text
                    }
                }
            }
        }
    };

    #endregion

    #region Content

    /// <summary>
    /// Whether titles, artists and sources use their unicode (original-script) forms. Follows the
    /// <see cref="YuukiSetting.UseUnicodeInfo"/> setting once loaded; set directly only without a config.
    /// </summary>
    public bool UseUnicode
    {
        get => useUnicode;
        set
        {
            if (useUnicode == value)
                return;

            useUnicode = value;
            refresh(animate: false);
        }
    }

    /// <summary>Shows a song: the set-level view, without difficulty details.</summary>
    public void ShowSet(BeatmapSet? beatmapSet)
    {
        set = beatmapSet;
        beatmap = null;
        refresh(animate: shown);
    }

    /// <summary>Shows one difficulty: the full view, with the difficulty row and tabs.</summary>
    public void ShowBeatmap(Beatmap? difficulty)
    {
        bool changed = !ReferenceEquals(beatmap, difficulty);

        beatmap = difficulty;
        set = difficulty?.BeatmapSet ?? set;

        // Another beatmap while showing: its Detail / Boss / Ranking load behind a spinner, then come in.
        if (changed && shown && difficulty != null && layout == PanelLayout.Full)
            startLoading();
        else
            refresh(animate: shown);
    }

    /// <summary>
    /// Swaps the tab content for another beatmap's: the current content steps aside and a spinner comes
    /// in; only once the old content is gone is the new one filled (so it never shows on panels on their
    /// way out), and once it's ready (the chart may be read off disk) it comes in as a tab switch does.
    /// </summary>
    private void startLoading()
    {
        int version = ++loadVersion;
        loading = true;
        refresh(animate: true);

        afterDelay(leave_duration, () =>
        {
            if (version != loadVersion)
                return;

            double spinnerUp = Clock.CurrentTime;

            updateRanking();
            requestSummary(() =>
            {
                double remaining = minimum_loading_time - (Clock.CurrentTime - spinnerUp);
                afterDelay(Math.Max(0, remaining), () => finishLoading(version));
            });
        });
    }

    private void finishLoading(int version)
    {
        if (version != loadVersion || !loading)
            return;

        loading = false;
        refresh(animate: shown);
    }

    public void SelectTab(InfoTab newTab)
    {
        if (tab == newTab)
            return;

        tab = newTab;
        refresh(animate: shown);
    }

    private void refresh(bool animate)
    {
        updateContent();

        bool full = layout == PanelLayout.Full;
        bool showTabs = full && beatmap != null;

        foreach ((InfoTab t, TabButton button) in tabButtons)
            button.Selected = t == tab;

        sourceRow.Target = full;
        setRow.Target = full;
        beatmapRow.Target = beatmap != null || !full;
        tabsRow.Target = showTabs;

        bool content = showTabs && !loading;
        loadingRow.Target = showTabs && loading;
        notesRow.Target = content && tab == InfoTab.Detail;
        chartRow.Target = content && tab == InfoTab.Detail;
        bossRow.Target = content && tab == InfoTab.Boss;
        rankingRow.Target = content && tab == InfoTab.Ranking;

        applyVisibility(animate);
    }

    /// <summary>
    /// Fills every row from the current set and beatmap. Also run once loaded: a screen may hand the
    /// panel its beatmap straight after constructing it, before the stores below are resolved, and the
    /// level range, play counts, Detail and Ranking would otherwise stay empty until the next change.
    /// </summary>
    private void updateContent()
    {
        string title = set?.GetDisplayTitle(useUnicode) ?? "";
        titleText.Text = string.IsNullOrEmpty(title) ? "No song selected" : title;
        artistText.Text = set?.GetDisplayArtist(useUnicode) ?? "";

        bool full = layout == PanelLayout.Full;
        bool hasBeatmap = beatmap != null;

        // The source row stays even without a source, so the block keeps one shape from song to song.
        string source = set?.GetDisplaySource(useUnicode) ?? "";
        string genre = set?.Genre ?? "";
        sourceRow.SetItems((IconUsage.Share, string.IsNullOrEmpty(source) ? "—" : source), (IconUsage.Album, genre));

        string length = set == null ? "" : TimeSpan.FromMilliseconds(set.TotalLength).ToString(@"mm\:ss");
        string bpm = set == null ? "" : ((int)set.BPM).ToString();

        // The song row reads the same whichever difficulty is picked — the song doesn't change.
        setRow.SetItems(
            (IconUsage.Rocket, levelRange()),
            (IconUsage.ViewColumn, laneRange()),
            (IconUsage.Timer, length),
            (IconUsage.Metro, bpm),
            (IconUsage.PlayArrow, set == null || scoreStore == null ? "" : scoreStore.GetPlayCount(set).ToString("N0")));

        beatmapRow.SetItems(
            (IconUsage.Person, beatmap?.Creator ?? ""),
            (IconUsage.Label, beatmap?.DifficultyName ?? ""),
            (IconUsage.Rocket, beatmap == null ? "" : $"{(int)beatmap.DifficultyRating}"),
            (IconUsage.ViewColumn, beatmap == null ? "" : BeatmapUtility.FormatLanes(beatmap.LaneCount)),
            (IconUsage.Timer, full ? length : ""),
            (IconUsage.Metro, full ? bpm : ""),
            (IconUsage.PlayArrow, beatmap == null || scoreStore == null ? "" : scoreStore.GetPlayCount(beatmap).ToString("N0")));

        // The difficulty row wears the difficulty's colour, as its card in the list does: the colour as
        // the fill, the text a deep shade of it.
        if (beatmap != null)
        {
            Color difficultyColor = BeatmapUtility.ColorForDifficulty(beatmap.DifficultyRating);
            beatmapRow.SetColors(difficultyColor, difficultyColor.Darken(0.6f));
        }
        else
            beatmapRow.SetColors(palette.Surface, palette.Text);

        // While loading, the tab content is filled once the old content has stepped aside (startLoading).
        if (full && hasBeatmap && !loading)
        {
            requestSummary();
            updateRanking();
        }

        if (cover != null && IsLoaded)
            updateCover();
    }

    /// <summary>The lane counts across the song's difficulties: "4K", or "4-7K" when they differ.</summary>
    private string laneRange() => set == null || beatmapsCollection == null
        ? ""
        : BeatmapUtility.FormatLaneRange(beatmapsCollection.GetBeatmapsFromBeatmapSet(set, false));

    private string levelRange()
    {
        if (set == null || beatmapsCollection == null)
            return "";

        var range = beatmapsCollection.GetMinMaxDifficultyLevel(set);
        if (range["max"] <= 0)
            return "";

        return range["min"] == range["max"] ? $"{range["max"]}" : $"{range["min"]}-{range["max"]}";
    }

    /// <summary>
    /// Fills the Detail tab from the chart's summary — cached, or read on a background thread the first
    /// time so scrolling through difficulties never stalls on disk.
    /// </summary>
    /// <param name="ready">Called once the summary is shown (straight away when it's cached).</param>
    private void requestSummary(Action? ready = null)
    {
        Beatmap? target = beatmap;
        int request = ++summaryRequest;

        if (target == null)
        {
            ready?.Invoke();
            return;
        }

        if (target.NoteSummary != null || noteStore == null)
        {
            showSummary(target.NoteSummary ?? BeatmapNoteSummary.From(target.Notes));
            ready?.Invoke();
            return;
        }

        showSummary(null);

        BeatmapNoteStore store = noteStore;
        Task.Run(() => store.GetSummary(target)).ContinueWith(task =>
        {
            Scheduler.Add(() =>
            {
                if (request != summaryRequest)
                    return;

                if (task.IsCompletedSuccessfully)
                    showSummary(task.Result);

                // Even a failed read ends the wait: the Detail tab then keeps its "Reading the chart" line.
                ready?.Invoke();
            });
        });
    }

    private void showSummary(BeatmapNoteSummary? summary)
    {
        string count(int value) => summary == null ? "…" : value.ToString("N0");

        notesFlow.Children = new[]
        {
            statItem(IconUsage.Circle, "Normal", count(summary?.NormalCount ?? 0)),
            statItem(IconUsage.TouchApp, "Touch", count(summary?.TouchCount ?? 0)),
            statItem(IconUsage.Pill, "Bullet", count(summary?.BulletCount ?? 0), bullet_color)
        };

        chartStats.Children = summary == null
            ? new Drawable[] { chartLine("Reading the chart…") }
            : summary.IsEmpty
                ? new Drawable[] { chartLine("No notes yet — a chart will be generated for play.") }
                : new Drawable[]
                {
                    chartLine($"Tap {summary.TapCount:N0}   ·   Hold {summary.HoldCount:N0}   ·   Swipe {summary.SwipeCount:N0}"),
                    chartLine($"Max combo {summary.MaxCombo:N0}   ·   Peak {summary.PeakNotesPerSecond} notes/s")
                };

        densityGraph.Clear();

        if (summary == null)
            return;

        float[] density = summary.Density;
        for (int i = 0; i < density.Length; i++)
        {
            densityGraph.Add(new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                RelativePositionAxes = Axes.X,
                RelativeSizeAxes = Axes.Both,
                X = (float)i / density.Length,
                Width = 0.7f / density.Length,
                Height = Math.Max(density[i], 0.04f),
                Color = palette.Control,
                Alpha = 0.75f
            });
        }
    }

    private void updateRanking()
    {
        IReadOnlyList<ScoreInfo> ranking = beatmap != null && scoreStore != null
            ? scoreStore.GetRanking(beatmap)
            : Array.Empty<ScoreInfo>();

        rankingScroll = null;
        rankingRows.Clear();
        bestRow = null;
        bestBand = null;
        rankingHeight = 0;

        if (ranking.Count == 0)
        {
            rankingContent.Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new[]
                {
                    shearedBackground(detail_width, palette.Surface.Lighten(0.2f)),
                    placeholder(IconUsage.Leaderboard, "No scores yet", "Finish a play to set the first one.")
                }
            };
            rankingRow.Height = 96;
            return;
        }

        var list = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, score_row_gap),
            // Room for the panels' lean at the top and bottom of the list.
            Padding = new MarginPadding
            {
                Top = 2,
                Bottom = 2
            }
        };

        for (int i = 0; i < ranking.Count; i++)
        {
            var row = new ScoreRow($"#{i + 1}", ranking[i], palette) { Action = selectScore(ranking[i]) };
            rankingRows.Add(row);
            list.Add(row);
        }

        ScoreInfo best = ranking[0];

        rankingContent.Child = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                rankingScroll = new YuukiScrollableContainer
                {
                    Child = list
                },
                bestBand = new Container
                {
                    Height = best_band_height,
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 10,
                            Shear = leaderboardShear,
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = palette.Backdrop,
                                Alpha = 0.8f
                            }
                        },
                        new SpriteText
                        {
                            X = content_inset,
                            Y = 10,
                            Text = $"Personal Best (#1 of {ranking.Count:N0})",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold),
                            Color = Color.White
                        }
                    }
                },
                bestRow = new ScoreRow("#1", best, palette, highlighted: true) { Action = selectScore(best) }
            }
        };

        layoutRanking();
    }

    public override void Update()
    {
        base.Update();

        if (rankingScroll != null)
            layoutRanking();
    }

    /// <summary>
    /// Sizes the Ranking tab to the room it has (the best at its foot, the list filling the rest), then
    /// places each panel by where it sits now — so the lean and the fades hold while the list scrolls.
    /// </summary>
    private void layoutRanking()
    {
        if (rankingScroll == null || bestRow == null || bestBand == null)
            return;

        float height = rankingHeightAvailable();

        if (Math.Abs(height - rankingHeight) > 0.5f)
        {
            rankingHeight = height;
            rankingRow.Height = height;

            float bandTop = height - best_band_height;
            bestBand.Y = bandTop;
            bestRow.Y = height - score_row_height - 10;
            rankingScroll.Height = Math.Max(score_row_height, bandTop - score_row_gap);
        }

        // The best sits at the left edge; the panels above it step right along the lean. They all keep
        // one width: as much as fits short of whatever is on the right, at most the full width.
        float topShift = leaderboardSlope * bestRow.Y;
        float maxRight = RankingMaxRight != null
            ? ToLocalSpace(new Vector2(RankingMaxRight(), 0)).X - 16
            : float.MaxValue;
        float rowWidth = Math.Clamp(maxRight - topShift - leaderboardSlope * score_row_height / 2, 560, score_row_width);

        rankingScroll.Width = rowWidth + topShift + 24;
        bestRow.Place(0, rowWidth);
        bestBand.X = -leaderboardSlope * best_band_height / 2;
        bestBand.Width = rowWidth + leaderboardSlope * best_band_height;

        float viewTop = rankingScroll.Y;
        float viewBottom = rankingScroll.Y + rankingScroll.Height;

        foreach (ScoreRow row in rankingRows)
        {
            float top = rankingContent.ToLocalSpace(new Vector2(row.DrawRectangle.X, row.DrawRectangle.Y)).Y;
            row.Place(leaderboardSlope * (bestRow.Y - top), rowWidth);

            float fadeIn = (top + score_row_height - viewTop) / score_fade_distance;
            float fadeOut = (viewBottom - top) / score_fade_distance;
            row.Visibility = Math.Clamp(Math.Min(fadeIn, fadeOut), 0, 1);
        }
    }

    private float rankingHeightAvailable()
    {
        float fallback = ranking_default_rows * (score_row_height + score_row_gap) + best_band_height + score_row_gap;

        if (RankingBottom == null)
            return fallback;

        float bottom = ToLocalSpace(new Vector2(0, RankingBottom())).Y - ranking_bottom_margin;
        float room = bottom - rankingRow.Y;

        // Never less than one score above the best.
        return Math.Max(best_band_height + score_row_height + score_row_gap * 2, room);
    }

    private Action selectScore(ScoreInfo score) => () => ScoreSelected?.Invoke(score);

    /// <summary>
    /// A sheared panel background flush with the screen's left edge, its slanted end tucked off it.
    /// </summary>
    private static Drawable shearedBackground(float width, Color color) => new Container
    {
        Anchor = Anchor.CentreRight,
        Origin = Anchor.CentreRight,
        RelativeSizeAxes = Axes.Y,
        Width = width + tail_cover,
        Masking = true,
        Shear = UserInterfaceConstant.Shear,
        CornerRadius = 5,
        Child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = color
        }
    };

    private SpriteText chartLine(string text) => new SpriteText
    {
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
        Color = palette.Text
    };

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Follows the setting live, so every host screen updates when it's changed from the settings
        // overlay. Tracked, so the shared setting doesn't keep the panel (and its screen) alive.
        if (config != null)
        {
            var setting = config.Get<bool>(YuukiSetting.UseUnicodeInfo);
            useUnicode = setting.Value;
            BindValueChanged(setting, e => UseUnicode = e.NewValue);
        }

        // Anything shown before the stores resolved was filled without them.
        updateContent();

        // The cover resolves its texture manager on load, so a source set before then would be ignored.
        if (cover != null)
            updateCover();
    }

    private void updateCover()
    {
        if (cover == null || set == coverSet)
            return;

        coverSet = set;
        cover.SetSource(set != null && storage != null && (set.Cover != null || !string.IsNullOrEmpty(set.CoverStoragePath))
            ? new BeatmapSetCoverSource(set, storage)
            : null);
    }

    #endregion

    #region Animation

    /// <summary>Slides every row that should show in from the left, one after another, with a soft settle.</summary>
    public void AnimateIn()
    {
        shown = true;
        transitionVersion++;

        int index = 0;
        foreach (PanelRow row in rows)
        {
            row.ClearTransforms();
            row.Displayed = row.Target;
            row.Alpha = row.Target ? 1 : 0;

            if (row.Target)
                row.SlideIn(index++ * stagger, slide_duration);
        }
    }

    /// <summary>
    /// Sends the rows back out to the left, bottom row first.
    /// </summary>
    public void AnimateOut()
    {
        shown = false;
        transitionVersion++;

        int index = 0;
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i].Displayed)
                rows[i].SlideOut(index++ * stagger * 0.5, 350);
        }
    }

    /// <summary>
    /// Brings the rows on screen in line with their <see cref="PanelRow.Target"/>s. Animated, it runs
    /// in two beats: rows that are going slide out first, and only once they're gone are they collapsed
    /// out of the flow and the arriving rows slid in. Doing both at once let the new rows appear below
    /// the leaving ones and then jump up when those collapsed.
    /// </summary>
    private void applyVisibility(bool animate)
    {
        int version = ++transitionVersion;

        if (!animate)
        {
            foreach (PanelRow row in rows)
            {
                row.ClearTransforms();
                row.Displayed = row.Target;
                row.Alpha = row.Target ? 1 : 0;
                row.SnapIn();
            }

            return;
        }

        var leaving = rows.Where(r => r.Displayed && !r.Target).ToList();
        // Rows sent away by a change that's since been overruled are still on screen, mid-exit: bring them back.
        var arriving = rows.Where(r => r.Target && (!r.Displayed || r.SlidingOut)).ToList();

        foreach (PanelRow row in leaving)
            row.SlideOut(0, leave_duration);

        afterDelay(leaving.Count > 0 ? leave_duration : 0, () =>
        {
            if (version != transitionVersion)
                return;

            foreach (PanelRow row in leaving)
            {
                row.Displayed = false;
                row.Alpha = 0;
            }

            int index = 0;
            foreach (PanelRow row in arriving)
            {
                row.Displayed = true;
                row.Alpha = 1;
                row.SlideIn(index++ * stagger, slide_duration * 0.8);
            }
        });
    }

    private void afterDelay(double delay, Action action)
    {
        if (delay <= 0)
            action();
        else
            Scheduler.AddDelayed(action, delay);
    }

    #endregion

    /// <summary>
    /// One row of the panel. The flow positions this wrapper, so the slide animates the inner
    /// <see cref="Inner"/> instead — moving the wrapper would fight the flow's layout.
    /// </summary>
    private partial class PanelRow : Container
    {
        public readonly Container Inner;

        /// <summary>The row's sheared fill, if it has one.</summary>
        protected readonly Box? Background;

        /// <summary>Whether the row should be shown for the current view and tab.</summary>
        public bool Target = true;

        /// <summary>Whether the row currently takes its place in the flow (it may be sliding in or out).</summary>
        public bool Displayed = true;

        /// <summary>Whether the row's content is on its way out (or out) to the left.</summary>
        public bool SlidingOut { get; private set; }

        public PanelRow(Vector2 size, Color? background, Drawable content)
            : this(background, content, fitWidth: false)
        {
            Size = size;
        }

        /// <summary>A row <paramref name="height"/> tall and as wide as <paramref name="content"/> (with its margins).</summary>
        public PanelRow(float height, Color? background, Drawable content)
            : this(background, content, fitWidth: true)
        {
            Height = height;
        }

        private PanelRow(Color? background, Drawable content, bool fitWidth)
        {
            if (fitWidth)
                AutoSizeAxes = Axes.X;

            Child = Inner = new Container
            {
                RelativeSizeAxes = fitWidth ? Axes.Y : Axes.Both
            };

            if (fitWidth)
                Inner.AutoSizeAxes = Axes.X;

            if (background is Color color)
            {
                Inner.Add(new Container
                {
                    // The row's size, widened leftwards by tail_cover (a negative margin grows a relative
                    // size), so the slanted left end stays off-screen; the right end, the one that
                    // shows, is the row's. Relative, it leaves a fitted row's width to the content.
                    RelativeSizeAxes = Axes.Both,
                    Margin = new MarginPadding { Left = -tail_cover },
                    Masking = true,
                    Shear = UserInterfaceConstant.Shear,
                    CornerRadius = 5,
                    Child = Background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = color
                    }
                });
            }

            Inner.Add(content);
        }

        // Bumped by every slide so a delayed start that's been overtaken (e.g. a slide-out arriving
        // before a staggered slide-in began) doesn't run. Wait() can't do this: it only delays the
        // next transform added, not the move and fade together.
        private int slideVersion;

        public void SlideIn(double delay, double duration)
        {
            int version = ++slideVersion;
            SlidingOut = false;

            Inner.ClearTransforms();
            Inner.X = -slideDistance;
            Inner.Alpha = 0;

            afterDelay(delay, () =>
            {
                if (version != slideVersion)
                    return;

                Inner.MoveToX(0, duration, UserInterfaceConstant.SoftBounce);
                Inner.FadeIn(duration / 2, Easing.OutQuint);
            });
        }

        /// <summary>Puts the content in place at once, cancelling any slide still pending.</summary>
        public void SnapIn()
        {
            ++slideVersion;
            SlidingOut = false;
            Inner.ClearTransforms();
            Inner.X = 0;
            Inner.Alpha = 1;
        }

        public void SlideOut(double delay, double duration)
        {
            int version = ++slideVersion;
            SlidingOut = true;

            afterDelay(delay, () =>
            {
                if (version != slideVersion)
                    return;

                Inner.ClearTransforms();
                // No wind-up: a backswing would pull the row right and show its slanted end.
                Inner.MoveToX(-slideDistance, duration, Easing.InCubic);
                Inner.FadeOut(duration * 0.85, Easing.InQuint);
            });
        }

        /// <summary>
        /// Far enough left to be out of sight. Measured on <see cref="Inner"/>, not the row: a row fitted
        /// to its content follows Inner, so it narrows as Inner slides away.
        /// </summary>
        private float slideDistance => Inner.DrawWidth + 120;

        private void afterDelay(double delay, Action action)
        {
            if (delay <= 0)
                action();
            else
                Scheduler.AddDelayed(action, delay);
        }
    }

    private partial class ScoreRow : ClickableContainer
    {
        private const float rank_tab_width = 60;
        private const float score_block_width = 230;
        private const float corner_radius = 8;

        // Wide enough for any lean and width the list asks for; only the visible panel takes input.
        private const float max_width = 2000;

        private static readonly Color perfect_value = ColorExtensions.FromHex("#B5E655");

        /// <summary>
        /// Below this width the date gives way, so it can't run into the max combo column.
        /// </summary>
        private const float min_width_for_date = 720;

        private readonly Container inner;
        private readonly Box fill;
        private readonly Color restColor;
        private readonly Drawable[] dateParts;

        private readonly SpriteText agoText;
        private readonly DateTimeOffset date;
        private long shownSecond = -1;

        /// <summary>
        /// How visible the panel is (it fades as it scrolls past the list's ends).
        /// </summary>
        public float Visibility
        {
            set => inner.Alpha = value;
        }

        public ScoreRow(string position, ScoreInfo score, YuukiColorPalette palette, bool highlighted = false)
        {
            Size = new Vector2(max_width, score_row_height);
            restColor = palette.Backdrop;

            Color rankColor = JudgementColors.For(score.Rank);
            string badge = score.IsAllPerfect ? "AP" : score.IsFullCombo ? "FC" : "";
            Color badgeColor = score.IsAllPerfect ? JudgementColors.CRITICAL : JudgementColors.BREAK;

            date = score.Date;
            agoText = text(ago(date), 14, FontWeight.Regular, Color.White.WithAlpha(0.7f), Anchor.CentreLeft, 84, -11);
            Drawable dateText = text(score.Date.LocalDateTime.ToString("d MMM yyyy, HH:mm:ss"), 18, FontWeight.Bold, Color.White, Anchor.CentreLeft, 84, 10);

            Child = inner = new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = score_row_width,
                Children = new Drawable[]
                {
                    sheared(fill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = restColor,
                        Alpha = 0.75f
                    }, Axes.Both),
                    // The personal best's wash of colour behind its position.
                    highlighted
                        ? sheared(new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            ColorInfo = ColorInfo.GradientHorizontal(palette.Accent.WithAlpha(0.9f), palette.Accent.WithAlpha(0))
                        }, Axes.Y, width: 170)
                        : new Container(),
                    // The score on its own darker block, joined to the rank tab.
                    sheared(new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.Black,
                        Alpha = 0.3f
                    }, Axes.Y, width: score_block_width + rank_tab_width, anchor: Anchor.CentreRight),
                    sheared(new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = rankColor
                    }, Axes.Y, width: rank_tab_width, anchor: Anchor.CentreRight),
                    text(position, 22, FontWeight.Bold, Color.White, Anchor.CentreLeft, 18),
                    agoText,
                    new FlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 170,
                        Y = -11,
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = badge,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 13, FontWeight.Bold),
                                Color = badgeColor,
                                Alpha = badge.Length > 0 ? 1 : 0
                            },
                            new IconSprite
                            {
                                Icon = IconUsage.PlayCircle,
                                IconSize = 15,
                                Filled = true,
                                Color = Color.White,
                                Alpha = score.HasReplay ? 0.8f : 0
                            }
                        }
                    },
                    dateText,
                    stat("MAX COMBO", $"{score.MaxCombo:N0}x", score.IsFullCombo ? perfect_value : Color.White,
                        -(rank_tab_width + score_block_width + 210)),
                    stat("ACCURACY", $"{score.Accuracy * 100:0.00}%", score.Accuracy >= 1 ? perfect_value : Color.White,
                        -(rank_tab_width + score_block_width + 100)),
                    text(score.Score.ToString("N0"), 30, FontWeight.Regular, Color.White, Anchor.CentreRight, -(rank_tab_width + 18)),
                    text(score.Rank.ToString(), 24, FontWeight.Bold, Color.White, Anchor.CentreRight, -(rank_tab_width / 2 - 6), origin: Anchor.Centre)
                }
            };

            dateParts = new[] { agoText, dateText };

            inner.Add(new HoverClickSounds(UISound.Select));
        }

        /// <summary>Puts the panel <paramref name="shift"/> along from the list's left edge, <paramref name="width"/> wide.</summary>
        public void Place(float shift, float width)
        {
            inner.X = shift;
            inner.Width = width;

            foreach (Drawable part in dateParts)
                part.Alpha = width >= min_width_for_date ? 1 : 0;
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => inner.Contains(screenSpacePos);

        /// <summary>A fill leaning with the leaderboard; whatever sits on it stays upright.</summary>
        private static Container sheared(Drawable fill, Axes relativeAxes, float width = 0, Anchor anchor = Anchor.CentreLeft) => new Container
        {
            Anchor = anchor,
            Origin = anchor,
            RelativeSizeAxes = relativeAxes,
            Width = relativeAxes.HasFlag(Axes.X) ? 1 : width,
            Masking = true,
            CornerRadius = corner_radius,
            Shear = leaderboardShear,
            Child = fill
        };

        private static SpriteText text(string value, int size, FontWeight weight, Color color, Anchor anchor, float x, float y = 0, Anchor? origin = null) => new SpriteText
        {
            Anchor = anchor,
            Origin = origin ?? anchor,
            X = x,
            Y = y,
            Text = value,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, size, weight),
            Color = color
        };

        /// <summary>
        /// A small caption over its value placed from the right end.
        /// </summary>
        private static Drawable stat(string caption, string value, Color valueColor, float x) => new Container
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreLeft,
            X = x,
            Size = new Vector2(100, 40),
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = caption,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 11, FontWeight.Bold),
                    Color = Color.White.WithAlpha(0.6f)
                },
                new SpriteText
                {
                    Y = 15,
                    Text = value,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold),
                    Color = valueColor
                }
            }
        };

        public override void Update()
        {
            base.Update();

            // Rewritten only when the second ticks over, not every frame.
            long second = DateTimeOffset.Now.ToUnixTimeSeconds();
            if (second == shownSecond)
                return;

            shownSecond = second;
            agoText.Text = ago(date);
        }

        /// <summary>
        /// How long ago, to the second while it's recent, then in the two largest units:
        /// "just now", "42s ago", "5m 12s ago", "3h 7m ago", "2d 4h ago", "4mo 2d ago", "1y 3mo ago".
        /// </summary>
        private static string ago(DateTimeOffset date)
        {
            // TODO: will convert this to central utils
            TimeSpan age = DateTimeOffset.Now - date;

            if (age.TotalSeconds < 1)
                return "just now";
            if (age.TotalMinutes < 1)
                return $"{age.Seconds}s ago";
            if (age.TotalHours < 1)
                return $"{age.Minutes}m {age.Seconds}s ago";
            if (age.TotalDays < 1)
                return $"{age.Hours}h {age.Minutes}m ago";
            if (age.TotalDays < 30)
                return $"{age.Days}d {age.Hours}h ago";

            int days = (int)age.TotalDays;
            if (days < 365)
                return $"{days / 30}mo {days % 30}d ago";

            return $"{days / 365}y {days % 365 / 30}mo ago";
        }

        public override bool OnHover(MouseEvent e)
        {
            fill.FadeToColor(restColor.Lighten(0.35f), 150, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            fill.FadeToColor(restColor, 150, Easing.OutQuint);
            return base.OnHoverLost(e);
        }
    }

    /// <summary>
    /// A tab as a small sheared button: the selected one filled dark with light text, the rest light
    /// with dark text, brightening on hover so it reads as clickable.
    /// </summary>
    private partial class TabButton : ClickableContainer
    {
        private const double fade_duration = 200;

        private readonly Box background;
        private readonly SpriteText label;
        private readonly YuukiColorPalette palette;

        private bool selected;
        private bool hovered;

        public bool Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;

                selected = value;
                updateState();
            }
        }

        public TabButton(string text, YuukiColorPalette palette)
        {
            this.palette = palette;

            AutoSizeAxes = Axes.Both;

            Children = new Drawable[]
            {
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Shear = UserInterfaceConstant.Shear,
                    CornerRadius = 5,
                    Child = background = new Box
                    {
                        RelativeSizeAxes = Axes.Both
                    }
                },
                label = new SpriteText
                {
                    Margin = new MarginPadding { Left = 22, Right = 22, Top = 5, Bottom = 6 },
                    Text = text,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold)
                }
            };

            updateState();
            FinishTransforms(true);

            Add(new HoverClickSounds(UISound.TabSelect));
        }

        public override bool OnHover(MouseEvent e)
        {
            hovered = true;
            updateState();
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hovered = false;
            updateState();
            return base.OnHoverLost(e);
        }

        private void updateState()
        {
            Color fill = selected ? palette.Control : palette.Surface.Lighten(hovered ? 0.4f : 0.2f);

            background.FadeToColor(fill, fade_duration, Easing.OutQuint);
            background.FadeTo(selected || hovered ? 1f : 0.8f, fade_duration, Easing.OutQuint);
            label.FadeToColor(selected ? Color.White : palette.Text, fade_duration, Easing.OutQuint);
        }
    }

    /// <summary>A thin row of icon + text pairs; pairs with empty text are left out.</summary>
    private partial class IconRow : PanelRow
    {
        private readonly FlowContainer flow;

        // What its icons and text are drawn in (the difficulty row follows the difficulty).
        private Color textColor;

        public IconRow(float width, YuukiColorPalette palette)
            : this(width, palette, new FlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = content_inset,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(6, 0)
            })
        {
        }

        private IconRow(float width, YuukiColorPalette palette, FlowContainer flow)
            : base(new Vector2(width, row_height), palette.Surface, flow)
        {
            this.flow = flow;
            textColor = palette.Text;
        }

        /// <summary>Recolours the row's fill and its icons and text, easing over when on screen.</summary>
        public void SetColors(Color background, Color text)
        {
            textColor = text;

            if (!IsLoaded)
            {
                if (Background != null)
                    Background.Color = background;

                foreach (Drawable item in flow.Children)
                    item.Color = text;

                return;
            }

            // Cleared first: the framework retargets a running fade in place, keeping its old timing.
            if (Background != null)
            {
                Background.ClearTransforms();
                Background.FadeToColor(background, 250, Easing.OutQuint);
            }

            foreach (Drawable item in flow.Children)
            {
                item.ClearTransforms();
                item.FadeToColor(text, 250, Easing.OutQuint);
            }
        }

        public void SetItems(params (IconUsage Icon, string Text)[] items)
        {
            flow.Clear();

            foreach ((IconUsage icon, string text) in items)
            {
                if (string.IsNullOrEmpty(text))
                    continue;

                flow.Add(new IconSprite
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    // A wider gap before every pair after the first, so they read as separate facts.
                    Margin = new MarginPadding { Left = flow.Children.Count > 0 ? 14 : 0 },
                    Icon = icon,
                    IconSize = 18,
                    Filled = true,
                    Color = textColor
                });
                flow.Add(new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = text,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Regular),
                    Color = textColor
                });
            }
        }
    }
}
