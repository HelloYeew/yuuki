// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;

namespace Yuuki.Game.Graphics.Colors;

/// <summary>
/// How readable one color is on another, as WCAG measures it: the ratio of their relative luminances (each plus 0.05),
/// from 1 (the same) to 21 (black on white). Text wants <see cref="TEXT"/>. Colors are taken as opaque.
/// </summary>
/// <remarks>
/// Sakura's ColorExtensions has GetRelativeLuminance and GetContrastRatio from its release after 2026.1101.0; once yuuki
/// is on it, <see cref="Luminance"/> and <see cref="Ratio"/> go, and the inks use those.
/// </remarks>
public static class ColorContrast
{
    /// <summary>
    /// What text wants against what it's on.
    /// </summary>
    public const double TEXT = 4.5;

    /// <summary>
    /// How light a color is to the eye, 0 (black) to 1 (white): its channels in linear light, weighted. Not Sakura's
    /// <see cref="ColorExtensions.GetBrightness"/>, which weighs the channels as they're stored, before linear light.
    /// </summary>
    public static double Luminance(Color color) =>
        0.2126 * ColorExtensions.SrgbToLinear(color.R) + 0.7152 * ColorExtensions.SrgbToLinear(color.G) + 0.0722 * ColorExtensions.SrgbToLinear(color.B);

    /// <summary>
    /// The contrast between two colors, whichever is lighter: 1 to 21.
    /// </summary>
    public static double Ratio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Dark text for <paramref name="background"/>, in its own hue: the background darkened by <paramref name="darken"/>,
    /// or as much further as it takes to reach <paramref name="ratio"/> against it (black, at the most).
    /// </summary>
    public static Color DarkInk(Color background, float darken, double ratio = TEXT)
    {
        for (int step = 0; darken + step * 0.01f < 1; step++)
        {
            Color ink = background.Darken(darken + step * 0.01f);

            if (Ratio(ink, background) >= ratio)
                return ink;
        }

        return background.Darken(1);
    }

    /// <summary>
    /// <paramref name="ink"/> softened toward <paramref name="background"/>, a hundredth at a time and at most
    /// <paramref name="most"/> of the way, for as long as it still reaches <paramref name="ratio"/> against it. Opaque on
    /// purpose: a faded ink is blended in linear light, so it comes out far lighter than its alpha suggests (the play
    /// palette's ink at 90% on the song select panel drew at 4.15:1, where the same blend worked out in sRGB is 5.23:1).
    /// </summary>
    public static Color SoftInk(Color ink, Color background, float most, double ratio = TEXT)
    {
        Color softest = ink;

        for (int step = 1; step / 100f <= most + 1e-4f; step++)
        {
            Color softer = ink.Blend(background, step / 100f);

            if (Ratio(softer, background) < ratio)
                break;

            softest = softer;
        }

        return softest;
    }
}
