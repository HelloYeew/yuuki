// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Extensions.ObjectExtensions;

namespace Yuuki.Game.Beatmaps;

public class BeatmapsCollection
{
    public const int THEME_BEATMAP_SET_ID = 0;

    public List<BeatmapSet> BeatmapSets { get; set; } = new List<BeatmapSet>();
    public List<Beatmap> Beatmaps { get; set; } = new List<Beatmap>();

    public void SortBeatmapSetsById()
    {
        BeatmapSets = BeatmapSets.OrderBy(e => e.ID).ToList();
    }

    public Beatmap[] GetBeatmapsFromBeatmapSet(BeatmapSet beatmapSet, bool sortByDifficulty = true)
    {
        if (beatmapSet.IsNull())
            return Array.Empty<Beatmap>();

        Beatmap[] beatmaps = Beatmaps.FindAll(e => e.BeatmapSet.IsNotNull() && e.BeatmapSet.ID == beatmapSet.ID).ToArray();

        if (sortByDifficulty)
            beatmaps = beatmaps.OrderBy(e => e.DifficultyRating).ToArray();

        return beatmaps;
    }

    public Beatmap? GetFirstBeatmapFromBeatmapSet(BeatmapSet beatmapSet)
    {
        return GetBeatmapsFromBeatmapSet(beatmapSet, false).FirstOrDefault();
    }

    public Dictionary<string, int> GetMinMaxDifficultyLevel(BeatmapSet beatmapSet)
    {
        Beatmap[] beatmaps = GetBeatmapsFromBeatmapSet(beatmapSet);

        if (beatmaps.Length == 0)
        {
            return new Dictionary<string, int>
            {
                { "min", 0 },
                { "max", 0 }
            };
        }

        return new Dictionary<string, int>
        {
            { "min", (int)beatmaps.Min(e => e.DifficultyRating) },
            { "max", (int)beatmaps.Max(e => e.DifficultyRating) }
        };
    }

    public BeatmapSet? GetBeatmapSetById(int id) => BeatmapSets.Find(e => e.ID == id);

    /// <summary>
    /// An ID for a set made in the editor. Below zero, so it can never meet an imported one (IDs
    /// and the importer's generated ones are all positive; 0 is the theme song).
    /// </summary>
    public int NextLocalSetId() => Math.Min(0, BeatmapSets.Select(s => s.ID).DefaultIfEmpty(0).Min()) - 1;

    /// <summary>
    /// An ID for a difficulty made in the editor, below zero for the same reason.
    /// </summary>
    public int NextLocalBeatmapId() => Math.Min(0, Beatmaps.Select(b => b.ID).DefaultIfEmpty(0).Min()) - 1;

    public Beatmap? GetBeatmapById(int id) => Beatmaps.Find(e => e.ID == id);

    /// <param name="except">A set not to pick while there's another, such as the one playing now.</param>
    public BeatmapSet? GetRandomBeatmapSet(BeatmapSet? except = null)
    {
        var candidates = BeatmapSets.Where(e => !e.Hide).ToList();

        if (except != null && candidates.Count > 1)
            candidates.RemoveAll(e => e.ID == except.ID);

        return candidates.Count == 0 ? null : candidates[Random.Shared.Next(candidates.Count)];
    }

    /// <summary>
    /// Adds the sets and difficulties of <paramref name="scanned"/> that this doesn't have yet, linked
    /// to this collection's sets. What's already here is left as it is, so nothing a screen is showing
    /// changes under it.
    /// </summary>
    /// <returns>How many sets were added.</returns>
    public int AddMissingFrom(BeatmapsCollection scanned)
    {
        int added = 0;

        foreach (var set in scanned.BeatmapSets)
        {
            if (GetBeatmapSetById(set.ID).IsNotNull())
                continue;

            BeatmapSets.Add(set);
            added++;
        }

        foreach (var beatmap in scanned.Beatmaps)
        {
            if (GetBeatmapById(beatmap.ID).IsNotNull())
                continue;

            if (beatmap.BeatmapSet.IsNotNull() && GetBeatmapSetById(beatmap.BeatmapSet.ID) is { } owner)
                beatmap.BeatmapSet = owner;

            Beatmaps.Add(beatmap);
        }

        if (added > 0)
            SortBeatmapSetsById();

        return added;
    }

    public void Clear()
    {
        BeatmapSets.Clear();
        Beatmaps.Clear();
    }
}
