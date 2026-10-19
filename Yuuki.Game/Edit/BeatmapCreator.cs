// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sakura.Framework.Logging;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Database;
using AtlTrack = ATL.Track;

namespace Yuuki.Game.Edit;

/// <summary>
/// Makes new beatmaps in the library
/// </summary>
public static class BeatmapCreator
{
    /// <summary>
    /// The audio files a set's song can be.
    /// </summary>
    public static readonly string[] AUDIO_EXTENSIONS = { "mp3", "ogg", "wav", "flac", "m4a" };

    /// <summary>
    /// The image files a background can be.
    /// </summary>
    public static readonly string[] IMAGE_EXTENSIONS = { "jpg", "jpeg", "png" };

    public const string DEFAULT_DIFFICULTY_NAME = "New difficulty";

    /// <summary>
    /// A new set around the song at <paramref name="audioPath"/>, with one empty difficulty: the
    /// artist and title come from the song's tags (the file name when it has none)
    /// </summary>
    /// <returns>The new difficulty, its set attached, both already in the library.</returns>
    public static Beatmap CreateSet(string audioPath, BeatmapWriter writer, BeatmapsCollection collection, string creator = "")
    {
        var tags = SongTags.Read(audioPath);

        var set = new BeatmapSet
        {
            ID = collection.NextLocalSetId(),
            Artist = romanised(tags.Artist),
            ArtistUnicode = tags.Artist,
            Title = romanised(tags.Title),
            TitleUnicode = tags.Title,
            Source = "",
            SourceUnicode = "",
            Genre = "",
            Creator = creator,
            TotalLength = (int)tags.Length,
            // TODO: I get this number from osu! (40%) but... yeah 40% is it good? dunno /shrug
            PreviewTime = (int)(tags.Length * 0.4),
            CoverPath = "",
            BackgroundPath = "",
            VideoPath = ""
        };

        string track = writer.AddFile(set, audioPath);
        set.TrackPath = track;
        set.TrackStoragePath = BeatmapWriter.StoragePathOf(set, track);

        var beatmap = new Beatmap
        {
            ID = collection.NextLocalBeatmapId(),
            BeatmapSet = set,
            DifficultyName = DEFAULT_DIFFICULTY_NAME,
            Creator = creator,
            LaneCount = 4,
            BackgroundPath = "",
            ControlPoints = new ControlPointInfo()
        };

        writer.Save(set);
        writer.Save(beatmap);

        return beatmap;
    }

    /// <summary>
    /// Another difficulty in <paramref name="from"/>'s set. Blank keeps the timing, lane count, and
    /// mapper, a copy keeps the notes too.
    /// </summary>
    /// <param name="notes">The notes of <paramref name="from"/>, for a copy.</param>
    public static Beatmap CreateDifficulty(Beatmap from, bool copy, IEnumerable<BeatmapNote> notes, BeatmapWriter writer, BeatmapsCollection collection)
    {
        var siblings = collection.GetBeatmapsFromBeatmapSet(from.BeatmapSet).Select(b => b.DifficultyName).ToHashSet();

        var beatmap = new Beatmap
        {
            ID = collection.NextLocalBeatmapId(),
            BeatmapSet = from.BeatmapSet,
            DifficultyName = uniqueName(copy ? $"{from.DifficultyName} (copy)" : DEFAULT_DIFFICULTY_NAME, siblings),
            Creator = from.Creator,
            LaneCount = from.LaneCount,
            BackgroundPath = from.BackgroundPath,
            ControlPoints = from.ControlPoints.Clone(),
            Notes = copy ? notes.Select(n => n.Clone()).ToArray() : Array.Empty<BeatmapNote>()
        };

        writer.Save(beatmap);

        // The library keeps metadata only; the editor reads the notes back from the file.
        beatmap.Notes = Array.Empty<BeatmapNote>();
        return beatmap;
    }

    private static string uniqueName(string name, IReadOnlySet<string> taken)
    {
        string candidate = name;

        for (int i = 2; taken.Contains(candidate); i++)
            candidate = $"{name} ({i})";

        return candidate;
    }

    /// <summary>
    /// The romanised field from the original one, the original minus what isn't Latin
    /// script. When that leaves nothing (an all-Japanese title), the original stands in, so the set's
    /// folder and song select still have a name to show.
    /// </summary>
    private static string romanised(string original)
    {
        var builder = new StringBuilder();

        foreach (char c in original)
        {
            if (c < 0x250 || char.IsWhiteSpace(c) || char.IsPunctuation(c))
                builder.Append(c);
        }

        string result = builder.ToString().Trim();
        return result.Any(char.IsLetterOrDigit) ? result : original;
    }
}

/// <summary>
/// What a song file says about itself.
/// </summary>
/// <param name="Length">In ms; 0 when it can't tell.</param>
public readonly record struct SongTags(string Artist, string Title, double Length)
{
    /// <summary>
    /// Reads the tags of the song at <paramref name="path"/>. A song without them gets its file name as
    /// the title
    /// </summary>
    public static SongTags Read(string path)
    {
        string artist = "";
        string title = "";
        double length = 0;

        try
        {
            var track = new AtlTrack(path);
            artist = track.Artist ?? "";
            title = track.Title ?? "";
            length = Math.Max(0, track.DurationMs);
        }
        catch (Exception e)
        {
            Logger.Error($"Couldn't read the tags of {Path.GetFileName(path)}: {e.Message}");
        }

        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(path);

        return new SongTags(artist.Trim(), title.Trim(), length);
    }
}
