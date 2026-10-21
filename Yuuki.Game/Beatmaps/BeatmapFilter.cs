// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// How song select orders the songs.
/// </summary>
public enum BeatmapSort
{
    Added,
    Title,
    Artist,
    Level,
    Bpm,
    Length
}

/// <summary>
/// Song selects search and filters
/// </summary>
public class BeatmapFilter
{
    /// <summary>
    /// The highest level the range can be set to; at this, the range has no upper limit.
    /// </summary>
    public const int MAX_LEVEL = 15;

    public string Search { get; set; } = "";

    /// <summary>
    /// The lowest level shown (0 for no lower limit).
    /// </summary>
    public int MinLevel { get; set; }

    /// <summary>
    /// The highest level shown; <see cref="MAX_LEVEL"/> means no upper limit.
    /// </summary>
    public int MaxLevel { get; set; } = MAX_LEVEL;

    public BeatmapSort Sort { get; set; } = BeatmapSort.Added;

    /// <summary>
    /// The lane counts shown, empty for all of them. A song with difficulties on several shows with only
    /// the ones on these.
    /// </summary>
    public HashSet<int> Lanes { get; } = new HashSet<int>();

    /// <summary>
    /// Whether anything is being filtered out (sorting aside).
    /// </summary>
    public bool IsFiltering => terms.Length > 0 || LevelRangeSet || Lanes.Count > 0;

    /// <summary>
    /// Whether the level range leaves any level out.
    /// </summary>
    public bool LevelRangeSet => MinLevel > 0 || MaxLevel < MAX_LEVEL;

    private string[] terms => Search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The level a difficulty is shown and filtered at (its rating, rounded down as the panel shows it).
    /// </summary>
    public static int LevelOf(Beatmap beatmap) => (int)beatmap.DifficultyRating;

    /// <summary>
    /// Whether <paramref name="beatmap"/> passes, judged with its song's details too.
    /// </summary>
    public bool Matches(Beatmap beatmap) => inRange(beatmap) && onLanes(beatmap) && matchesTerms(terms, beatmap.BeatmapSet, beatmap);

    /// <summary>
    /// How many difficulties among <paramref name="sets"/> sit at each level, for the search and lanes but
    /// any level: what the level histogram draws, so the range shows what it would let through before it's
    /// set. Indexed by level, with everything at <see cref="MAX_LEVEL"/> and above counted there.
    /// </summary>
    public int[] LevelCounts(IEnumerable<BeatmapSet> sets, Func<BeatmapSet, IReadOnlyList<Beatmap>> difficultiesOf)
    {
        string[] words = terms;
        int[] counts = new int[MAX_LEVEL + 1];

        foreach (BeatmapSet set in sets)
        {
            foreach (Beatmap beatmap in difficultiesOf(set))
            {
                if (onLanes(beatmap) && matchesTerms(words, set, beatmap))
                    counts[Math.Clamp(LevelOf(beatmap), 0, MAX_LEVEL)]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// The songs that pass, in <see cref="Sort"/> order. <paramref name="difficultiesOf"/> gives each
    /// song's difficulties; a song without any passes on its own details when no level range is set.
    /// <paramref name="alwaysInclude"/>, when given, is listed (in its sorted place) whether it passes or
    /// not: what song select does for a song picked from outside it, so the list holds what's playing.
    /// </summary>
    public List<BeatmapSet> Apply(IEnumerable<BeatmapSet> sets, Func<BeatmapSet, IReadOnlyList<Beatmap>> difficultiesOf, BeatmapSet? alwaysInclude = null)
    {
        string[] words = terms;
        var matches = new List<(BeatmapSet Set, int Level)>();

        foreach (BeatmapSet set in sets)
        {
            IReadOnlyList<Beatmap> difficulties = difficultiesOf(set);

            if (passes(words, set, difficulties, out int level))
                matches.Add((set, level));
            else if (Equals(set, alwaysInclude))
                matches.Add((set, difficulties.Count > 0 ? difficulties.Max(LevelOf) : 0));
        }

        IEnumerable<(BeatmapSet Set, int Level)> sorted = Sort switch
        {
            BeatmapSort.Title => matches.OrderBy(m => m.Set.Title, StringComparer.CurrentCultureIgnoreCase),
            BeatmapSort.Artist => matches.OrderBy(m => m.Set.Artist, StringComparer.CurrentCultureIgnoreCase),
            BeatmapSort.Level => matches.OrderBy(m => m.Level),
            BeatmapSort.Bpm => matches.OrderBy(m => m.Set.BPM),
            BeatmapSort.Length => matches.OrderBy(m => m.Set.TotalLength),
            _ => matches
        };

        return sorted.Select(m => m.Set).ToList();
    }

    /// <summary>
    /// Whether <paramref name="set"/> passes, given its difficulties.
    /// </summary>
    public bool Passes(BeatmapSet set, IReadOnlyList<Beatmap> difficulties) => passes(terms, set, difficulties, out _);

    /// <summary>
    /// Whether a song passes: one of its difficulties does, or, with none, its own details when no level
    /// range or lanes are set. <paramref name="level"/> is its highest passing level, what the Level sort
    /// uses.
    /// </summary>
    private bool passes(string[] words, BeatmapSet set, IReadOnlyList<Beatmap> difficulties, out int level)
    {
        level = 0;
        bool found = false;

        foreach (Beatmap beatmap in difficulties)
        {
            if (!inRange(beatmap) || !onLanes(beatmap) || !matchesTerms(words, set, beatmap))
                continue;

            level = found ? Math.Max(level, LevelOf(beatmap)) : LevelOf(beatmap);
            found = true;
        }

        if (found)
            return true;

        return difficulties.Count == 0 && !LevelRangeSet && Lanes.Count == 0 && matchesTerms(words, set, null);
    }

    private bool onLanes(Beatmap beatmap) => Lanes.Count == 0 || Lanes.Contains(beatmap.LaneCount);

    private bool inRange(Beatmap beatmap)
    {
        int level = LevelOf(beatmap);
        return level >= MinLevel && (MaxLevel >= MAX_LEVEL || level <= MaxLevel);
    }

    private static bool matchesTerms(string[] words, BeatmapSet? set, Beatmap? beatmap)
    {
        if (words.Length == 0)
            return true;

        string[] fields =
        {
            set?.Title ?? "", set?.TitleUnicode ?? "",
            set?.Artist ?? "", set?.ArtistUnicode ?? "",
            set?.Source ?? "", set?.SourceUnicode ?? "",
            set?.Genre ?? "", set?.Creator ?? "",
            beatmap?.DifficultyName ?? "", beatmap?.Creator ?? ""
        };

        CompareInfo compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth;

        return words.All(word => fields.Any(field => compare.IndexOf(field, word, options) >= 0));
    }
}
