// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.Text.Json;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Utilities;

/// <summary>
/// Helper methods for working with <see cref="BeatmapSet"/> files and on-disk paths.
/// </summary>
public static class BeatmapSetUtility
{
    public const string BEATMAP_FOLDER_NAME = "beatmaps";

    /// <summary>
    /// Format the total length of a beatmap set as mm:ss.
    /// </summary>
    public static string GetFormattedTime(BeatmapSet beatmapSet)
    {
        int totalMilliseconds = beatmapSet.TotalLength;
        return $"{totalMilliseconds / 60000:00}:{totalMilliseconds / 1000 % 60:00}";
    }

    public static BeatmapSet Deserialize(string json) => JsonSerializer.Deserialize<BeatmapSet>(json);

    public static BeatmapSet Deserialize(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<BeatmapSet>(reader.ReadToEnd());
    }

    /// <summary>
    /// The folder name a beatmap set is written to (Format: {ID} {Artist} - {Title}). Only used when
    /// importing/writing — the reader does not rely on it.
    /// </summary>
    public static string GetFolderName(BeatmapSet beatmapSet)
    {
        return $"{beatmapSet.ID} {StringUtility.CleanFileName(beatmapSet.Artist)} - {StringUtility.CleanFileName(beatmapSet.Title)}";
    }

    /// <summary>
    /// The base file name (no extension) used for a beatmap set's serialized file in storage.
    /// </summary>
    public static string GetBeatmapSetFileName(BeatmapSet beatmapSet) => GetFolderName(beatmapSet);
}
