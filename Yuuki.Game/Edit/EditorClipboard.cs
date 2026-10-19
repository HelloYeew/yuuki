// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Edit;

/// <summary>
/// The editor's own clipboard for notes: kept by the game rather than the editor, so notes
/// copied in one difficulty paste into another.
/// </summary>
public class EditorClipboard
{
    /// <summary>
    /// The copied notes as JSON, or null when nothing has been copied.
    /// </summary>
    public string? Content { get; private set; }

    public bool HasContent => !string.IsNullOrEmpty(Content);

    public void Copy(IEnumerable<BeatmapNote> notes)
    {
        var copied = notes.Select(n => n.Clone()).OrderBy(n => n.StartTime).ToArray();

        if (copied.Length > 0)
            Content = JsonSerializer.Serialize(copied);
    }

    /// <summary>
    /// Fresh copies of what was copied, shifted so the first starts at <paramref name="startTime"/>.
    /// Notes in lanes the beatmap doesn't have (copied from a difficulty with more) are left out.
    /// </summary>
    public BeatmapNote[] Paste(double startTime, int laneCount)
    {
        if (!HasContent)
            return Array.Empty<BeatmapNote>();

        BeatmapNote[] notes;

        try
        {
            notes = JsonSerializer.Deserialize<BeatmapNote[]>(Content!) ?? Array.Empty<BeatmapNote>();
        }
        catch (JsonException)
        {
            return Array.Empty<BeatmapNote>();
        }

        if (notes.Length == 0)
            return notes;

        double offset = startTime - notes.Min(n => n.StartTime);

        foreach (var note in notes)
        {
            note.StartTime += offset;
            note.EndTime += offset;
        }

        return notes.Where(n => (int)n.Lane < laneCount && (n.EndLane is not NoteLane end || (int)end < laneCount)).ToArray();
    }

    public static string Timestamp(double time, IReadOnlyList<BeatmapNote> selection)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, time));
        string text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}:{span.Milliseconds:000}";

        if (selection.Count == 0)
            return text + " - ";

        double start = selection.Min(n => n.StartTime);
        span = TimeSpan.FromMilliseconds(Math.Max(0, start));
        text = $"{(int)span.TotalMinutes:00}:{span.Seconds:00}:{span.Milliseconds:000}";

        // TODO: This is from osu!mania, maybe change?
        return $"{text} ({string.Join(',', selection.OrderBy(n => n.StartTime).Select(n => $"{n.StartTime:0}|{(int)n.Lane}"))}) - ";
    }
}
