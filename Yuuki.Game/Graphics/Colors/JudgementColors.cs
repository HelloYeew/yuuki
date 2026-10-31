// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Graphics.Colors;

public static class JudgementColors
{
    /// <summary>
    /// A Bloom, where one color has to do (a dot on a graph, a burst of light). Shown on its own, a Bloom takes the
    /// Hero Club's rainbow (<see cref="RainbowColors"/>).
    /// </summary>
    public static readonly Color BLOOM = ColorExtensions.FromHex("#F07FB0");

    public static readonly Color CRITICAL = ColorExtensions.FromHex("#E0A030");
    public static readonly Color BREAK = ColorExtensions.FromHex("#4E8FD9");
    public static readonly Color HIT = ColorExtensions.FromHex("#4FA36B");
    public static readonly Color MISS = ColorExtensions.FromHex("#D9534F");

    /// <summary>
    /// A full combo, wherever it's shown. Its own color, apart from every rank's (it used to be
    /// <see cref="BREAK"/>, which is B's), so "B | FC" doesn't read as one blue block.
    /// </summary>
    public static readonly Color FULL_COMBO = ColorExtensions.FromHex("#2EC4D6");

    /// <summary>
    /// An all perfect, wherever it's shown. Its own color (it used to be <see cref="CRITICAL"/>, which is
    /// S's, and an all perfect is always an S).
    /// </summary>
    public static readonly Color ALL_PERFECT = ColorExtensions.FromHex("#E8649E");

    /// <summary>
    /// SS: Yuuna's color.
    /// </summary>
    public static readonly Color SS = HeroColors.YUUNA.Main;

    private static readonly Color bloom_deep = BLOOM.Darken(0.35f);
    private static readonly Color critical_deep = CRITICAL.Darken(0.35f);
    private static readonly Color break_deep = BREAK.Darken(0.35f);
    private static readonly Color hit_deep = HIT.Darken(0.35f);
    private static readonly Color miss_deep = MISS.Darken(0.35f);

    /// <summary>
    /// A judgement's color for a small mark on a graph, over the bands tinted with those colors. The color itself can't be
    /// told from a tint of itself (1.1:1 for a Bloom on its own band): these are the same hues deep enough to stand out, 2.3:1
    /// or more.
    /// </summary>
    public static Color MarkFor(HitResult result) => result switch
    {
        HitResult.Bloom => bloom_deep,
        HitResult.Critical => critical_deep,
        HitResult.Break => break_deep,
        HitResult.Hit => hit_deep,
        _ => miss_deep
    };

    public static Color For(HitResult result) => result switch
    {
        HitResult.Bloom => BLOOM,
        HitResult.Critical => CRITICAL,
        HitResult.Break => BREAK,
        HitResult.Hit => HIT,
        _ => MISS
    };

    public static Color For(ScoreRank rank) => rank switch
    {
        ScoreRank.SS => SS,
        ScoreRank.S => CRITICAL,
        ScoreRank.A => HIT,
        ScoreRank.B => BREAK,
        ScoreRank.C => ColorExtensions.FromHex("#9B6BD9"),
        _ => MISS
    };
}
