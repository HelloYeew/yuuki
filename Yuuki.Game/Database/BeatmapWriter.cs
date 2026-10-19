// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.Difficulty;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Database;

/// <summary>
/// Writes beatmaps and sets back into the library: what the editor saves through. The counterpart
/// of <see cref="BeatmapCollectionReader"/>, writing the same files it reads, into the folder it read
/// them from.
/// </summary>
/// <remarks>
/// Every write goes to a temporary file first and is moved into place, so a crash mid-save leaves the
/// old file rather than half a chart. The in-memory <see cref="BeatmapsCollection"/> is kept in step,
/// so song select shows the saved state without a rescan.
/// </remarks>
public class BeatmapWriter
{
    private readonly Storage storage;
    private readonly BeatmapsCollection collection;

    /// <param name="storage">The game's root storage, the one holding the beatmaps folder.</param>
    /// <param name="collection">The library to keep in step with what is written.</param>
    public BeatmapWriter(Storage storage, BeatmapsCollection collection)
    {
        this.storage = storage;
        this.collection = collection;
    }

    /// <summary>
    /// Saves a difficulty, notes and timing included, and puts it in the library in place of any
    /// difficulty with the same <see cref="Beatmap.ID"/>.
    /// </summary>
    /// <remarks>
    /// The chart is tidied the way the rest of the game expects a stored one to be (notes in time
    /// order, lanes in range, the current file version) and re-rated, since a changed chart has a
    /// changed difficulty. The set's file is not touched; save it with <see cref="Save(BeatmapSet)"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The beatmap belongs to no set.</exception>
    public void Save(Beatmap beatmap)
    {
        BeatmapSet set = beatmap.BeatmapSet ?? throw new InvalidOperationException("A beatmap has to belong to a set to be saved.");

        beatmap.Version = Beatmap.CURRENT_VERSION;
        beatmap.LaneCount = LaneKeyLayouts.Clamp(beatmap.LaneCount);
        beatmap.Notes = beatmap.Notes.OrderBy(n => n.StartTime).ThenBy(n => n.Lane).ToArray();
        beatmap.ControlPoints.Sort();
        // TODO: replace with our version
        beatmap.DifficultyRating = OsuManiaStarRating.Calculate(beatmap.Notes, beatmap.LaneCount);
        beatmap.NoteSummary = null;

        string path = $"{folderPathOf(set)}/{BeatmapUtility.GetBeatmapFileName(beatmap)}.ykb";

        write(path, beatmap);

        // Renaming a difficulty renames its file. The old one would otherwise be read back next launch
        // as a second copy of the same difficulty.
        if (!string.IsNullOrEmpty(beatmap.NotesStoragePath) && beatmap.NotesStoragePath != path && storage.Exists(beatmap.NotesStoragePath))
            storage.Delete(beatmap.NotesStoragePath);

        beatmap.NotesStoragePath = path;

        int existing = collection.Beatmaps.FindIndex(b => b.ID == beatmap.ID);

        if (existing >= 0)
            collection.Beatmaps[existing] = beatmap;
        else
            collection.Beatmaps.Add(beatmap);
    }

    /// <summary>
    /// Saves a set's metadata, and puts it in the library in place of any set with the same
    /// <see cref="BeatmapSet.ID"/>.
    /// </summary>
    public void Save(BeatmapSet set)
    {
        string folder = folderPathOf(set);
        string fileName = $"{BeatmapSetUtility.GetBeatmapSetFileName(set)}.yks";

        write($"{folder}/{fileName}", set);

        // One set file per folder: a retitled set's old one goes.
        foreach (string file in storage.GetFiles(folder, "*.yks"))
        {
            if (!string.Equals(Path.GetFileName(file), fileName, StringComparison.Ordinal))
                storage.Delete($"{folder}/{Path.GetFileName(file)}");
        }

        int existing = collection.BeatmapSets.FindIndex(s => s.ID == set.ID);

        if (existing >= 0)
            collection.BeatmapSets[existing] = set;
        else
            collection.BeatmapSets.Add(set);

        // The set's other difficulties still point at the object it replaced, which song select would
        // keep showing the old title from.
        foreach (var beatmap in collection.Beatmaps)
        {
            if (beatmap.BeatmapSet?.ID == set.ID)
                beatmap.BeatmapSet = set;
        }
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> (a song or a background picked in the editor) into the set's
    /// folder, under its own name, or a numbered one if a different file already has it, so the file it
    /// replaces stays until the change is saved. Returns the name it was given.
    /// </summary>
    public string AddFile(BeatmapSet set, string sourcePath)
    {
        string folder = folderPathOf(set);
        string name = Path.GetFileName(sourcePath);
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);

        for (int i = 2; storage.Exists($"{folder}/{name}") && !sameFile($"{folder}/{name}", sourcePath); i++)
            name = $"{stem} ({i}){extension}";

        if (!storage.Exists($"{folder}/{name}"))
        {
            using var source = File.OpenRead(sourcePath);
            using var target = storage.CreateFileSafely($"{folder}/{name}");
            source.CopyTo(target);
        }

        return name;
    }

    /// <summary>
    /// Where <paramref name="fileName"/> in the set's folder is, from the storage root: what the
    /// library's <c>*StoragePath</c>s hold.
    /// </summary>
    public static string StoragePathOf(BeatmapSet set, string fileName) => $"{folderPathOf(set)}/{fileName}";

    /// <summary>
    /// Deletes a difficulty's file and takes it out of the library. Its set stays.
    /// </summary>
    public void Delete(Beatmap beatmap)
    {
        if (!string.IsNullOrEmpty(beatmap.NotesStoragePath) && storage.Exists(beatmap.NotesStoragePath))
            storage.Delete(beatmap.NotesStoragePath);

        collection.Beatmaps.RemoveAll(b => b.ID == beatmap.ID);
    }

    /// <summary>
    /// Deletes a whole set, its folder and every difficulty in it, from disk and the library (a new set
    /// the mapper chose not to keep).
    /// </summary>
    public void Delete(BeatmapSet set)
    {
        if (!string.IsNullOrEmpty(set.StorageFolder))
        {
            string folder = folderPathOf(set);

            if (storage.ExistsDirectory(folder))
                storage.DeleteDirectory(folder);
        }

        collection.Beatmaps.RemoveAll(b => b.BeatmapSet?.ID == set.ID);
        collection.BeatmapSets.RemoveAll(s => s.ID == set.ID);
    }

    private bool sameFile(string storagePath, string sourcePath)
    {
        string? existing = storage.GetFileSystemPath(storagePath);

        if (existing == null)
            return false;

        if (string.Equals(Path.GetFullPath(existing), Path.GetFullPath(sourcePath), StringComparison.Ordinal))
            return true;

        var a = new FileInfo(existing);
        var b = new FileInfo(sourcePath);

        if (a.Length != b.Length)
            return false;

        using var first = a.OpenRead();
        using var second = b.OpenRead();

        return System.Security.Cryptography.SHA256.HashData(first).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(second));
    }

    /// <summary>
    /// The set's folder, from the storage root. A set that was never written gets the folder name the
    /// converter would have given it and keeps it: renaming a set later doesn't move its folder,
    /// since the library finds sets by their files, not their folder names.
    /// </summary>
    private static string folderPathOf(BeatmapSet set)
    {
        set.StorageFolder ??= BeatmapSetUtility.GetFolderName(set);
        return $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{set.StorageFolder}";
    }

    private void write<T>(string path, T value)
    {
        using Stream stream = storage.CreateFileSafely(path);
        JsonSerializer.Serialize(stream, value, BeatmapUtility.WRITE_OPTIONS);
    }
}
