// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// A small digest of a chart for display (song select Detail tab, the setup screen): note counts by
/// kind, the max combo, and how the notes are spread over the song. Built once from the notes and
/// cached on <see cref="Beatmap.NoteSummary"/>, so showing it never keeps the chart itself in memory.
/// </summary>
public class BeatmapNoteSummary
{
    /// <summary>
    /// Columns in <see cref="Density"/>.
    /// </summary>
    public const int DENSITY_COLUMNS = 60;

    public static readonly BeatmapNoteSummary EMPTY = From(Array.Empty<BeatmapNote>());

    /// <summary>
    /// Single-judgment normal notes: taps, plus Pass and Skill notes.
    /// </summary>
    public int TapCount { get; private init; }
    public int HoldCount { get; private init; }
    public int SwipeCount { get; private init; }

    /// <summary>
    /// Touch notes (VOEZ-style: keep the lane held as they pass).
    /// </summary>
    public int TouchCount { get; private init; }

    /// <summary>
    /// Boss-phase bullets of every kind. Always 0 until the boss phase exists.
    /// </summary>
    public int BulletCount { get; private init; }

    public int TotalNotes { get; private init; }

    /// <summary>
    /// Normal-phase notes that are pressed: taps, holds, and swipes.
    /// </summary>
    public int NormalCount => TapCount + HoldCount + SwipeCount;

    /// <summary>
    /// The highest reachable combo: one per note, plus one more per hold for its release.
    /// </summary>
    public int MaxCombo => TotalNotes + HoldCount;

    /// <summary>
    /// The most notes starting within any one second.
    /// </summary>
    public int PeakNotesPerSecond { get; private init; }

    /// <summary>
    /// Notes per column across the chart, normalized so the busiest column is 1.
    /// </summary>
    public float[] Density { get; private init; } = Array.Empty<float>();

    /// <summary>
    /// Whether there are no notes (gameplay will generate a chart instead).
    /// </summary>
    public bool IsEmpty => TotalNotes == 0;

    public static BeatmapNoteSummary From(BeatmapNote[] notes)
    {
        int taps = 0, holds = 0, swipes = 0, bullets = 0, touches = 0;

        foreach (BeatmapNote note in notes)
        {
            switch (note.Type)
            {
                case NoteType.Bullet:
                case NoteType.SwipeBullet:
                    bullets++;
                    break;

                case NoteType.Swipe:
                    swipes++;
                    break;

                case NoteType.Touch:
                    touches++;
                    break;

                // Matches PlayableBeatmap.FromBeatmap: any note with a duration plays as a hold.
                case NoteType.Hold:
                    holds++;
                    break;

                default:
                    if (note.EndTime > note.StartTime)
                        holds++;
                    else
                        taps++;
                    break;
            }
        }

        double[] starts = notes.Select(n => n.StartTime).OrderBy(t => t).ToArray();

        return new BeatmapNoteSummary
        {
            TapCount = taps,
            HoldCount = holds,
            SwipeCount = swipes,
            BulletCount = bullets,
            TouchCount = touches,
            TotalNotes = notes.Length,
            PeakNotesPerSecond = peakPerSecond(starts),
            Density = density(starts)
        };
    }

    private static int peakPerSecond(double[] sortedStarts)
    {
        int peak = 0;

        for (int first = 0, last = 0; last < sortedStarts.Length; last++)
        {
            while (sortedStarts[last] - sortedStarts[first] >= 1000)
                first++;

            peak = Math.Max(peak, last - first + 1);
        }

        return peak;
    }

    private static float[] density(double[] sortedStarts)
    {
        if (sortedStarts.Length == 0)
            return Array.Empty<float>();

        float[] columns = new float[DENSITY_COLUMNS];
        double span = Math.Max(1, sortedStarts[^1]);

        foreach (double start in sortedStarts)
            columns[Math.Clamp((int)(start / span * DENSITY_COLUMNS), 0, DENSITY_COLUMNS - 1)]++;

        float max = columns.Max();
        for (int i = 0; i < columns.Length; i++)
            columns[i] /= max;

        return columns;
    }
}
