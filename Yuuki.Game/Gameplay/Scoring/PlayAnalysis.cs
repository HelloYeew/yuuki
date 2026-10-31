// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// One judgement where the results' graphs put it: at its note's time (a hold's release at the hold's end), its timing
/// error (none for a miss, which is drawn apart from the timing), and what it was.
/// </summary>
public readonly record struct PlayPoint(double Time, double? Error, HitResult Result, int Lane, NoteType Type, bool IsHoldTail);

/// <summary>
/// Where the run fared worst: the stretch that lost the most accuracy, from its first note to its last.
/// </summary>
public readonly record struct HardestStretch(double Start, double End, double Accuracy, int Misses);

/// <summary>
/// How one lane went: its accuracy, how early or late it was pressed on average (null with nothing pressed), and its
/// misses.
/// </summary>
public readonly record struct LaneFigures(int Lane, double Accuracy, double? MeanError, int Misses, int Count);

/// <summary>
/// How one kind of note went: how many of them were hit, of how many.
/// </summary>
public readonly record struct NoteTypeFigures(string Name, int Hit, int Total);

/// <summary>
/// What the results' More info page reads off a run's judgements: where each fell, how the timing drifted through the
/// song, health and combo as they went, where it fared worst, and how each lane and kind of note went.
/// </summary>
/// <remarks>Worked out once, from <see cref="GameplayResult.Judgements"/> and <see cref="GameplayResult.HoldBreakTimes"/>.</remarks>
public class PlayAnalysis
{
    /// <summary>
    /// How far apart the drift's points are, and how far either side of each its presses come from, in ms.
    /// </summary>
    public const double DRIFT_STEP = 1000;

    public const double DRIFT_REACH = 4000;

    /// <summary>
    /// The fewest presses a drift point is worked out from; with fewer there is none.
    /// </summary>
    public const int DRIFT_MINIMUM = 4;

    /// <summary>
    /// How far apart the note density's points are, in ms: each counts the notes within this either side.
    /// </summary>
    public const double DENSITY_STEP = 500;

    /// <summary>
    /// How long the hardest stretch is at most, in ms.
    /// </summary>
    public const double HARDEST_LENGTH = 8000;

    public GameplayResult Result { get; }

    /// <summary>
    /// The span the graphs cover, in ms: the chart's length.
    /// </summary>
    public double Length { get; }

    public IReadOnlyList<PlayPoint> Points { get; }

    /// <summary>
    /// The presses' timing errors, as the timing statistics take them (<see cref="GameplayResult.AffectsTimingStatistics"/>).
    /// </summary>
    public IReadOnlyList<double> TimingErrors { get; }

    /// <summary>
    /// Every <see cref="DRIFT_STEP"/> ms from the start, the presses' mean error round it (null where there were too few).
    /// </summary>
    public IReadOnlyList<double?> Drift { get; }

    /// <summary>
    /// Every <see cref="DENSITY_STEP"/> ms from the start, how many notes a second came round it.
    /// </summary>
    public IReadOnlyList<float> Density { get; }

    /// <summary>
    /// Health as it went (0–1), from 1 at the start: a point at each judgement.
    /// </summary>
    public IReadOnlyList<(double Time, double Health)> Health { get; }

    /// <summary>
    /// The combo as it went: a point at each judgement and each hold broken.
    /// </summary>
    public IReadOnlyList<(double Time, int Combo)> Combo { get; }

    /// <summary>
    /// Where the combo broke, by a miss or a hold let go of.
    /// </summary>
    public IReadOnlyList<double> ComboBreaks { get; }

    public int MaxCombo { get; }

    /// <summary>
    /// Where health ran out; null where it didn't.
    /// </summary>
    public double? FailedAt { get; }

    /// <summary>
    /// The stretch of up to <see cref="HARDEST_LENGTH"/> that lost the most (a Break a third of a note, a Hit two, a miss
    /// all of one; a Pass note less), from its first note to its last; null where nothing was lost.
    /// </summary>
    public HardestStretch? Hardest { get; }

    public IReadOnlyList<LaneFigures> Lanes { get; }

    /// <summary>
    /// The kinds of note the chart had, in the order the game teaches them; a hold's release apart from its press.
    /// </summary>
    public IReadOnlyList<NoteTypeFigures> NoteTypes { get; }

    /// <summary>
    /// Presses outside the Critical window, early and late: what was off, and which way.
    /// </summary>
    public int EarlyOff { get; }

    public int LateOff { get; }

    /// <summary>
    /// The most Criticals (or Blooms) in a row.
    /// </summary>
    public int LongestCriticalStreak { get; }

    /// <summary>
    /// The presses' standard deviation, in ms; null with nothing pressed.
    /// </summary>
    public double? StandardDeviation => Result.UnstableRate / 10;

    public PlayAnalysis(GameplayResult result)
    {
        Result = result;

        IReadOnlyList<JudgementResult> judgements = result.Judgements;

        Points = judgements.Select(j => new PlayPoint(
            noteTime(j),
            j.Result.IsHit() ? j.TimingError : null,
            j.Result,
            (int)j.Note.Lane,
            j.Note.Type,
            j.IsHoldTail)).ToArray();

        Length = result.ChartLength > 0 ? result.ChartLength : Points.Count > 0 ? Points.Max(p => p.Time) : 0;

        var timed = judgements.Where(GameplayResult.AffectsTimingStatistics).Select(j => (Time: noteTime(j), Error: j.TimingError)).ToArray();
        TimingErrors = timed.Select(t => t.Error).ToArray();

        Drift = drift(timed, Length);
        Density = density(Points, Length);

        (Health, Combo, ComboBreaks, MaxCombo, FailedAt) = walk(result);

        Hardest = hardest(Points);
        Lanes = lanes(Points, result.LaneCount);
        NoteTypes = noteTypes(Points);

        EarlyOff = timed.Count(t => t.Error < -result.HitWindows.Critical);
        LateOff = timed.Count(t => t.Error > result.HitWindows.Critical);

        int streak = 0;

        foreach (JudgementResult judgement in judgements)
        {
            streak = judgement.Result.IsCritical() ? streak + 1 : 0;
            LongestCriticalStreak = Math.Max(LongestCriticalStreak, streak);
        }
    }

    /// <summary>
    /// Where a judgement goes on the song's time: its note's, or for a hold's release, the hold's end.
    /// </summary>
    private static double noteTime(JudgementResult judgement) => judgement.IsHoldTail ? judgement.Note.EndTime : judgement.Note.StartTime;

    /// <summary>
    /// When a judgement happened: a hit as it was pressed (or let go of), a miss as its window closed.
    /// </summary>
    private static double judgedAt(JudgementResult judgement, HitWindows windows) =>
        noteTime(judgement) + (judgement.Result.IsHit() ? judgement.TimingError : judgement.IsHoldTail ? windows.ReleaseWindow : windows.MissWindow);

    private static IReadOnlyList<double?> drift((double Time, double Error)[] timed, double length)
    {
        var drift = new List<double?>();

        for (double at = 0; at <= length; at += DRIFT_STEP)
        {
            double sum = 0;
            int count = 0;

            foreach ((double time, double error) in timed)
            {
                if (Math.Abs(time - at) > DRIFT_REACH)
                    continue;

                sum += error;
                count++;
            }

            drift.Add(count >= DRIFT_MINIMUM ? sum / count : null);
        }

        return drift;
    }

    private static IReadOnlyList<float> density(IReadOnlyList<PlayPoint> points, double length)
    {
        var density = new List<float>();
        double[] presses = points.Where(p => !p.IsHoldTail).Select(p => p.Time).ToArray();

        for (double at = 0; at <= length; at += DENSITY_STEP)
            density.Add(presses.Count(t => t >= at - DENSITY_STEP && t < at + DENSITY_STEP) / (float)(2 * DENSITY_STEP / 1000));

        return density;
    }

    // Health and combo as they went, through the judgements and the holds broken, in the order they happened.
    private static (IReadOnlyList<(double, double)>, IReadOnlyList<(double, int)>, IReadOnlyList<double>, int, double?) walk(GameplayResult result)
    {
        var healthProcessor = new HealthProcessor();
        var health = new List<(double, double)> { (0, 1) };
        var combo = new List<(double, int)> { (0, 0) };
        var breaks = new List<double>();
        double? failedAt = null;
        double latest = 0;
        int current = 0, max = 0, broken = 0;

        foreach (JudgementResult judgement in result.Judgements)
        {
            // In the order they happened: a miss is judged a little after its window closes, which can be after a hit
            // that follows it.
            double time = latest = Math.Max(latest, judgedAt(judgement, result.HitWindows));

            // The holds let go of before it.
            for (; broken < result.HoldBreakTimes.Count && result.HoldBreakTimes[broken] <= time; broken++)
                breakCombo(result.HoldBreakTimes[broken]);

            healthProcessor.Apply(judgement);
            health.Add((time, healthProcessor.Health.Value));

            if (healthProcessor.IsFailed)
                failedAt ??= time;

            if (judgement.Result.IsHit())
            {
                max = Math.Max(max, ++current);
                combo.Add((time, current));
            }
            else
                breakCombo(time);
        }

        for (; broken < result.HoldBreakTimes.Count; broken++)
            breakCombo(result.HoldBreakTimes[broken]);

        return (health, combo, breaks, max, failedAt);

        void breakCombo(double time)
        {
            if (current > 0)
                breaks.Add(time);

            current = 0;
            combo.Add((time, 0));
        }
    }

    private static HardestStretch? hardest(IReadOnlyList<PlayPoint> points)
    {
        // Every stretch starts at a note (one starting anywhere else holds no more), and takes the notes up to
        // HARDEST_LENGTH on: in time order, with running totals, so each is a subtraction.
        PlayPoint[] sorted = points.OrderBy(p => p.Time).ToArray();
        int count = sorted.Length;
        double[] weight = new double[count + 1], value = new double[count + 1];
        int[] misses = new int[count + 1];

        for (int i = 0; i < count; i++)
        {
            double w = sorted[i].Type == NoteType.Pass ? JudgementWeights.PASS_WEIGHT : 1;
            weight[i + 1] = weight[i] + w;
            value[i + 1] = value[i] + w * sorted[i].Result.Value();
            misses[i + 1] = misses[i] + (sorted[i].Result == HitResult.Miss ? 1 : 0);
        }

        HardestStretch? hardest = null;
        double mostLost = 0;
        int last = 0;

        for (int first = 0; first < count; first++)
        {
            // Past the stretch's last note.
            last = Math.Max(last, first + 1);

            while (last < count && sorted[last].Time < sorted[first].Time + HARDEST_LENGTH)
                last++;

            double w = weight[last] - weight[first], v = value[last] - value[first];
            double lost = w - v;

            if (lost <= mostLost + 1e-9)
                continue;

            mostLost = lost;
            hardest = new HardestStretch(sorted[first].Time, sorted[last - 1].Time, v / w, misses[last] - misses[first]);
        }

        return hardest;
    }

    private static IReadOnlyList<LaneFigures> lanes(IReadOnlyList<PlayPoint> points, int laneCount)
    {
        if (laneCount <= 0)
            laneCount = points.Count > 0 ? points.Max(p => p.Lane) + 1 : 0;

        return Enumerable.Range(0, laneCount).Select(lane =>
        {
            PlayPoint[] inLane = points.Where(p => p.Lane == lane).ToArray();
            double weight = 0, value = 0;

            foreach (PlayPoint point in inLane)
            {
                double w = point.Type == NoteType.Pass ? JudgementWeights.PASS_WEIGHT : 1;
                weight += w;
                value += w * point.Result.Value();
            }

            double[] errors = inLane.Where(p => p.Error is not null && !p.IsHoldTail && p.Type is not (NoteType.Pass or NoteType.Touch))
                                    .Select(p => p.Error!.Value).ToArray();

            return new LaneFigures(lane, weight > 0 ? value / weight : 1, errors.Length > 0 ? errors.Average() : null, inLane.Count(p => p.Result == HitResult.Miss), inLane.Length);
        }).ToArray();
    }

    private static IReadOnlyList<NoteTypeFigures> noteTypes(IReadOnlyList<PlayPoint> points) =>
        points.GroupBy(p => (p.Type, p.IsHoldTail))
              .OrderBy(g => g.Key.Type).ThenBy(g => g.Key.IsHoldTail)
              .Select(g => new NoteTypeFigures(g.Key.IsHoldTail ? "Release" : nameOf(g.Key.Type), g.Count(p => p.Result.IsHit()), g.Count()))
              .ToArray();

    private static string nameOf(NoteType type) => type switch
    {
        NoteType.SwipeBullet => "Swipe bullet",
        _ => type.ToString()
    };
}
