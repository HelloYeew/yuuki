// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// The rail's rows run on to its ends, where the carousel's mask fades them, instead of each fading
/// whole before it gets there.
/// </summary>
[TestFixture]
public class RailCarouselLayoutTest
{
    private static readonly CarouselMetrics rail = new CarouselMetrics(new Vector2(600, 700));

    /// <summary>
    /// Five rows up, a 64-pitch row in a 700-tall rail is part over the top: it still draws, and the
    /// mask cuts and fades it.
    /// </summary>
    [Test]
    public void ARowOverTheEndStillDraws() =>
        Assert.That(new RailCarouselLayout().Compute(-5, rail).Alpha, Is.EqualTo(1));

    [Test]
    public void TheEndsFade()
    {
        var fade = new RailCarouselLayout().MaskFade;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fade.HasValue, Is.True);
            Assert.That(fade!.Value.Top, Is.GreaterThan(0));
            Assert.That(fade.Value.Bottom, Is.GreaterThan(0));
        }
    }

    /// <summary>
    /// The row passing under the focus window still fades, so none of it shows round the window's corners.
    /// </summary>
    [Test]
    public void TheRowUnderTheWindowStillFades() =>
        Assert.That(new RailCarouselLayout().Compute(0, rail).Alpha, Is.EqualTo(0));

    /// <summary>
    /// The strip's tiles still fade themselves: it isn't masked.
    /// </summary>
    [Test]
    public void TheStripIsntMasked() => Assert.That(new StripCarouselLayout().MaskFade.HasValue, Is.False);
}
