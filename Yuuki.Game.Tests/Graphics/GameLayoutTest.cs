// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Graphics;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// Which layout the game picks and how far it zooms: from the window's height in screen points and the
/// UI scale.
/// </summary>
[TestFixture]
public class GameLayoutTest
{
    /// <summary>
    /// A 6.5" phone held sideways is about 412 points tall.
    /// </summary>
    [Test]
    public void AShortWindowIsAPhones() =>
        Assert.That(GameLayout.IsPhoneWindow(412, wasPhoneWindow: false), Is.True);

    [Test]
    public void ATallWindowIsNot() =>
        Assert.That(GameLayout.IsPhoneWindow(900, wasPhoneWindow: true), Is.False);

    /// <summary>
    /// Between the two lines the window stays what it was, so one resized across a line doesn't flicker
    /// between layouts.
    /// </summary>
    [Test]
    public void BetweenTheLinesItStays()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GameLayout.IsPhoneWindow(600, wasPhoneWindow: false), Is.False);
            Assert.That(GameLayout.IsPhoneWindow(600, wasPhoneWindow: true), Is.True);
        }
    }

    /// <summary>
    /// A phone's window gets the phone layout, and so does a desktop's from a UI scale of 1.4 up.
    /// </summary>
    [Test]
    public void TheWindowAndTheZoomPickTheLayout()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GameLayout.Choose(phoneWindow: true, zoom: 1.5), Is.EqualTo(DisplayLayout.Phone));
            Assert.That(GameLayout.Choose(phoneWindow: false, zoom: 1), Is.EqualTo(DisplayLayout.Desktop));
            Assert.That(GameLayout.Choose(phoneWindow: false, zoom: GameLayout.COMPACT_ZOOM), Is.EqualTo(DisplayLayout.Phone));
        }
    }

    /// <summary>
    /// A phone starts at 1.5× (a 1280×720 canvas); the UI scale multiplies that, and nothing zooms past the
    /// smallest canvas the layouts fit.
    /// </summary>
    [Test]
    public void TheZoom()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GameLayout.Zoom(phoneWindow: false, uiScale: 1.2), Is.EqualTo(1.2).Within(1e-9));
            Assert.That(GameLayout.Zoom(phoneWindow: true, uiScale: 1), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(GameLayout.Zoom(phoneWindow: true, uiScale: 0.6), Is.EqualTo(0.9).Within(1e-9));
            Assert.That(GameLayout.Zoom(phoneWindow: true, uiScale: 1.75), Is.EqualTo(GameLayout.MAX_ZOOM).Within(1e-9));
        }
    }
}
