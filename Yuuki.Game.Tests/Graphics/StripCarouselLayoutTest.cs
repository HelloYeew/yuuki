// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// The strip's tiles fade out before the room kept at its start (where the docked song sits), as they do
/// at its ends.
/// </summary>
[TestFixture]
public class StripCarouselLayoutTest
{
    private static readonly Vector2 strip_size = new Vector2(1280, 320);

    [Test]
    public void ATileUnderTheStartInsetFades()
    {
        var layout = new StripCarouselLayout();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Compute(-3, new CarouselMetrics(strip_size)).Alpha, Is.EqualTo(1));
            Assert.That(layout.Compute(-3, new CarouselMetrics(strip_size, startInset: 200)).Alpha, Is.EqualTo(0));
        }
    }

    [Test]
    public void TheOtherEndIsntAffected() =>
        Assert.That(new StripCarouselLayout().Compute(3, new CarouselMetrics(strip_size, startInset: 200)).Alpha, Is.EqualTo(1));
}
