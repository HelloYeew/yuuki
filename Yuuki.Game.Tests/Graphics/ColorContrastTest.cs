// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// Contrast as WCAG measures it, and dark ink that reads on a fill.
/// </summary>
[TestFixture]
public class ColorContrastTest
{
    [Test]
    public void TheEnds()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ColorContrast.Ratio(Color.Black, Color.White), Is.EqualTo(21).Within(0.01));
            Assert.That(ColorContrast.Ratio(Color.White, Color.Black), Is.EqualTo(21).Within(0.01));
            Assert.That(ColorContrast.Ratio(ColorExtensions.FromHex("#B4B4C4"), ColorExtensions.FromHex("#B4B4C4")), Is.EqualTo(1));
        }
    }

    /// <summary>
    /// As measured elsewhere: the play palette's text on its surface (ResultsInk), and the results buttons' old label,
    /// the fill at half its brightness.
    /// </summary>
    [Test]
    public void KnownPairs()
    {
        Color surface = ColorExtensions.FromHex("#B4B4C4");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ColorContrast.Ratio(ColorExtensions.FromHex("#393947"), surface), Is.EqualTo(5.55).Within(0.01));
            Assert.That(ColorContrast.Ratio(surface.Darken(0.5f), surface), Is.EqualTo(3.34).Within(0.01));
        }
    }

    /// <summary>
    /// Every fill a menu button has in the game: the ink reads on it, and is no darker than it needs to be (the ink is
    /// darkened a hundredth at a time, so it lands just past what text wants).
    /// </summary>
    [Test]
    public void InkReadsOnEveryButtonsFill()
    {
        Color[] fills = DefaultYuukiColorProvider.PALETTES.Values
                                                 .SelectMany(p => new[] { p.Accent, p.Surface, p.TextLight, p.TextMuted })
                                                 .Append(UserInterfaceConstant.DangerColor)
                                                 .ToArray();

        using (Assert.EnterMultipleScope())
        {
            foreach (Color fill in fills)
            {
                double ratio = ColorContrast.Ratio(ColorContrast.DarkInk(fill, 0.5f), fill);
                Assert.That(ratio, Is.GreaterThanOrEqualTo(ColorContrast.TEXT), $"on {fill}");
                Assert.That(ratio, Is.LessThan(ColorContrast.TEXT + 0.3), $"on {fill}");
            }
        }
    }

    /// <summary>
    /// A softer ink, opaque: the song select panel's artist on its title row (the play palette's surface, lightened)
    /// goes as far toward the row as still reads (17%, #50505D, 4.54:1), and a step further wouldn't; and never past
    /// the most it's allowed.
    /// </summary>
    [Test]
    public void SoftInkStopsWhereItStillReads()
    {
        YuukiColorPalette play = DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);
        Color row = play.Surface.Lighten(0.2f);
        Color soft = ColorContrast.SoftInk(play.Text, row, 0.25f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ColorContrast.Ratio(soft, row), Is.GreaterThanOrEqualTo(ColorContrast.TEXT));
            Assert.That(soft, Is.EqualTo(play.Text.Blend(row, 0.17f)));
            Assert.That(ColorContrast.Ratio(play.Text.Blend(row, 0.18f), row), Is.LessThan(ColorContrast.TEXT));
            Assert.That(ColorContrast.SoftInk(play.Text, row, 0.05f), Is.EqualTo(play.Text.Blend(row, 0.05f)), "no further than the most");
        }
    }

    /// <summary>
    /// Where no shade of the fill reaches it (a fill too dark for dark ink), black: as much as dark ink can do.
    /// </summary>
    [Test]
    public void BlackWhereNothingReaches()
    {
        Color fill = ColorExtensions.FromHex("#595959");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ColorContrast.Ratio(Color.Black, fill), Is.LessThan(ColorContrast.TEXT));
            Color ink = ColorContrast.DarkInk(fill, 0.5f);
            Assert.That(ink.R + ink.G + ink.B, Is.Zero);
        }
    }
}
