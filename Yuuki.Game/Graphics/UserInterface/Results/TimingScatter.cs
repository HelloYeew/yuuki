// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// Every press of the run where it fell in the song and how early or late it was, over the judgements' windows: a dot of
/// its judgement's color (a ring for a hold's release), misses as ticks along the top, the notes a second under it all,
/// and a line of how the timing drifted. It answers "where did I drift?".
/// </summary>
/// <remarks>
/// The judgements and lanes shown can be picked (<see cref="Shows"/>, <see cref="Lanes"/>), one lane brought out
/// (<see cref="BroughtOut"/>), and another run (the best) ghosted under it to compare (<see cref="Compare"/>). Zoomed in, it
/// shows less of how early or late as well as less of the song, as the presses crowd round on time: ones past what's shown
/// stay on its edge.
/// </remarks>
public partial class TimingScatter : SongTimeGraph
{
    /// <summary>
    /// Its height until it's given another: it can be any, and whatever it has over this goes to the timing.
    /// </summary>
    public const float HEIGHT = 336;

    // Down the graph: the misses' row and the timing from its top, the density and the time labels from its foot.
    private const float miss_top = 10;
    private const float timing_top = 34;

    private float timingBottom => DrawHeight - 54;

    private float densityTop => DrawHeight - 44;

    private float densityBottom => DrawHeight - 26;

    /// <summary>
    /// The notes a second the density is drawn full height at.
    /// </summary>
    private const float density_full = 11;

    /// <summary>
    /// How big a press's dot is: bigger zoomed in, where there's room.
    /// </summary>
    private float dotRadius => View.Level == 0 ? 3 : View.Level == 1 ? 3.5f : 4;

    private readonly HashSet<HitResult> shown = new HashSet<HitResult> { HitResult.Bloom, HitResult.Critical, HitResult.Break, HitResult.Hit, HitResult.Miss };
    private readonly HashSet<int> lanes = new HashSet<int>();

    private PlayAnalysis? best;
    private bool compare;
    private int? broughtOut;

    protected override float PlotTop => miss_top;

    protected override float PlotBottom => densityBottom;

    public TimingScatter(YuukiColorPalette palette)
        : base(palette, HEIGHT)
    {
    }

    /// <summary>
    /// The judgements shown (all to begin with).
    /// </summary>
    public IReadOnlySet<HitResult> Shows => shown;

    /// <summary>
    /// The lanes picked to show: none picked shows them all.
    /// </summary>
    public IReadOnlySet<int> Lanes => lanes;

    public void Show(HitResult result, bool show)
    {
        if (show ? shown.Add(result) : shown.Remove(result))
            Replot();
    }

    public void ShowLane(int lane, bool show)
    {
        if (show ? lanes.Add(lane) : lanes.Remove(lane))
            Replot();
    }

    /// <summary>
    /// A lane to bring out, the rest dimmed (as its card is pointed at); null for none.
    /// </summary>
    public int? BroughtOut
    {
        get => broughtOut;
        set
        {
            if (broughtOut == value)
                return;

            broughtOut = value;
            Replot();
        }
    }

    /// <summary>
    /// The run to compare with (the best), ghosted under this one when <see cref="Compare"/> is on.
    /// </summary>
    public void SetBest(PlayAnalysis? run)
    {
        best = run;
        Replot();
    }

    public bool Compare
    {
        get => compare;
        set
        {
            if (compare == value)
                return;

            compare = value;
            Replot();
        }
    }

    private bool visible(in PlayPoint point) => shown.Contains(point.Result) && (lanes.Count == 0 || lanes.Contains(point.Lane));

    private float yOf(double error) => (timing_top + timingBottom) / 2 - (float)(Math.Clamp(error, -range, range) / range) * (timingBottom - timing_top) / 2;

    // The timing drawn: out to where a press can still take a note, or zoomed in, to what a Break or a Critical is.
    private double range
    {
        get
        {
            HitWindows windows = Run?.Result.HitWindows ?? new HitWindows();
            return View.Level == 0 ? windows.MissWindow : View.Level == 1 ? windows.Break : windows.Critical;
        }
    }

    protected override void Plot()
    {
        PlayAnalysis run = Run!;
        HitWindows windows = run.Result.HitWindows;
        float left = PlotLeft, right = PlotRight;

        // The windows, widest first: a band each side of on time, as far as it's shown.
        foreach ((double from, double to, Color color, float alpha) in new[]
                 {
                     (windows.Hit, windows.MissWindow, JudgementColors.MISS, 0.10f),
                     (windows.Break, windows.Hit, JudgementColors.HIT, 0.11f),
                     (windows.Critical, windows.Break, JudgementColors.BREAK, 0.12f),
                     (windows.Bloom, windows.Critical, JudgementColors.CRITICAL, 0.17f),
                     (0, windows.Bloom, JudgementColors.BLOOM, 0.20f)
                 })
        {
            if (from >= range)
                continue;

            Backdrop.Box(new RectangleF(left, yOf(to), right - left, yOf(from) - yOf(to)), color.WithAlpha(alpha));
            Backdrop.Box(new RectangleF(left, yOf(-from), right - left, yOf(-to) - yOf(-from)), color.WithAlpha(alpha));
        }

        Backdrop.Line(new Vector2(left, yOf(0)), new Vector2(right, yOf(0)), 1.5f, Palette.Text.WithAlpha(0.32f));

        SideLabel("0", yOf(0));

        // Bloom's has room once it's zoomed in.
        foreach (double at in View.Level == 0 ? new[] { windows.Hit, windows.Break, windows.Critical } : new[] { windows.Hit, windows.Break, windows.Critical, windows.Bloom })
        {
            if (at > range)
                continue;

            SideLabel($"+{at:0}", yOf(at));
            SideLabel($"−{at:0}", yOf(-at));
        }

        SideLabel("miss", miss_top + 8, JudgementColors.MISS.WithAlpha(0.9f), 12);
        SideLabel("notes/s", (densityTop + densityBottom) / 2, size: 12);
        PlotLabel("late ↑", new Vector2(left + 8, timing_top + 10));
        PlotLabel("early ↓", new Vector2(left + 8, timingBottom - 10));

        // How many notes a second, under it all.
        float foot = densityBottom, rise = densityBottom - densityTop;
        var density = run.Density.Select((rate, i) => new Vector2(XOf(i * PlayAnalysis.DENSITY_STEP), foot - rise * Math.Min(1, rate / density_full))).ToList();
        Marks.Area(density, foot, Palette.Text.WithAlpha(0.16f));

        // The best, ghosted under this run.
        if (compare && best != null)
        {
            foreach (PlayPoint point in best.Points)
            {
                if (point.Error is double error && visible(point))
                    Marks.Dot(new Vector2(XOf(point.Time), yOf(error)), 2.2f, Palette.Text.WithAlpha(0.28f));
            }
        }

        foreach (PlayPoint point in run.Points)
        {
            if (!visible(point))
                continue;

            float x = XOf(point.Time);

            // Off the plot, zoomed in.
            if (x < left - dotRadius * 2 || x > right + dotRadius * 2)
                continue;

            float alpha = broughtOut is int lane && point.Lane != lane ? 0.12f : 1;
            Color color = JudgementColors.MarkFor(point.Result).WithAlpha(alpha);

            if (point.Error is not double error)
                Marks.Line(new Vector2(x, miss_top), new Vector2(x, miss_top + 14), 2.5f, color);
            else if (point.IsHoldTail)
                Marks.Ring(new Vector2(x, yOf(error)), dotRadius + 0.1f, 1.7f, color);
            else
                Marks.Dot(new Vector2(x, yOf(error)), dotRadius, color);
        }

        // How the timing drifted: the best's dashed, this run's over it.
        if (compare && best != null)
            drift(best, Palette.Text.WithAlpha(0.55f), 6, 5);

        drift(run, Palette.Text, 0, 0);
    }

    private void drift(PlayAnalysis run, Color color, float dash, float gap)
    {
        var points = run.Drift.Select((mean, i) => mean is double m ? new Vector2(XOf(i * PlayAnalysis.DRIFT_STEP), yOf(Math.Clamp(m, -range, range))) : (Vector2?)null).ToList();

        foreach (List<Vector2> line in Unbroken(points))
            Marks.Line(line, 2.6f, color, dash, gap);
    }

    protected override (string Text, Color? Color, Vector2 At, double Time)? Describe(Vector2 at)
    {
        PlayAnalysis run = Run!;
        PlayPoint? nearest = null;
        Vector2 nearestAt = default;
        float closest = 14;

        foreach (PlayPoint point in run.Points)
        {
            if (!visible(point))
                continue;

            var where = new Vector2(XOf(point.Time), point.Error is double error ? yOf(error) : miss_top + 7);

            if (where.X < PlotLeft || where.X > PlotRight)
                continue;

            float distance = Vector2.Distance(where, at);

            if (distance >= closest)
                continue;

            closest = distance;
            nearest = point;
            nearestAt = where;
        }

        if (nearest is not PlayPoint found)
            return null;

        string timing = found.Error is double e ? $"{Math.Abs(e):0} ms {(e >= 0 ? "late" : "early")}" : "no press";
        string kind = found.IsHoldTail ? "Release" : found.Type == NoteType.SwipeBullet ? "Swipe bullet" : found.Type.ToString();

        return ($"{formatPreciseTime(found.Time)} · lane {found.Lane + 1} · {kind} · {found.Result}, {timing}", JudgementColors.For(found.Result), nearestAt, found.Time);
    }

    protected override void MarkHovered(PlotCanvas canvas, Vector2? at)
    {
        if (at is Vector2 point && point.Y > timing_top - 2)
            canvas.Ring(point, 7, 2.5f, ColorExtensions.FromHex("#1B1622"));
    }
}
