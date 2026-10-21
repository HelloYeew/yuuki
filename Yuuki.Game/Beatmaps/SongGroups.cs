// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// The groups a sort makes of the song list: what group jump (PgUp / PgDn) moves between, what the large
/// label shows while skimming, and what the scrubber's ticks are labelled with. "Added" order has none.
/// </summary>
public static class SongGroups
{
    /// <summary>
    /// The group <paramref name="set"/> is in, as the skim label shows it: the first letter for title or
    /// artist ("A"), "Lv 12", "170 BPM", or "1:30+" for length. Null when the sort has no groups.
    /// </summary>
    /// <param name="set">The song.</param>
    /// <param name="sort">The sort the list is in.</param>
    /// <param name="level">The song's level as the Level sort places it: its highest difficulty the filter
    /// lets through.</param>
    public static string? LabelOf(BeatmapSet set, BeatmapSort sort, int level) => sort switch
    {
        BeatmapSort.Title => initialOf(set.Title),
        BeatmapSort.Artist => initialOf(set.Artist),
        BeatmapSort.Level => $"Lv {level}",
        BeatmapSort.Bpm => $"{bpmBand(set, 10)} BPM",
        BeatmapSort.Length => $"{TimeSpan.FromSeconds(lengthSeconds(set) / 30 * 30):m\\:ss}+",
        _ => null
    };

    /// <summary>
    /// The group <paramref name="set"/> is in, as the scrubber's ticks show it: shorter and coarser than
    /// <see cref="LabelOf"/>, so a long list still has room for them ("A", "12", "160", "1'").
    /// </summary>
    public static string? TickOf(BeatmapSet set, BeatmapSort sort, int level) => sort switch
    {
        BeatmapSort.Title => initialOf(set.Title),
        BeatmapSort.Artist => initialOf(set.Artist),
        BeatmapSort.Level => level.ToString(),
        BeatmapSort.Bpm => bpmBand(set, 20).ToString(),
        BeatmapSort.Length => $"{lengthSeconds(set) / 60}'",
        _ => null
    };

    /// <summary>
    /// The first letter, upper case; "#" for a title that starts with anything else, or has none.
    /// </summary>
    private static string initialOf(string? text)
    {
        if (string.IsNullOrEmpty(text) || !char.IsLetter(text[0]))
            return "#";

        return char.ToUpperInvariant(text[0]).ToString();
    }

    private static int bpmBand(BeatmapSet set, int width) => (int)Math.Floor(set.BPM / width) * width;

    private static int lengthSeconds(BeatmapSet set) => (int)(set.TotalLength / 1000);
}
