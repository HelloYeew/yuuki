// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Graphics.Colors;

/// <summary>
/// The colors of difficulty, in one place: every card, bar, tab and row that shows how hard a chart is
/// takes its color from here.
/// </summary>
/// <remarks>
/// A gradient rather than one color per tier. Each tier's own color sits at the middle of its levels, and
/// in between it blends to the next as it looks (<c>BlendPerceptually</c>), so a 7.9 and an 8.0 look like neighbours rather than two
/// different tiers. Below the first tier's middle and past the last's it holds.
/// </remarks>
public static class DifficultyColors
{
    public static readonly Color BASIC = ColorExtensions.FromHex("#A3BE8C");
    public static readonly Color ADVANCE = ColorExtensions.FromHex("#88C0D0");
    public static readonly Color EXPERT = ColorExtensions.FromHex("#EBCB8B");
    public static readonly Color MASTER = ColorExtensions.FromHex("#D08770");
    public static readonly Color VIRTUOSO = ColorExtensions.FromHex("#B48EAD");

    /// <summary>
    /// Each tier's color at the middle of its levels: the stops the gradient blends between.
    /// </summary>
    private static readonly (double Rating, Color Color)[] stops =
    {
        (2, BASIC),
        (6, ADVANCE),
        (10, EXPERT),
        (14, MASTER),
        (18, VIRTUOSO)
    };

    /// <summary>
    /// The color of a difficulty rating.
    /// </summary>
    public static Color For(double rating)
    {
        if (rating <= stops[0].Rating)
            return stops[0].Color;

        for (int i = 1; i < stops.Length; i++)
        {
            var (stopRating, color) = stops[i];

            if (rating > stopRating)
                continue;

            var (previousRating, previousColor) = stops[i - 1];
            return previousColor.BlendPerceptually(color, (float)((rating - previousRating) / (stopRating - previousRating)));
        }

        return stops[^1].Color;
    }

    /// <summary>
    /// The color of <paramref name="beatmap"/>'s rating.
    /// </summary>
    public static Color For(Beatmap beatmap) => For(beatmap.DifficultyRating);

    /// <summary>
    /// A tier's own color, for what stands for the whole tier (its name under the level histogram). Not an
    /// overload of <see cref="For(double)"/>: a literal 0 converts to an enum too, and would be ambiguous.
    /// </summary>
    public static Color ForTier(DifficultyLevel tier) => tier switch
    {
        DifficultyLevel.Basic => BASIC,
        DifficultyLevel.Advance => ADVANCE,
        DifficultyLevel.Expert => EXPERT,
        DifficultyLevel.Master => MASTER,
        _ => VIRTUOSO
    };

    /// <summary>
    /// The color of a whole level, every rating from it to the next: the one at its middle.
    /// </summary>
    public static Color ForLevel(int level) => For(level + 0.5);

    /// <summary>
    /// Text on a difficulty's color (or the softened fill made from it): the same hue, most of the way to
    /// near-black.
    /// </summary>
    public static Color Ink(Color difficultyColor) => SetlistColors.TierInk(difficultyColor);
}
