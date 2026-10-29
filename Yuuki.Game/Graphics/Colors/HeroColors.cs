// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Graphics.Colors;

/// <summary>
/// One hero's colors from the character sheet include the main swatch, a light one, and a deep one.
/// </summary>
public readonly record struct HeroSwatch(Color Main, Color Light, Color Deep);

/// <summary>
/// The Hero Club's colors
/// </summary>
public static class HeroColors
{
    public static readonly HeroSwatch YUUNA = swatch("#EC959B", "#D8C2B7", "#E73324");
    public static readonly HeroSwatch KARIN = swatch("#DF9179", "#6ADEDF", "#99554C");
    public static readonly HeroSwatch SONOKO = swatch("#CC5DA1", "#F5E5A5", "#8E5485");
    public static readonly HeroSwatch MIMORI = swatch("#517B92", "#9EC3CE", "#6A828D");
    public static readonly HeroSwatch ITSUKI = swatch("#B9CB78", "#DFF39F", "#699363");
    public static readonly HeroSwatch FUU = swatch("#E8BE58", "#F9EC7B", "#9D9367");

    /// <summary>
    /// Kaoruko Waguri colors, it's using in Play palette (don't tell anyone shhh)
    /// </summary>
    public static readonly HeroSwatch KAORUKO = swatch("#BA7F8C", "#CDA6A5", "#E5C5AF");

    /// <summary>
    /// The heroes in the order their colors cycle round the color wheel from Yuuna's pink.
    /// </summary>
    public static readonly Color[] CYCLE = { YUUNA.Main, FUU.Main, ITSUKI.Main, MIMORI.Main, SONOKO.Main, KARIN.Main };

    /// <summary>
    /// How many heroes <see cref="Cycle"/> moves through a second at speed 1.
    /// </summary>
    public const double CYCLE_PER_SECOND = 1.2;

    /// <summary>
    /// How much white the cycle's colors take by default, so they read as light and soft.
    /// </summary>
    public const float CYCLE_WHITE = 0.22f;

    /// <summary>
    /// The hero whose color is nearest a rank's each rank band and flower on the blossom ring wears it.
    /// </summary>
    public static HeroSwatch ForRank(ScoreRank rank) => rank switch
    {
        ScoreRank.S => FUU,
        ScoreRank.A => ITSUKI,
        ScoreRank.B => MIMORI,
        ScoreRank.C => SONOKO,
        _ => KARIN
    };

    /// <summary>
    /// The heroes' colors, in turn, blended smoothly from one to the next.
    /// </summary>
    /// <param name="time">How long the colors have been moving, in ms.</param>
    /// <param name="hue">Where on the cycle to start, in degrees. 60 is one hero on. Things side by side take different hues so the colors run across them.</param>
    /// <param name="white">How much white to mix in, 0–1.</param>
    /// <param name="speed">How fast it moves, as a multiple of <see cref="CYCLE_PER_SECOND"/>.</param>
    public static Color Cycle(double time, float hue = 0, float white = CYCLE_WHITE, double speed = 1)
    {
        double position = hue / 360.0 * CYCLE.Length + time / 1000 * CYCLE_PER_SECOND * speed;
        position %= CYCLE.Length;

        if (position < 0)
            position += CYCLE.Length;

        int index = (int)Math.Floor(position);
        Color color = CYCLE[index].Blend(CYCLE[(index + 1) % CYCLE.Length], (float)(position - index));

        return white > 0 ? color.Blend(Color.White, white) : color;
    }

    private static HeroSwatch swatch(string main, string light, string deep) =>
        new HeroSwatch(ColorExtensions.FromHex(main), ColorExtensions.FromHex(light), ColorExtensions.FromHex(deep));
}
