// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// A graph of something through the song, left to right
/// </summary>
/// <remarks>What it graphs, its subclass draws on <see cref="Marks"/> (<see cref="Plot"/>), in its own pixels.</remarks>
public abstract partial class SongTimeGraph : Container
{
    /// <summary>
    /// Where the graph's left and right edges are, in from its own: the left leaves room for the labels up its side.
    /// </summary>
    protected const float LEFT = 58;

    protected const float RIGHT_GAP = 12;

    /// <summary>
    /// How far before a moment the replay opens, in ms, to see it come.
    /// </summary>
    public const double WATCH_LEAD = 2000;

    protected readonly YuukiColorPalette Palette;

    /// <summary>
    /// What's revealed left to right: the marks of the graph itself.
    /// </summary>
    protected readonly PlotCanvas Marks;

    /// <summary>
    /// What's drawn whole from the start, under the marks: its zones and its grid.
    /// </summary>
    protected readonly PlotCanvas Backdrop;

    private readonly Container revealed;
    private readonly Container labels;
    private readonly PlotCanvas overlay;
    private readonly Box revealLine;
    private readonly Box hoverLine;
    private readonly Container judgingPill;
    private readonly SpriteText judgingText;
    private readonly Container watch;
    private readonly PillButton watchButton;

    protected readonly PlotTooltip Tooltip;

    private float plottedWidth = -1;
    private float plottedHeight = -1;
    private int plottedVersion = -1;
    private SongTimeView view = new SongTimeView();
    private float dragged;
    private float reveal = 1;
    private double? hoverTime;
    private double watchFrom;

    /// <summary>
    /// The run graphed; null for none yet.
    /// </summary>
    protected PlayAnalysis? Run { get; private set; }

    /// <summary>
    /// The song's span, in ms.
    /// </summary>
    protected double Length => Math.Max(1, Run?.Length ?? 1);

    /// <summary>
    /// How much of the song it shows, and where: one shared with the graphs it's lined up with.
    /// </summary>
    public SongTimeView View
    {
        get => view;
        set
        {
            view = value;
            plottedVersion = -1;
        }
    }

    /// <summary>
    /// Where the plot's left and right edges are, whatever part of the song is on them.
    /// </summary>
    protected float PlotLeft => LEFT;

    protected float PlotRight => DrawWidth - RIGHT_GAP;

    /// <summary>
    /// Where along the graph the top and bottom of its plot are (the time labels go under it).
    /// </summary>
    protected abstract float PlotTop { get; }

    protected abstract float PlotBottom { get; }

    /// <summary>
    /// How far along the song it's drawn, 0–1.
    /// </summary>
    public float Reveal
    {
        get => reveal;
        set => reveal = Math.Clamp(value, 0, 1);
    }

    /// <summary>
    /// While the replay is judged again: how far it's got (in ms of the song), and how many notes of how many are in. The
    /// graph is drawn to there, under a playhead that says so. Null once it's done.
    /// </summary>
    public (double Time, int Judged, int Total)? Judging { get; set; }

    /// <summary>
    /// The stretch to shade (the hardest, while it's pointed at); null for none.
    /// </summary>
    public HardestStretch? Highlight
    {
        get => highlight;
        set
        {
            if (highlight == value)
                return;

            highlight = value;
            drawOverlay();
        }
    }

    private HardestStretch? highlight;

    /// <summary>
    /// Called with where in the song to open the replay at, when watching from somewhere is picked.
    /// </summary>
    public Action<double>? WatchFrom { get; set; }

    protected SongTimeGraph(YuukiColorPalette palette, float height)
    {
        Palette = palette;
        Height = height;

        Children = new Drawable[]
        {
            Backdrop = new PlotCanvas(),
            labels = new Container { RelativeSizeAxes = Axes.Both },
            revealed = new Container
            {
                RelativeSizeAxes = Axes.Y,
                Masking = true,
                Child = Marks = new PlotCanvas { RelativeSizeAxes = Axes.Y }
            },
            overlay = new PlotCanvas(),
            revealLine = new Box
            {
                Width = 2,
                Color = Color.White,
                Alpha = 0,
                AlwaysPresent = true
            },
            hoverLine = new Box
            {
                Width = 1.5f,
                Color = palette.Text.WithAlpha(0.55f),
                Alpha = 0,
                AlwaysPresent = true
            },
            judgingPill = new Container
            {
                AutoSizeAxes = Axes.Both,
                Origin = Anchor.TopCentre,
                Masking = true,
                CornerRadius = 12,
                Alpha = 0,
                AlwaysPresent = true,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Color = palette.Text },
                    judgingText = new SpriteText
                    {
                        Margin = new MarginPadding { Left = 11, Right = 11, Top = 4, Bottom = 4 },
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 13, FontWeight.Bold),
                        Color = Color.White
                    }
                }
            },
            Tooltip = new PlotTooltip(),
            // In a holder of its own, which is what shows it: the button sets its own alpha as it loads.
            watch = new Container
            {
                AutoSizeAxes = Axes.Both,
                Origin = Anchor.TopCentre,
                Alpha = 0,
                Child = watchButton = new PillButton(palette.Control, palette.TextLight)
                {
                    Action = () =>
                    {
                        dismissWatch();
                        WatchFrom?.Invoke(watchFrom);
                    }
                }
            }
        };
    }

    /// <summary>
    /// Graphs <paramref name="run"/>.
    /// </summary>
    public void SetRun(PlayAnalysis run)
    {
        Run = run;
        plottedWidth = -1;
    }

    /// <summary>
    /// Where a moment in the song is, across the graph.
    /// </summary>
    protected float XOf(double time) => LEFT + (float)((time / Length - view.Start) * view.Zoom) * (DrawWidth - RIGHT_GAP - LEFT);

    /// <summary>
    /// The moment in the song at a place across the graph.
    /// </summary>
    protected double TimeAt(float x) => (Math.Clamp((x - LEFT) / (DrawWidth - RIGHT_GAP - LEFT), 0, 1) / view.Zoom + view.Start) * Length;

    /// <summary>
    /// Draws the graph again, on <see cref="Backdrop"/> and <see cref="Marks"/>, for <see cref="Run"/> at the graph's width.
    /// </summary>
    protected abstract void Plot();

    /// <summary>
    /// Adds a label up the graph's side, its right edge by the plot at <paramref name="y"/>.
    /// </summary>
    protected void SideLabel(string text, float y, Color? color = null, int size = 13) => labels.Add(new SpriteText
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.CentreRight,
        Position = new Vector2(LEFT - 10, y),
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, size, FontWeight.Bold),
        Color = color ?? ResultsInk.Secondary(Palette)
    });

    /// <summary>
    /// Adds a label on the plot itself, left-aligned at <paramref name="at"/>.
    /// </summary>
    protected void PlotLabel(string text, Vector2 at, Color? color = null, int size = 13) => labels.Add(new SpriteText
    {
        Origin = Anchor.CentreLeft,
        Position = at,
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, size, FontWeight.Bold),
        Color = color ?? ResultsInk.Secondary(Palette)
    });

    /// <summary>
    /// Re-plots a graph whose look changed (a filter, a lane picked).
    /// </summary>
    protected void Replot() => plottedWidth = -1;

    /// <summary>
    /// Whether it says what's going on for the graphs lined up with it: how far the re-judge has got and which part of the
    /// song is shown (the graph on top does; the one under it, just the line).
    /// </summary>
    protected virtual bool SaysForTheOthers => true;

    /// <summary>
    /// What's said where health ran out.
    /// </summary>
    protected virtual string FailLabel => "fell here";

    /// <summary>
    /// What to say about the moment at <paramref name="at"/> under the pointer, and the time to watch from; null to say
    /// just the time.
    /// </summary>
    protected virtual (string Text, Color? Color, Vector2 At, double Time)? Describe(Vector2 at) => null;

    /// <summary>
    /// Marks what <see cref="Describe"/> found under the pointer (a ring round a dot); nothing by default.
    /// </summary>
    protected virtual void MarkHovered(PlotCanvas canvas, Vector2? at)
    {
    }

    public override void Update()
    {
        // Laid out before the frame's matrices: the reveal, the lines and the pill are where this frame puts them.
        float width = DrawWidth;
        float height = DrawHeight;

        if (Run != null && width > LEFT + RIGHT_GAP && (Math.Abs(width - plottedWidth) > 0.5f || Math.Abs(height - plottedHeight) > 0.5f || view.Version != plottedVersion))
        {
            plottedWidth = width;
            plottedHeight = height;
            plottedVersion = view.Version;
            Backdrop.Clear();
            Marks.Clear();
            labels.Clear();
            Marks.Width = width;
            Plot();
            timeLabels();
            drawOverlay();

            if (Run.FailedAt is double failed && XOf(failed) >= PlotLeft && XOf(failed) <= PlotRight)
                PlotLabel(FailLabel, new Vector2(XOf(failed) + 8, PlotTop + 12), JudgementColors.MISS.Darken(0.2f), 13);
        }

        double shownTo = Judging?.Time ?? reveal * Length;
        float edge = Math.Clamp(XOf(shownTo), 0, width);
        bool drawing = Judging != null || reveal < 1;

        // Zoomed in, what's off either side of the plot isn't to show past it. All of the song leaves the first and last
        // dots whole.
        float clipLeft = view.Zoom > 1 ? PlotLeft : 0;
        float clipRight = view.Zoom > 1 ? PlotRight : width;

        revealed.X = clipLeft;
        Marks.X = -clipLeft;
        revealed.Width = Math.Max(0, (Judging == null && reveal >= 1 ? clipRight : Math.Min(edge, clipRight)) - clipLeft);
        revealLine.Alpha = Judging == null && reveal < 1 && reveal > 0 ? 0.9f : 0;
        revealLine.X = edge - 1;
        revealLine.Y = PlotTop - 4;
        revealLine.Height = PlotBottom - PlotTop + 8;

        if (Judging is var (time, judged, total))
        {
            judgingText.Text = $"re-judging · {formatTime(Math.Min(time, Length))} · {judged.ToString("N0", CultureInfo.InvariantCulture)} / {total.ToString("N0", CultureInfo.InvariantCulture)} notes";
            judgingPill.Alpha = SaysForTheOthers ? 1 : 0;

            // Kept on the graph, its playhead running down from it.
            float half = judgingPill.DrawWidth / 2;
            judgingPill.X = Math.Clamp(edge, LEFT + 70 + half, width - half);
            judgingPill.Y = PlotTop - 2;
            hoverLine.Alpha = 0;
        }
        else
            judgingPill.Alpha = 0;

        revealLine.Color = Judging != null ? Palette.Text : Color.White;

        if (Judging != null)
        {
            revealLine.Alpha = edge >= PlotLeft && edge <= PlotRight ? 1 : 0;
            revealLine.Y = PlotTop + 22;
            revealLine.Height = PlotBottom - PlotTop - 18;
        }

        if (hoverTime is double hovered && !drawing)
        {
            hoverLine.Alpha = 1;
            hoverLine.X = XOf(hovered) - 0.75f;
            hoverLine.Y = PlotTop - 4;
            hoverLine.Height = PlotBottom - PlotTop + 8;
        }
        else
            hoverLine.Alpha = 0;

        base.Update();
    }

    private void timeLabels()
    {
        // Every 30 s along a song, every 10 along a short one or a part of one, and every 5 along a shorter one yet.
        double shown = Length / view.Zoom;
        double step = shown >= 90_000 ? 30_000 : shown >= 25_000 ? 10_000 : 5_000;
        double from = view.Start * Length;

        for (double at = Math.Ceiling(from / step) * step; at <= from + shown + 1; at += step)
        {
            labels.Add(new SpriteText
            {
                Origin = Anchor.TopCentre,
                Position = new Vector2(XOf(at), PlotBottom + 12),
                Text = formatTime(at),
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 13, FontWeight.Bold),
                Color = ResultsInk.Secondary(Palette)
            });
        }
    }

    // What's drawn over the marks: the hardest stretch while it's pointed at, and where health ran out.
    private void drawOverlay()
    {
        overlay.Clear();

        if (Run == null || DrawWidth <= LEFT + RIGHT_GAP)
            return;

        // From its first note to its last, and a little either side so the notes at its ends are in it, as far as is shown.
        if (highlight is HardestStretch stretch)
        {
            float from = Math.Max(XOf(stretch.Start) - 6, PlotLeft - 6);
            float to = Math.Min(XOf(stretch.End) + 6, PlotRight + 6);

            if (to > from)
                overlay.Box(new RectangleF(from, PlotTop - 6, to - from, PlotBottom - PlotTop + 12), JudgementColors.MISS.Darken(0.2f).WithAlpha(0.14f));
        }

        if (Run.FailedAt is double failed)
        {
            float x = XOf(failed);
            float shadedFrom = Math.Max(x, PlotLeft);

            if (PlotRight > shadedFrom)
                overlay.Box(new RectangleF(shadedFrom, PlotTop - 6, PlotRight - shadedFrom, PlotBottom - PlotTop + 12), Palette.Text.WithAlpha(0.07f));

            if (x >= PlotLeft && x <= PlotRight)
                overlay.Line(new Vector2(x, PlotTop - 6), new Vector2(x, PlotBottom + 4), 2, JudgementColors.MISS.Darken(0.2f).WithAlpha(0.85f), 6, 5);
        }

        // Zoomed in, which part of the song this is: a thumb on a track, under the plot.
        if (view.Zoom > 1 && SaysForTheOthers)
        {
            float across = PlotRight - PlotLeft;
            float y = PlotBottom + 5;

            overlay.Box(new RectangleF(PlotLeft, y, across, 4), Palette.Text.WithAlpha(0.12f));
            overlay.Box(new RectangleF(PlotLeft + (float)view.Start * across, y, across / view.Zoom, 4), Palette.Text.WithAlpha(0.5f));
        }

        MarkHovered(overlay, hoveredMark);
    }

    private Vector2? hoveredMark;

    public override bool OnMouseMove(MouseEvent e)
    {
        if (Run == null || Judging != null || reveal < 1)
            return base.OnMouseMove(e);

        Vector2 at = ToLocalSpace(e.ScreenSpaceMousePosition);

        if (at.X < LEFT - 4 || at.X > DrawWidth - RIGHT_GAP + 4)
        {
            clearHover();
            return base.OnMouseMove(e);
        }

        var described = Describe(at);
        double time = described?.Time ?? TimeAt(at.X);
        hoverTime = time;

        Vector2? mark = described?.At;

        if (mark != hoveredMark)
        {
            hoveredMark = mark;
            drawOverlay();
        }

        Tooltip.ShowAt(described?.At ?? new Vector2(at.X, PlotTop), described?.Text ?? formatPreciseTime(time), described?.Color);
        Tooltip.X = Math.Clamp(Tooltip.X, Tooltip.DrawWidth / 2, DrawWidth - Tooltip.DrawWidth / 2);

        return base.OnMouseMove(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        clearHover();
        return base.OnHoverLost(e);
    }

    private void clearHover()
    {
        hoverTime = null;
        Tooltip.Dismiss();

        if (hoveredMark != null)
        {
            hoveredMark = null;
            drawOverlay();
        }
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        if (hoverTime is not double time || WatchFrom == null)
            return base.OnClick(e);

        // A button to watch from a little before there, under where it was clicked.
        watchFrom = Math.Max(0, time - WATCH_LEAD);
        watchButton.Text = $"▶  watch from {formatTime(watchFrom)}";

        // Under the pointer, or over it where there's no room under it: the card's edge would cut it off.
        Vector2 at = ToLocalSpace(e.ScreenSpaceMousePosition);
        float height = watch.DrawHeight > 0 ? watch.DrawHeight : 34;
        float y = at.Y + 14 + height <= DrawHeight ? at.Y + 14 : at.Y - 14 - height;

        watch.Position = new Vector2(Math.Clamp(at.X, 100, DrawWidth - 100), Math.Max(0, y));
        watch.Alpha = 1;
        return true;
    }

    // Zoomed in, a drag moves along the song. All of it on show, there's nowhere to move, and a drag is the page's to scroll.
    public override bool OnDragStart(MouseButtonEvent e)
    {
        dragged = 0;
        return view.Zoom > 1;
    }

    public override bool OnDrag(MouseEvent e)
    {
        if (view.Zoom <= 1)
            return false;

        Vector2 now = ToLocalSpace(e.ScreenSpaceMousePosition);
        Vector2 before = ToLocalSpace(e.ScreenSpaceMousePosition - e.Delta);
        float moved = now.X - before.X;

        // The press offered to watch from there: moving along the song isn't taking it up on that.
        dragged += Math.Abs(moved);

        if (dragged > 4)
            dismissWatch();

        view.MoveByView(-moved / (PlotRight - PlotLeft));
        return true;
    }

    // Sideways on a trackpad moves along the song, zoomed in. Up and down is still the page's.
    public override bool OnScroll(ScrollEvent e)
    {
        Vector2 delta = e.AbsoluteDelta ?? e.ScrollDelta * 100;

        if (view.Zoom <= 1 || Math.Abs(delta.X) <= Math.Abs(delta.Y))
            return base.OnScroll(e);

        view.MoveByView(-delta.X / (PlotRight - PlotLeft));
        return true;
    }

    private void dismissWatch() => watch.Alpha = 0;

    /// <summary>
    /// Takes away the button to watch from somewhere.
    /// </summary>
    public void DismissWatch() => dismissWatch();

    /// <summary>
    /// The button offered to watch from somewhere; null while there's none.
    /// </summary>
    public PillButton? OfferedWatch => watch.Alpha > 0 ? watchButton : null;

    // m:ss.
    protected static string formatTime(double ms)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    // m:ss.ss.
    protected static string formatPreciseTime(double ms)
    {
        double seconds = Math.Max(0, ms) / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{(int)(seconds / 60)}:{seconds % 60:00.00}");
    }

    /// <summary>
    /// A line through <paramref name="points"/> wherever there's one, broken where there's none.
    /// </summary>
    protected static IEnumerable<List<Vector2>> Unbroken(IReadOnlyList<Vector2?> points)
    {
        var run = new List<Vector2>();

        foreach (Vector2? point in points)
        {
            if (point is Vector2 p)
            {
                run.Add(p);
                continue;
            }

            if (run.Count > 1)
                yield return run;

            run = new List<Vector2>();
        }

        if (run.Count > 1)
            yield return run;
    }
}
