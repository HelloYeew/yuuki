// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Yuuki.Game.Beatmaps.ControlPoints;

namespace Yuuki.Game.Beatmaps;

public class Beatmap
{
    /// <summary>
    /// The file format this was written in:
    /// <list type="bullet">
    /// <item>Version 1 has no timing of its own and plays on its set's BPM (see <see cref="ControlPointInfo.FromLegacyBpm"/>).</item>
    /// <item>Version 2 added <see cref="ControlPoints"/>.</item>
    /// </list>
    /// </summary>
    public const int CURRENT_VERSION = 2;

    public int Version { get; set; } = CURRENT_VERSION;

    public int ID { get; set; }

    public BeatmapSet BeatmapSet { get; set; }

    public string Creator { get; set; }

    public string DifficultyName { get; set; }

    // TODO: this should not be here..., yeah it should be here BUT in game should calculate it live
    public double DifficultyRating { get; set; }

    /// <summary>
    /// How many lanes the chart is played on (4 to 8). 4 when the file doesn't say.
    /// </summary>
    public int LaneCount { get; set; } = 4;

    public string BackgroundPath { get; set; }

    /// <summary>
    /// This difficulty's timing, per difficulty. Empty for a chart saved before timing
    /// points existed. Small, so it is read with the metadata rather than with the notes.
    /// </summary>
    public ControlPointInfo ControlPoints { get; set; } = new ControlPointInfo();

    /// <summary>
    /// The hit objects, as stored in the beatmap file. Times are in ms on the audio timeline; a note
    /// with <see cref="BeatmapNote.EndTime"/> greater than its <see cref="BeatmapNote.StartTime"/> is
    /// a hold. Empty for generated/metadata-only beatmaps.
    /// </summary>
    /// <remarks>
    /// Beatmaps read from storage arrive here with no notes: the library scan reads metadata only and
    /// records <see cref="NotesStoragePath"/> instead, because keeping every difficulty's chart
    /// resident costs footprint proportional to the whole library while only one is ever played at a
    /// time. Use <see cref="BeatmapNoteStore"/> to fill this in for a play and clear it afterward.
    /// </remarks>
    public BeatmapNote[] Notes { get; set; } = Array.Empty<BeatmapNote>();

    /// <summary>
    /// The storage-relative path of the beatmap file this was read from (e.g.
    /// "beatmaps/1 Artist - Title/1 Artist - Title [Basic].ykb"), used to load
    /// <see cref="Notes"/> on demand. Null for beatmaps that were never read from storage, whose
    /// notes (if any) are already in memory. Not serialized.
    /// </summary>
    [JsonIgnore]
    public string NotesStoragePath { get; set; }

    /// <summary>
    /// The chart's digest for display, cached by <see cref="BeatmapNoteStore.GetSummary"/> the first
    /// time it's shown. Tiny, so it stays resident where the notes don't. Not serialized.
    /// </summary>
    [JsonIgnore]
    public BeatmapNoteSummary NoteSummary { get; set; }

    public override string ToString()
    {
        return $"{BeatmapSet.Title} [{DifficultyName}] by {Creator} ({DifficultyRating:0.00})";
    }

    public override bool Equals(object? obj)
    {
        return obj is Beatmap beatmap &&
               ID == beatmap.ID &&
               EqualityComparer<BeatmapSet>.Default.Equals(BeatmapSet, beatmap.BeatmapSet) &&
               Creator == beatmap.Creator &&
               DifficultyName == beatmap.DifficultyName &&
               DifficultyRating.Equals(beatmap.DifficultyRating) &&
               BackgroundPath == beatmap.BackgroundPath;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ID, BeatmapSet, Creator, DifficultyName, DifficultyRating, BackgroundPath);
    }

    public Beatmap Clone()
    {
        return (Beatmap)MemberwiseClone();
    }
}
