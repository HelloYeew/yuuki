// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;

namespace Yuuki.Game.Graphics.Colors;

/// <summary>
/// The Hero Club rainbow: all six heroes side by side, BLOOM's own color. It moves wherever it's shown
/// (<see cref="UserInterface.RainbowFill"/>, <see cref="UserInterface.RainbowText"/>).
/// </summary>
public static class RainbowColors
{
    /// <summary>
    /// The rainbow as a fill, light enough for dark ink on it. It ends where it starts, so it repeats seamlessly.
    /// </summary>
    public static readonly Color[] PASTEL = fromHex("#F6B3BB", "#F9E27A", "#DFF39F", "#9EC3CE", "#E59AC8", "#F2B39F", "#F6B3BB");

    /// <summary>
    /// The same heroes, deep enough to read as letters on a light surface.
    /// </summary>
    public static readonly Color[] INK = fromHex("#D9637A", "#C99A2E", "#7E9B3E", "#517B92", "#B9488E", "#C9705A", "#D9637A");

    /// <summary>
    /// Ink for text on <see cref="PASTEL"/>.
    /// </summary>
    public static readonly Color INK_ON_PASTEL = ColorExtensions.FromHex("#3A2236");

    /// <summary>
    /// How long the rainbow takes to move by one whole run of its colors, in ms.
    /// </summary>
    public const double PERIOD = 2500;

    /// <summary>
    /// How many times wider than what it fills one run of the colors is, so only part of the rainbow shows at once.
    /// </summary>
    public const float PATTERN_WIDTH = 3;

    /// <summary>
    /// The color at <paramref name="position"/> along one run of <paramref name="stops"/> (0–1, wrapping), blended
    /// between the stops either side as a CSS gradient does.
    /// </summary>
    public static Color Sample(Color[] stops, double position)
    {
        position %= 1;

        if (position < 0)
            position += 1;

        double scaled = position * (stops.Length - 1);
        int index = Math.Min((int)Math.Floor(scaled), stops.Length - 2);

        return stops[index].Blend(stops[index + 1], (float)(scaled - index));
    }

    /// <summary>
    /// How far the rainbow has moved after <paramref name="time"/> ms, in runs of its colors (0–1).
    /// </summary>
    public static double Phase(double time, double speed = 1)
    {
        double phase = time * speed / PERIOD % 1;
        return phase < 0 ? phase + 1 : phase;
    }

    private static Color[] fromHex(params string[] hex) => Array.ConvertAll(hex, ColorExtensions.FromHex);
}
