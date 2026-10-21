// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Utilities;

/// <summary>
/// Helper methods for working with <see cref="Beatmap"/> files.
/// </summary>
public static class BeatmapUtility
{
    /// <summary>
    /// Map a difficulty rating onto a <see cref="DifficultyLevel"/> bucket.
    /// </summary>
    public static DifficultyLevel CalculateDifficultyLevel(double difficultyRating)
    {
        return difficultyRating switch
        {
            < 4 => DifficultyLevel.Basic,
            < 8 => DifficultyLevel.Advance,
            < 12 => DifficultyLevel.Expert,
            < 16 => DifficultyLevel.Master,
            _ => DifficultyLevel.Virtuoso
        };
    }

    public static DifficultyLevel CalculateDifficultyLevel(Beatmap beatmap) => CalculateDifficultyLevel(beatmap.DifficultyRating);

    /// <summary>
    /// How many lanes a difficulty has, as rhythm games write it: "4K".
    /// </summary>
    public static string FormatLanes(int laneCount) => $"{laneCount}K";

    /// <summary>
    /// A difficulty's level as it's shown, to one decimal place: "13.4". Rounded down, so it never reads as
    /// the level above the one it's filtered and sorted at (13.96 is 13.9, a level 13).
    /// </summary>
    public static string FormatLevel(double rating) =>
        (Math.Floor(rating * 10 + 1e-9) / 10).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// The levels across a song's difficulties: "13.4", or "2.0–13.4" when they differ; empty when there
    /// are none. Shown after the level icon, so with no "Lv".
    /// </summary>
    public static string FormatLevelRange(IEnumerable<Beatmap> beatmaps)
    {
        double min = double.MaxValue, max = double.MinValue;

        foreach (var beatmap in beatmaps)
        {
            min = Math.Min(min, beatmap.DifficultyRating);
            max = Math.Max(max, beatmap.DifficultyRating);
        }

        if (min > max)
            return "";

        string low = FormatLevel(min), high = FormatLevel(max);
        return low == high ? low : $"{low}–{high}";
    }

    /// <summary>
    /// The lane counts across a song's difficulties: "4K", or "4-7K" when they differ; empty when there
    /// are none.
    /// </summary>
    public static string FormatLaneRange(IEnumerable<Beatmap> beatmaps)
    {
        int min = int.MaxValue, max = 0;

        foreach (var beatmap in beatmaps)
        {
            min = Math.Min(min, beatmap.LaneCount);
            max = Math.Max(max, beatmap.LaneCount);
        }

        if (max == 0)
            return "";

        return min == max ? FormatLanes(max) : $"{min}-{max}K";
    }

    /// <summary>
    /// The base file name (no extension) used for a beatmap's serialized file in storage.
    /// </summary>
    public static string GetBeatmapFileName(Beatmap beatmap)
    {
        return $"{beatmap.ID} {StringUtility.CleanFileName(beatmap.BeatmapSet.Artist)} - {StringUtility.CleanFileName(beatmap.BeatmapSet.Title)} [{StringUtility.CleanFileName(beatmap.DifficultyName)}]";
    }

    public static Beatmap Deserialize(string json) => JsonSerializer.Deserialize<Beatmap>(json);

    /// <summary>
    /// How beatmap and set files are written: indented, with titles in other scripts left readable
    /// rather than escaped. The .osz converter writes the same way.
    /// </summary>
    public static readonly JsonSerializerOptions WRITE_OPTIONS = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Reads a beatmap in full, including its notes. Deserializes straight from the stream so a
    /// note-heavy file is never materialised as one large string first.
    /// </summary>
    public static Beatmap Deserialize(Stream stream) => JsonSerializer.Deserialize<Beatmap>(stream);

    /// <summary>
    /// Reads a beatmap's metadata, skipping its notes array entirely.
    /// </summary>
    /// <remarks>
    /// Used by the library scan. Every difficulty in the library goes through this, and a chart is
    /// tens of thousands of notes; parsing them all at scan time would allocate — and, since the
    /// beatmaps stay in the collection, retain — footprint proportional to the whole library for data
    /// only the one difficulty being played needs. The skip happens inside the JSON reader, so no
    /// <see cref="BeatmapNote"/> is constructed at all.
    /// </remarks>
    public static Beatmap DeserializeMetadata(Stream stream) => JsonSerializer.Deserialize<Beatmap>(stream, metadata_only_options);

    /// <summary>
    /// Reads just the notes out of a beatmap file.
    /// </summary>
    public static BeatmapNote[] DeserializeNotes(Stream stream) => Deserialize(stream)?.Notes;

    private static readonly JsonSerializerOptions metadata_only_options = createMetadataOnlyOptions();

    private static JsonSerializerOptions createMetadataOnlyOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SkipNotesConverter());
        return options;
    }

    /// <summary>
    /// Reads a beatmap's notes array as empty, consuming the JSON without building anything.
    /// </summary>
    private class SkipNotesConverter : JsonConverter<BeatmapNote[]>
    {
        public override BeatmapNote[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            reader.Skip();
            return Array.Empty<BeatmapNote>();
        }

        public override void Write(Utf8JsonWriter writer, BeatmapNote[] value, JsonSerializerOptions options)
        {
            // Only ever used for reading; writing a beatmap must not silently drop its chart.
            throw new NotSupportedException($"{nameof(SkipNotesConverter)} is read-only.");
        }
    }
}
