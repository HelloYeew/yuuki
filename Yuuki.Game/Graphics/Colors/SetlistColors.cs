// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.Colors;

/// <summary>
/// Colors of song selects lists. Text on a colored fill is tone-on-tone (a darker shade of that fill,
/// never white), as on the menu buttons; text on the glass band is the screen's ink.
/// </summary>
public static class SetlistColors
{
    private static readonly Color near_black = ColorExtensions.FromHex("#1B1622");

    /// <summary>
    /// A tier or rank color as a fill (a focus window, a best mark): softened towards white.
    /// </summary>
    public static Color TierFill(Color tier) => tier.Lighten(0.3f);

    /// <summary>
    /// Ink for text on a tier or rank color, or its fill: the same hue, most of the way to near-black.
    /// </summary>
    public static Color TierInk(Color tier) => tier.Blend(near_black, 0.75f);

    /// <summary>
    /// The color of a song's hardest difficulty: what its window wears until the cover's own colour is known.
    /// </summary>
    public static Color SongTier(IReadOnlyList<Beatmap> difficulties) =>
        DifficultyColors.For(difficulties.Count == 0 ? 0 : difficulties.Max(b => b.DifficultyRating));

    /// <summary>
    /// A song's fill and the ink printed on it: its cover's color once picked, and otherwise (not loaded
    /// yet, or greyscale art) its hardest difficulty's tier.
    /// </summary>
    public static (Color Fill, Color Ink) ForSong(BeatmapSet set, IReadOnlyList<Beatmap> difficulties, CoverColorStore? coverColors)
    {
        if (coverColors != null && coverColors.TryGet(BeatmapSetCoverSource.KeyFor(set), out var swatch) && !swatch.IsNeutral)
            return (swatch.Fill, swatch.Ink);

        var tier = SongTier(difficulties);
        return (TierFill(tier), TierInk(tier));
    }

    /// <summary>
    /// Secondary text on the glass band (artists, counts, labels): the screen's ink, softened. The palette's
    /// TextMuted is a mid-grey, and on glass whose tone changes with every song it disappears.
    /// </summary>
    public static Color Secondary(Color ink) => ink.WithAlpha(0.74f);
}
