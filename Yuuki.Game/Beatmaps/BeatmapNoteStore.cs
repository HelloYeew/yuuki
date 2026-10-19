// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// Loads a <see cref="Beatmap"/>'s hit objects from storage on demand and drops them again when the
/// play is over.
/// </summary>
public class BeatmapNoteStore
{
    private readonly Storage storage;

    public BeatmapNoteStore(Storage storage)
    {
        this.storage = storage;
    }

    /// <summary>
    /// Ensures <paramref name="beatmap"/> has its notes loaded and returns them.
    /// </summary>
    /// <remarks>
    /// Beatmaps that already carry notes in memory (generated charts, the bundled theme, anything
    /// built by test utilities) are returned as they are — they have no file to read.
    /// </remarks>
    /// <returns>The beatmap's notes, empty if it has none or could not be read.</returns>
    public BeatmapNote[] Load(Beatmap beatmap)
    {
        if (beatmap.IsNull())
            return Array.Empty<BeatmapNote>();

        if (beatmap.Notes.Length > 0)
            return beatmap.Notes;

        if (string.IsNullOrEmpty(beatmap.NotesStoragePath))
            return Array.Empty<BeatmapNote>();

        beatmap.Notes = read(beatmap.NotesStoragePath);
        return beatmap.Notes;
    }

    /// <summary>
    /// Drops the notes loaded by <see cref="Load"/>, leaving the beatmap's metadata intact.
    /// </summary>
    /// <remarks>
    /// A no-op for beatmaps whose notes came from memory rather than from storage: clearing those
    /// would lose the only copy.
    /// </remarks>
    public void Unload(Beatmap beatmap)
    {
        if (beatmap.IsNull() || string.IsNullOrEmpty(beatmap.NotesStoragePath))
            return;

        beatmap.Notes = Array.Empty<BeatmapNote>();
    }

    /// <summary>
    /// The beatmap's <see cref="BeatmapNoteSummary"/>, computed on first use and cached on the beatmap.
    /// Safe to call off the update thread: it reads the chart into a local array and never touches
    /// <see cref="Beatmap.Notes"/>, so it can't race a play loading or unloading the same beatmap.
    /// </summary>
    public BeatmapNoteSummary GetSummary(Beatmap beatmap)
    {
        if (beatmap.IsNull())
            return BeatmapNoteSummary.EMPTY;

        if (beatmap.NoteSummary.IsNotNull())
            return beatmap.NoteSummary;

        BeatmapNote[] notes = beatmap.Notes;

        if (notes.Length == 0 && !string.IsNullOrEmpty(beatmap.NotesStoragePath))
            notes = read(beatmap.NotesStoragePath);

        return beatmap.NoteSummary = BeatmapNoteSummary.From(notes);
    }

    private BeatmapNote[] read(string path)
    {
        try
        {
            if (!storage.Exists(path))
            {
                Logger.Warning($"Beatmap file missing while loading notes: {path}");
                return Array.Empty<BeatmapNote>();
            }

            using Stream stream = storage.GetStream(path);

            return stream.IsNull() ? Array.Empty<BeatmapNote>() : BeatmapUtility.DeserializeNotes(stream);
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to load notes for beatmap file: {path}", e);
            return Array.Empty<BeatmapNote>();
        }
    }
}
