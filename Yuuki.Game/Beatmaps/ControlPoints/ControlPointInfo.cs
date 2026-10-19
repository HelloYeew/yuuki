// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Yuuki.Game.Beatmaps.ControlPoints;

/// <summary>
/// A difficulty's timing: its <see cref="TimingPoints"/>, and the beat-grid questions everything else
/// asks of them (which tempo is in force, where the nearest beat is, which beat this is)
/// </summary>
/// <remarks>
/// Before the first timing point the first one's grid extends backwards, which is what a lead-in or
/// a count-in before the song's offset runs on. With no timing points at all, <see cref="TimingPointAt"/>
/// answers with <see cref="DEFAULT"/> and <see cref="HasTiming"/> is false; gameplay treats that as
/// "tempo unknown" and leaves its beat-synced touches out.
/// </remarks>
public class ControlPointInfo
{
    /// <summary>
    /// The divisors a beat is usually split into
    /// </summary>
    public static readonly int[] PREDEFINED_DIVISORS = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16 };

    /// <summary>
    /// What <see cref="TimingPointAt"/> answers with when there is no timing at all: 120 BPM from 0.
    /// </summary>
    public static readonly TimingPoint DEFAULT = new TimingPoint(0, 500);

    private List<TimingPoint> timingPoints = new List<TimingPoint>();

    /// <summary>
    /// The timing points, in time order. Use <see cref="Add"/> and <see cref="Remove"/> to keep them so
    /// a list set from outside (deserialization) is sorted on the way in.
    /// </summary>
    public List<TimingPoint> TimingPoints
    {
        get => timingPoints;
        set => timingPoints = value.OrderBy(t => t.Time).ToList();
    }

    [JsonIgnore]
    public bool HasTiming => timingPoints.Count > 0;

    /// <summary>
    /// The timing point in force at <paramref name="time"/>: the last one at or before it, the first
    /// one before them all, or <see cref="DEFAULT"/> when there are none.
    /// </summary>
    public TimingPoint TimingPointAt(double time)
    {
        if (timingPoints.Count == 0)
            return DEFAULT;

        int index = indexAt(time);
        return timingPoints[Math.Max(0, index)];
    }

    /// <summary>
    /// The first timing point strictly after <paramref name="time"/>, or null if none.
    /// </summary>
    public TimingPoint? TimingPointAfter(double time)
    {
        int index = indexAt(time) + 1;
        return index < timingPoints.Count ? timingPoints[index] : null;
    }

    /// <summary>
    /// Index of the last timing point at or before <paramref name="time"/>, or -1 if <paramref name="time"/>
    /// is before them all.
    /// </summary>
    private int indexAt(double time)
    {
        int low = 0;
        int high = timingPoints.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int mid = (low + high) / 2;

            if (timingPoints[mid].Time <= time)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    /// <summary>
    /// Adds a timing point in time order. One already at exactly the same time is replaced
    /// </summary>
    public void Add(TimingPoint point)
    {
        timingPoints.RemoveAll(t => t.Time.Equals(point.Time));

        int index = timingPoints.FindIndex(t => t.Time > point.Time);
        timingPoints.Insert(index < 0 ? timingPoints.Count : index, point);
    }

    public bool Remove(TimingPoint point) => timingPoints.Remove(point);

    /// <summary>
    /// Re-sorts after a point's <see cref="TimingPoint.Time"/> was changed in place.
    /// </summary>
    public void Sort() => timingPoints = timingPoints.OrderBy(t => t.Time).ToList();

    public void Clear() => timingPoints.Clear();

    #region Beats

    /// <summary>
    /// Which beat of its timing section <paramref name="time"/> is in, counted from the section's
    /// point (negative before it). Beat 0 is always a downbeat.
    /// </summary>
    public long BeatIndexAt(double time)
    {
        var point = TimingPointAt(time);
        return (long)Math.Floor((time - point.Time) / point.BeatLength + 1e-9);
    }

    /// <summary>
    /// When the beat <paramref name="time"/> is in started.
    /// </summary>
    public double BeatStartAt(double time)
    {
        var point = TimingPointAt(time);
        return point.Time + BeatIndexAt(time) * point.BeatLength;
    }

    /// <summary>
    /// Whether <paramref name="beatIndex"/> (as from <see cref="BeatIndexAt"/>) starts a bar under
    /// <paramref name="point"/>.
    /// </summary>
    public static bool IsDownbeat(TimingPoint point, long beatIndex)
    {
        int bar = point.BeatsPerBar;
        return (beatIndex % bar + bar) % bar == 0;
    }

    #endregion

    #region Snapping

    /// <summary>
    /// The time on the <paramref name="beatDivisor"/> grid nearest <paramref name="time"/>, or the next
    /// timing point if that is nearer, so snapping never skips across a tempo change.
    /// </summary>
    /// <param name="referenceTime">Snap on the grid in force here instead of at <paramref name="time"/>.</param>
    public double GetClosestSnappedTime(double time, int beatDivisor, double? referenceTime = null)
    {
        var point = TimingPointAt(referenceTime ?? time);
        double snapped = snapOnGrid(point, time, beatDivisor);

        if (referenceTime.HasValue)
            return snapped;

        var after = TimingPointAfter(time);
        return after == null || Math.Abs(time - snapped) < Math.Abs(time - after.Time) ? snapped : after.Time;
    }

    /// <summary>
    /// The smallest of <see cref="PREDEFINED_DIVISORS"/> whose grid <paramref name="time"/> sits on (or
    /// closest to): what colour to draw a note as, and what to snap to when nothing is chosen.
    /// </summary>
    public int GetClosestBeatDivisor(double time, double? referenceTime = null)
    {
        var point = TimingPointAt(referenceTime ?? time);

        int closestDivisor = PREDEFINED_DIVISORS[0];
        double closestDistance = double.MaxValue;

        foreach (int divisor in PREDEFINED_DIVISORS)
        {
            double distance = Math.Abs(time - snapOnGrid(point, time, divisor));

            // Strictly closer by more than rounding noise, so ties go to the smaller divisor.
            if (distance < closestDistance - 0.001)
            {
                closestDivisor = divisor;
                closestDistance = distance;
            }
        }

        return closestDivisor;
    }

    private static double snapOnGrid(TimingPoint point, double time, int beatDivisor)
    {
        double step = point.BeatLength / Math.Max(1, beatDivisor);
        double steps = Math.Round((time - point.Time) / step, MidpointRounding.AwayFromZero);
        return point.Time + steps * step;
    }

    #endregion

    #region BPM

    /// <summary>
    /// The beat length that lasts longest up to <paramref name="lastObjectTime"/>
    /// </summary>
    public double GetMostCommonBeatLength(double lastObjectTime)
    {
        if (timingPoints.Count == 0)
            return DEFAULT.BeatLength;

        double lastTime = lastObjectTime > 0 ? lastObjectTime : timingPoints[^1].Time;

        var mostCommon = timingPoints
                         .Select((t, i) =>
                         {
                             if (t.Time > lastTime)
                                 return (beatLength: t.BeatLength, duration: 0d);

                             double start = i == 0 ? 0 : t.Time;
                             double end = i == timingPoints.Count - 1 ? lastTime : timingPoints[i + 1].Time;
                             return (beatLength: t.BeatLength, duration: end - start);
                         })
                         // Rounded so floating-point noise doesn't split one tempo into several.
                         .GroupBy(t => Math.Round(t.beatLength * 1000) / 1000)
                         .Select(g => (beatLength: g.First().beatLength, duration: g.Sum(t => t.duration)))
                         .OrderByDescending(t => t.duration)
                         .First();

        return mostCommon.duration > 0 ? mostCommon.beatLength : timingPoints[0].BeatLength;
    }

    public double GetMostCommonBpm(double lastObjectTime) => 60000 / GetMostCommonBeatLength(lastObjectTime);

    [JsonIgnore]
    public double BpmMinimum => timingPoints.Count == 0 ? DEFAULT.Bpm : timingPoints.Min(t => t.Bpm);

    [JsonIgnore]
    public double BpmMaximum => timingPoints.Count == 0 ? DEFAULT.Bpm : timingPoints.Max(t => t.Bpm);

    #endregion

    /// <summary>
    /// A single tempo from <paramref name="offset"/>.
    /// </summary>
    public static ControlPointInfo FromBpm(double bpm, double offset = 0)
    {
        var info = new ControlPointInfo();

        if (bpm > 0)
            info.Add(new TimingPoint(offset, 60000 / bpm));

        return info;
    }

    /// <summary>
    /// Timing for a chart saved before beatmaps carried any: the set's one BPM, with a beat on the
    /// first note. That is the grid gameplay used before timing points existed, so old charts keep
    /// playing exactly as they did.
    /// </summary>
    public static ControlPointInfo FromLegacyBpm(double bpm, double firstNoteTime)
    {
        if (bpm <= 0)
            return new ControlPointInfo();

        double beatLength = 60000 / bpm;
        double phase = firstNoteTime % beatLength;

        return FromBpm(bpm, phase < 0 ? phase + beatLength : phase);
    }

    public ControlPointInfo Clone() => new ControlPointInfo { timingPoints = timingPoints.Select(t => t.Clone()).ToList() };
}
