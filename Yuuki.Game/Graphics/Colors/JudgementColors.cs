// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Graphics.Colors;

public static class JudgementColors
{
    public static readonly Color CRITICAL = ColorExtensions.FromHex("#E0A030");
    public static readonly Color BREAK = ColorExtensions.FromHex("#4E8FD9");
    public static readonly Color HIT = ColorExtensions.FromHex("#4FA36B");
    public static readonly Color MISS = ColorExtensions.FromHex("#D9534F");

    public static Color For(HitResult result) => result switch
    {
        HitResult.Critical => CRITICAL,
        HitResult.Break => BREAK,
        HitResult.Hit => HIT,
        _ => MISS
    };

    public static Color For(ScoreRank rank) => rank switch
    {
        ScoreRank.S => CRITICAL,
        ScoreRank.A => HIT,
        ScoreRank.B => BREAK,
        ScoreRank.C => ColorExtensions.FromHex("#9B6BD9"),
        _ => MISS
    };
}
