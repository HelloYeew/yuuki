// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Database;

public class BeatmapCollectionReader
{
    private readonly Storage gameStorage;
    private readonly BeatmapsCollection beatmapsCollection;

    private static readonly string[] beatmapset_extensions = { ".yks" };
    private static readonly string[] beatmap_extensions = { ".ykb" };

    private static readonly string[] image_extensions = { ".jpg", ".jpeg", ".png", ".bmp" };
    private static readonly string[] audio_extensions = { ".mp3", ".ogg", ".wav", ".flac" };
    private static readonly string[] video_extensions = { ".mp4", ".mov", ".avi", ".webm", ".mkv" };

    public BeatmapCollectionReader(Storage storage, BeatmapsCollection beatmapsCollection)
    {
        gameStorage = storage.GetStorageForDirectory(BeatmapSetUtility.BEATMAP_FOLDER_NAME);
        this.beatmapsCollection = beatmapsCollection;
    }

    public void Read(bool clear = true, bool sortOnFinish = true)
    {
        if (clear)
            beatmapsCollection.Clear();

        var beatmapSetFolders = new List<string>(gameStorage.GetDirectories(string.Empty));

        foreach (string folder in beatmapSetFolders)
        {
            var files = new List<string>(gameStorage.GetFiles(folder));

            foreach (string file in files)
            {
                if (endsWithAny(file, beatmapset_extensions))
                    readBeatmapSet(folder, file);
                else if (endsWithAny(file, beatmap_extensions))
                    readBeatmap(file);
            }
        }

        relinkBeatmaps();

        if (sortOnFinish)
            beatmapsCollection.SortBeatmapSetsById();
    }

    private void readBeatmapSet(string folder, string file)
    {
        try
        {
            using Stream stream = gameStorage.GetStream(file);
            if (stream.IsNull())
                return;

            BeatmapSet beatmapSet = BeatmapSetUtility.Deserialize(stream);
            if (beatmapSet.IsNull())
                return;

            beatmapSet.UseLocalSource = false;
            beatmapSet.StorageFolder = folder;

            if (beatmapsCollection.BeatmapSets.Exists(e => e.ID == beatmapSet.ID))
                return;

            beatmapSet.CoverStoragePath = resolveMedia(folder, beatmapSet.CoverPath, image_extensions, "cover");
            beatmapSet.BackgroundStoragePath = resolveMedia(folder, beatmapSet.BackgroundPath, image_extensions, "background");
            beatmapSet.TrackStoragePath = resolveMedia(folder, beatmapSet.TrackPath, audio_extensions, null);

            if (beatmapSet.HasVideo)
                beatmapSet.VideoStoragePath = resolveMedia(folder, beatmapSet.VideoPath, video_extensions, null);

            beatmapsCollection.BeatmapSets.Add(beatmapSet);
        }
        catch (Exception)
        {
        }
    }

    private void readBeatmap(string file)
    {
        try
        {
            using Stream stream = gameStorage.GetStream(file);
            if (stream.IsNull())
                return;

            // Metadata only: the chart itself is the bulk of the file and is loaded per play by
            // BeatmapNoteStore, from the path recorded below.
            Beatmap beatmap = BeatmapUtility.DeserializeMetadata(stream);
            if (beatmap.IsNull())
                return;

            beatmap.NotesStoragePath = $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{file}";

            if (!beatmapsCollection.Beatmaps.Exists(e => e.ID == beatmap.ID))
                beatmapsCollection.Beatmaps.Add(beatmap);
        }
        catch (Exception)
        {
            // Ignore malformed beatmap files.
        }
    }

    private void relinkBeatmaps()
    {
        foreach (var beatmap in beatmapsCollection.Beatmaps)
        {
            if (beatmap.BeatmapSet.IsNull())
                continue;

            var owner = beatmapsCollection.GetBeatmapSetById(beatmap.BeatmapSet.ID);
            if (owner.IsNotNull())
                beatmap.BeatmapSet = owner;
        }
    }

    private string? resolveMedia(string folder, string preferredName, string[] extensions, string hint)
    {
        if (!string.IsNullOrEmpty(preferredName) && gameStorage.Exists($"{folder}/{preferredName}"))
            return $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{folder}/{preferredName}";

        string? fallback = null;

        foreach (string file in gameStorage.GetFiles(folder))
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (Array.IndexOf(extensions, extension) < 0)
                continue;

            if (!string.IsNullOrEmpty(hint) && Path.GetFileName(file).Contains(hint, StringComparison.OrdinalIgnoreCase))
                return $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{file}";

            fallback ??= file;
        }

        return fallback.IsNull() ? null : $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{fallback}";
    }

    private static bool endsWithAny(string file, string[] extensions)
    {
        foreach (string ext in extensions)
        {
            if (file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
