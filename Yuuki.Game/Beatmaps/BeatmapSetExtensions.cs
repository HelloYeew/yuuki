// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// Helpers for picking between a <see cref="BeatmapSet"/>'s romanized and unicode metadata,
/// driven by the <c>UseUnicodeInfo</c> setting (see <c>YuukiConfigManager</c>).
/// </summary>
public static class BeatmapSetExtensions
{
    /// <summary>
    /// The title to display: <see cref="BeatmapSet.TitleUnicode"/> when <paramref name="useUnicode"/>
    /// is true and it's populated, otherwise <see cref="BeatmapSet.Title"/>.
    /// </summary>
    public static string GetDisplayTitle(this BeatmapSet set, bool useUnicode) => pick(set.Title, set.TitleUnicode, useUnicode);

    /// <summary>
    /// The artist to display: <see cref="BeatmapSet.ArtistUnicode"/> when <paramref name="useUnicode"/>
    /// is true, and it's populated, otherwise <see cref="BeatmapSet.Artist"/>.
    /// </summary>
    public static string GetDisplayArtist(this BeatmapSet set, bool useUnicode) => pick(set.Artist, set.ArtistUnicode, useUnicode);

    /// <summary>
    /// The source to display: <see cref="BeatmapSet.SourceUnicode"/> when <paramref name="useUnicode"/>
    /// is true, and it's populated, otherwise <see cref="BeatmapSet.Source"/>.
    /// </summary>
    public static string GetDisplaySource(this BeatmapSet set, bool useUnicode) => pick(set.Source, set.SourceUnicode, useUnicode);

    private static string pick(string romanized, string unicode, bool useUnicode) => useUnicode && !string.IsNullOrEmpty(unicode) ? unicode : romanized ?? "";
}
