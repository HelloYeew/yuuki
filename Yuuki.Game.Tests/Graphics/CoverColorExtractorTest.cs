// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// Picking the colour a song's focus window wears from its cover's decoded pixels.
/// </summary>
[TestFixture]
public class CoverColorExtractorTest
{
    private static readonly byte[] sky_blue = [40, 110, 230];
    private static readonly byte[] sunset_orange = [245, 140, 30];

    /// <summary>
    /// Fills a <paramref name="size"/>² image with <paramref name="background"/>, and its top
    /// <paramref name="share"/> of rows with <paramref name="foreground"/>.
    /// </summary>
    private static byte[] image(int size, byte[] background, byte[] foreground, float share)
    {
        byte[] pixels = new byte[size * size * 4];
        int foregroundRows = (int)Math.Round(size * share);

        for (int y = 0; y < size; y++)
        {
            byte[] c = y < foregroundRows ? foreground : background;

            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                pixels[i] = c[0];
                pixels[i + 1] = c[1];
                pixels[i + 2] = c[2];
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] grey(byte level) => [level, level, level];

    /// <summary>
    /// The reason it votes instead of averaging: blue and orange average to a muddy grey-brown, which is
    /// neither colour on the cover.
    /// </summary>
    [Test]
    public void TheLargerHueWinsInsteadOfMixing()
    {
        var swatch = CoverColorExtractor.Extract(image(40, sky_blue, sunset_orange, 0.3f), 40, 40);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(swatch.IsNeutral, Is.False);
            Assert.That(swatch.Fill.B, Is.GreaterThan(swatch.Fill.R), "the sky, which covers most of it");
            Assert.That(swatch.Fill.B - swatch.Fill.R, Is.GreaterThan(30), "a clear blue, not a blend");
        }
    }

    [Test]
    public void GreyscaleArtIsNeutral()
    {
        var swatch = CoverColorExtractor.Extract(image(40, grey(30), grey(200), 0.5f), 40, 40);

        Assert.That(swatch.IsNeutral, Is.True);
    }

    /// <summary>
    /// A dark cover with a small bright accent is still coloured: what counts is the share of pixels that
    /// carry a hue, not the average.
    /// </summary>
    [Test]
    public void ASmallAccentOnADarkCoverStillCounts()
    {
        var swatch = CoverColorExtractor.Extract(image(40, grey(12), [230, 40, 60], 0.1f), 40, 40);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(swatch.IsNeutral, Is.False);
            Assert.That(swatch.Fill.R, Is.GreaterThan(swatch.Fill.G).And.GreaterThan(swatch.Fill.B), "the red accent");
        }
    }

    [Test]
    public void TheInkReadsOnTheFill()
    {
        var swatch = CoverColorExtractor.Extract(image(40, sky_blue, sunset_orange, 0.5f), 40, 40);

        Assert.That(contrast(swatch.Fill, swatch.Ink), Is.GreaterThanOrEqualTo(7), "a title in the ink has to read like body text");
    }

    /// <summary>
    /// It samples a grid instead of every pixel, so a large decode costs the same as a small one and gives
    /// the same answer.
    /// </summary>
    [Test]
    public void ALargeImageGivesTheSameAnswerAsASmallOne()
    {
        var small = CoverColorExtractor.Extract(image(40, sky_blue, sunset_orange, 0.3f), 40, 40);
        var large = CoverColorExtractor.Extract(image(800, sky_blue, sunset_orange, 0.3f), 800, 800);

        Assert.That(large, Is.EqualTo(small));
    }

    [Test]
    public void BadInputIsNeutralRatherThanAnError()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CoverColorExtractor.Extract([], 0, 0).IsNeutral, Is.True);
            Assert.That(CoverColorExtractor.Extract(new byte[16], 40, 40).IsNeutral, Is.True, "fewer bytes than the size says");
        }
    }

    [Test]
    public void TheStoreKeepsWhatWasPicked()
    {
        var store = new CoverColorStore();
        int before = store.Version;

        store.Pick("cover", image(8, sky_blue, sky_blue, 0), 8, 8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Version, Is.GreaterThan(before), "anything waiting on a colour has to know to look again");
            Assert.That(store.TryGet("cover", out var swatch), Is.True);
            Assert.That(swatch.IsNeutral, Is.False);
            Assert.That(store.Contains("another cover"), Is.False);
        }
    }

    private static double contrast(Color a, Color b)
    {
        double la = luminance(a), lb = luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double luminance(Color c) => 0.2126 * linear(c.R) + 0.7152 * linear(c.G) + 0.0722 * linear(c.B);

    private static double linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
