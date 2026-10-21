// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// The filter a song select lists by, shared with what it lists. A song's level bars keep every difficulty
/// but dim the ones the filter leaves out, as osu! greys them on a set, so you still see what the song
/// has.
/// </summary>
public class SongListFilter
{
    /// <summary>
    /// The filter in effect, or null before there is one.
    /// </summary>
    public BeatmapFilter? Filter { get; private set; }

    /// <summary>
    /// Moves on whenever the filter changes, for anything showing it to look again.
    /// </summary>
    public int Version { get; private set; }

    public void Set(BeatmapFilter filter)
    {
        Filter = filter;
        Version++;
    }

    /// <summary>
    /// Tells whatever shows the filter that it changed.
    /// </summary>
    public void Changed() => Version++;

    /// <summary>
    /// Whether the filter lets <paramref name="beatmap"/> through; true when nothing is filtered.
    /// </summary>
    public bool LetsThrough(Beatmap beatmap) => Filter == null || !Filter.IsFiltering || Filter.Matches(beatmap);
}
