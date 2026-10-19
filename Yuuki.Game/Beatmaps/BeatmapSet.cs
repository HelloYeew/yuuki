// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text.Json.Serialization;
using Sakura.Framework.Graphics.Textures;

namespace Yuuki.Game.Beatmaps;

public class BeatmapSet
{
    public int Version { get; set; } = 1;

    public int ID { get; set; }

    public string Title { get; set; }

    public string TitleUnicode { get; set; }

    public string Artist { get; set; }

    public string ArtistUnicode { get; set; }

    public string Source { get; set; }

    public string SourceUnicode { get; set; }

    /// <summary>
    /// The song's genre (e.g. "Pop"), shown beside the source. Empty when the beatmap doesn't say.
    /// </summary>
    public string Genre { get; set; }

    public int TotalLength { get; set; }

    public int PreviewTime { get; set; }

    /// <summary>
    /// The BPM shown for the set. Timing itself is per difficulty, in <see cref="Beatmap.ControlPoints"/>.
    /// </summary>
    public float BPM { get; set; }

    public string Creator { get; set; }

    public bool HasVideo { get; set; }

    [JsonIgnore]
    public bool UseLocalSource { get; set; }

    public string CoverPath { get; set; }

    public string TrackPath { get; set; }

    public string BackgroundPath { get; set; }

    public string VideoPath { get; set; } = "";

    [JsonIgnore]
    public bool Hide { get; set; }

    /// <summary>
    /// The set's folder inside the beatmaps folder (e.g. "1 Artist - Title"), recorded by the library
    /// scan so a save goes back where the set was read from. Null for a set not yet written. Not serialized.
    /// </summary>
    [JsonIgnore]
    public string? StorageFolder { get; set; }

    /// <summary>
    /// The full storage-relative path to the cover image (e.g. "beatmaps/1 Title - Artist/cover.jpg"),
    /// populated by the beatmap reader. Used to decode the cover on demand. Not serialized.
    /// </summary>
    [JsonIgnore]
    public string CoverStoragePath { get; set; }

    /// <summary>
    /// The decoded album cover texture, populated lazily the first time the cover is shown and cached
    /// here afterwards so it does not need re-decoding. Not serialized.
    /// </summary>
    [JsonIgnore]
    public Texture Cover { get; set; }

    /// <summary>
    /// The full storage-relative path to the background image, populated by the beatmap reader.
    /// Not serialized.
    /// </summary>
    [JsonIgnore]
    public string BackgroundStoragePath { get; set; }

    /// <summary>
    /// The decoded background texture, populated lazily the first time it is shown and cached here
    /// afterwards. Not serialized.
    /// </summary>
    [JsonIgnore]
    public Texture Background { get; set; }

    /// <summary>
    /// The full storage-relative path to the track audio file, populated by the beatmap reader.
    /// Not serialized.
    /// </summary>
    [JsonIgnore]
    public string TrackStoragePath { get; set; }

    /// <summary>
    /// The full storage-relative path to the background video file (if any), populated by the reader.
    /// Not serialized.
    /// </summary>
    [JsonIgnore]
    public string VideoStoragePath { get; set; }

    public override string ToString()
    {
        return $"{ID} {Artist} - {Title}";
    }

    public BeatmapSet Clone() => (BeatmapSet)MemberwiseClone();
}
