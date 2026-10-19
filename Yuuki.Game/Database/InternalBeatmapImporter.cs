// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Database;

public class InternalBeatmapImporter
{
    private readonly Storage resourceStorage;
    private readonly Storage gameStorage;
    private readonly BeatmapTestUtility testUtility = new BeatmapTestUtility();

    private static readonly JsonSerializerOptions json_options = new JsonSerializerOptions { WriteIndented = true };

    public InternalBeatmapImporter(Storage resourceStorage, Storage rootGameStorage)
    {
        this.resourceStorage = resourceStorage;
        gameStorage = rootGameStorage.GetStorageForDirectory(BeatmapSetUtility.BEATMAP_FOLDER_NAME);
    }

    public void Import()
    {
        foreach (BeatmapSet beatmapSet in testUtility.BeatmapSets)
        {
            try
            {
                importSet(beatmapSet, testUtility.Beatmaps);
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to import bundled beatmapset '{beatmapSet}'.", e);
            }
        }
    }

    private void importSet(BeatmapSet beatmapSet, List<Beatmap> beatmaps)
    {
        string folder = BeatmapSetUtility.GetFolderName(beatmapSet);

        string coverFile = "cover" + Path.GetExtension(beatmapSet.CoverPath);
        string backgroundFile = "background" + Path.GetExtension(beatmapSet.BackgroundPath);
        string trackFile = "track" + Path.GetExtension(beatmapSet.TrackPath);

        if (!copy($"Tracks/{beatmapSet.TrackPath}", $"{folder}/{trackFile}")) return;
        if (!copy($"Textures/{beatmapSet.CoverPath}", $"{folder}/{coverFile}")) return;
        if (!copy($"Textures/{beatmapSet.BackgroundPath}", $"{folder}/{backgroundFile}")) return;

        BeatmapSet storedSet = beatmapSet.Clone();
        storedSet.UseLocalSource = false;
        storedSet.CoverPath = coverFile;
        storedSet.BackgroundPath = backgroundFile;
        storedSet.TrackPath = trackFile;

        writeJson($"{folder}/{BeatmapSetUtility.GetBeatmapSetFileName(beatmapSet)}.yks", storedSet);

        foreach (Beatmap beatmap in beatmaps)
        {
            if (beatmap.BeatmapSet == null || beatmap.BeatmapSet.ID != beatmapSet.ID)
                continue;

            Beatmap storedBeatmap = beatmap.Clone();
            storedBeatmap.BeatmapSet = storedSet;
            storedBeatmap.BackgroundPath = backgroundFile;

            writeJson($"{folder}/{BeatmapUtility.GetBeatmapFileName(beatmap)}.ykb", storedBeatmap);
        }
    }

    private bool copy(string sourcePath, string destinationPath)
    {
        if (!resourceStorage.Exists(sourcePath))
        {
            Logger.Warning($"Bundled beatmap resource missing: {sourcePath}");
            return false;
        }

        using Stream source = resourceStorage.GetStream(sourcePath);
        if (source == null)
            return false;

        using Stream destination = gameStorage.GetStream(destinationPath, FileAccess.Write);
        source.CopyTo(destination);
        return true;
    }

    private void writeJson(string destinationPath, object value)
    {
        string json = JsonSerializer.Serialize(value, json_options);

        using Stream destination = gameStorage.GetStream(destinationPath, FileAccess.Write);
        using var writer = new StreamWriter(destination);
        writer.Write(json);
    }
}
