// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// A press's dot on the timing graph has to be seen over the bands behind it, on any palette's card.
/// </summary>
[TestFixture]
public class JudgementColorsTest
{
    /// <summary>
    /// The least the marks give over a band. The colors themselves give 1.08 to 1.72, and are all but lost.
    /// </summary>
    private const double least = 2.3;

    // The bands as the timing graph tints them.
    private static readonly (HitResult Result, float Alpha)[] bands =
    {
        (HitResult.Bloom, 0.20f),
        (HitResult.Critical, 0.17f),
        (HitResult.Break, 0.12f),
        (HitResult.Hit, 0.11f),
        (HitResult.Miss, 0.10f)
    };

    private static IEnumerable<string> surfaces() => new[] { "#B4B4C4", "#B4C4C4", "#B5C4B4", "#C4C3B4", "#E4C4A4", "#FFFFFF" };

    [Test]
    public void AMarkStandsOutFromItsOwnBand([ValueSource(nameof(surfaces))] string surface)
    {
        Assert.Multiple(() =>
        {
            foreach ((HitResult result, float alpha) in bands)
            {
                Color band = ColorExtensions.FromHex(surface).Blend(JudgementColors.For(result), alpha);
                Assert.That(contrast(JudgementColors.MarkFor(result), band), Is.GreaterThanOrEqualTo(least), result.ToString);
            }
        });
    }

    /// <summary>
    /// Most presses land on the Bloom band, whatever they got: a Critical just off its edge is drawn there too, zoomed or not.
    /// </summary>
    [Test]
    public void EveryMarkStandsOutFromTheBloomBand([ValueSource(nameof(surfaces))] string surface)
    {
        Color band = ColorExtensions.FromHex(surface).Blend(JudgementColors.For(HitResult.Bloom), 0.20f);

        Assert.Multiple(() =>
        {
            foreach ((HitResult result, _) in bands)
                Assert.That(contrast(JudgementColors.MarkFor(result), band), Is.GreaterThanOrEqualTo(least), result.ToString);
        });
    }

    /// <summary>
    /// Deep as they are, they keep to the hue the chips and the rest of the screen show.
    /// </summary>
    [Test]
    public void AMarkKeepsItsJudgementsHue()
    {
        Assert.Multiple(() =>
        {
            foreach ((HitResult result, _) in bands)
            {
                float apart = Math.Abs(hueOf(JudgementColors.For(result)) - hueOf(JudgementColors.MarkFor(result)));
                Assert.That(Math.Min(apart, 1 - apart), Is.LessThan(15f / 360), result.ToString);
            }
        });
    }

    private static float hueOf(Color color)
    {
        ColorExtensions.ToHSL(color, out float hue, out _, out _);
        return hue;
    }

    private static double contrast(Color a, Color b)
    {
        double la = luminance(a), lb = luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double luminance(Color c) =>
        0.2126 * ColorExtensions.SrgbToLinear(c.R) + 0.7152 * ColorExtensions.SrgbToLinear(c.G) + 0.0722 * ColorExtensions.SrgbToLinear(c.B);
}
