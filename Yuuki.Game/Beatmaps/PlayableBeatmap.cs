// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Yuuki.Game.Beatmaps.ControlPoints;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// A <see cref="Beatmap"/> paired with the concrete hit objects a playfield consumes. Kept separate
/// from <see cref="Beatmap"/> (metadata only) so charts can be generated or loaded independently of
/// the beatmap database.
/// </summary>
public class PlayableBeatmap
{
    /// <summary>
    /// The metadata this chart belongs to. May be null for standalone/generated charts.
    /// </summary>
    public Beatmap? Beatmap { get; set; }

    /// <summary>The number of active lanes. 4 for the current prototype, up to 7 for the dense layout.</summary>
    public int LaneCount { get; set; } = 4;

    /// <summary>
    /// The chart's timing. Beat-synced touches (the judgement line pulse, hold ticks, the count-in)
    /// read it; with no timing points they are left out.
    /// </summary>
    public ControlPointInfo ControlPoints { get; set; } = new ControlPointInfo();

    /// <summary>
    /// The tempo that lasts longest what scroll speed follows. 0 when unknown.
    /// </summary>
    public double Bpm => ControlPoints.HasTiming ? ControlPoints.GetMostCommonBpm(LastNoteTime) : 0;

    /// <summary>
    /// The hit objects, ordered by <see cref="BeatmapNote.StartTime"/>.
    /// </summary>
    public BeatmapNote[] Notes { get; set; } = Array.Empty<BeatmapNote>();

    public PlayableBeatmap()
    {
    }

    public PlayableBeatmap(BeatmapNote[] notes, int laneCount = 4, Beatmap? beatmap = null)
    {
        Notes = notes.OrderBy(n => n.StartTime).ToArray();
        LaneCount = laneCount;
        Beatmap = beatmap;
    }

    /// <summary>
    /// Builds a playable chart from a beatmap's stored notes since the file
    /// format encodes type only by timing
    /// </summary>
    public static PlayableBeatmap FromBeatmap(Beatmap beatmap, int laneCount = 4)
    {
        var notes = beatmap.Notes.Select(n => new BeatmapNote
        {
            Lane = n.Lane,
            StartTime = n.StartTime,
            EndTime = n.EndTime,
            Type = n.EndTime > n.StartTime ? NoteType.Hold : n.Type,
            Bank = n.Bank,
            Additions = n.Additions,
            Direction = n.Direction,
            Skill = n.Skill,
            EndLane = n.EndLane
        }).ToArray();

        var chart = new PlayableBeatmap(notes, laneCount, beatmap);

        chart.ControlPoints = beatmap.ControlPoints is { HasTiming: true } timing
            ? timing.Clone()
            : ControlPointInfo.FromLegacyBpm(beatmap.BeatmapSet?.BPM ?? 0, chart.FirstNoteTime);

        return chart;
    }

    /// <summary>The start time of the first note, or 0 if the chart is empty.</summary>
    public double FirstNoteTime => Notes.Length == 0 ? 0 : Notes[0].StartTime;

    /// <summary>The end time of the last note, or 0 if the chart is empty.</summary>
    public double LastNoteTime => Notes.Length == 0 ? 0 : Notes.Max(n => n.EndTime);
}
