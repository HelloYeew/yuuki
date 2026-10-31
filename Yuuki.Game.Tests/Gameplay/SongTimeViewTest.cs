// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Gameplay;

[TestFixture]
public class SongTimeViewTest
{
    private SongTimeView view = null!;

    [SetUp]
    public void SetUp() => view = new SongTimeView();

    [Test]
    public void ItStartsWithAllOfTheSong()
    {
        Assert.Multiple(() =>
        {
            Assert.That(view.Zoom, Is.EqualTo(1));
            Assert.That(view.Level, Is.Zero);
            Assert.That(view.Start, Is.Zero);
            Assert.That(view.CanZoomOut, Is.False);
            Assert.That(view.CanZoomIn);
            Assert.That(view.CanMoveBack, Is.False);
            Assert.That(view.CanMoveOn, Is.False);
        });
    }

    [Test]
    public void AllOfTheSongHasNowhereToMove()
    {
        int before = view.Version;

        view.Move(0.3);

        Assert.Multiple(() =>
        {
            Assert.That(view.Start, Is.Zero);
            Assert.That(view.Version, Is.EqualTo(before), "nothing to draw again");
        });
    }

    [TestCase(1, 2, 0.25)]
    [TestCase(2, 4, 0.375)]
    public void ZoomingInKeepsTheMiddle(int steps, int zoom, double start)
    {
        for (int i = 0; i < steps; i++)
            view.ZoomIn();

        Assert.Multiple(() =>
        {
            Assert.That(view.Zoom, Is.EqualTo(zoom));
            Assert.That(view.Middle, Is.EqualTo(0.5));
            Assert.That(view.Start, Is.EqualTo(start));
        });
    }

    [Test]
    public void ItStopsAtEachEndOfTheZoom()
    {
        for (int i = 0; i < 9; i++)
            view.ZoomIn();

        Assert.Multiple(() =>
        {
            Assert.That(view.Zoom, Is.EqualTo(4));
            Assert.That(view.CanZoomIn, Is.False);
            Assert.That(view.CanZoomOut);
        });

        for (int i = 0; i < 9; i++)
            view.ZoomOut();

        Assert.Multiple(() =>
        {
            Assert.That(view.Zoom, Is.EqualTo(1));
            Assert.That(view.CanZoomOut, Is.False);
        });
    }

    [Test]
    public void ItStopsAtEachEndOfTheSong()
    {
        view.ZoomIn();

        view.Move(5);

        Assert.Multiple(() =>
        {
            Assert.That(view.Start, Is.EqualTo(0.5));
            Assert.That(view.CanMoveOn, Is.False);
            Assert.That(view.CanMoveBack);
        });

        view.Move(-5);

        Assert.Multiple(() =>
        {
            Assert.That(view.Start, Is.Zero);
            Assert.That(view.CanMoveBack, Is.False);
            Assert.That(view.CanMoveOn);
        });
    }

    [Test]
    public void MovingByTheViewIsAShareOfWhatsShown()
    {
        view.ZoomIn();
        view.ZoomIn();

        view.MoveByView(0.5);

        Assert.That(view.Middle, Is.EqualTo(0.5 + 0.125));
    }

    /// <summary>
    /// Zooming out near an end comes back in from it, rather than showing past the song.
    /// </summary>
    [Test]
    public void ZoomingOutAtAnEndStaysOnTheSong()
    {
        view.ZoomIn();
        view.ZoomIn();
        view.Move(5);

        view.ZoomOut();

        Assert.Multiple(() =>
        {
            Assert.That(view.Zoom, Is.EqualTo(2));
            Assert.That(view.Start, Is.EqualTo(0.5), "the last half");
        });

        view.ZoomOut();

        Assert.That(view.Start, Is.Zero);
    }

    [Test]
    public void EachChangeSaysSo()
    {
        int version = view.Version;

        view.ZoomIn();
        Assert.That(view.Version, Is.GreaterThan(version));
        version = view.Version;

        view.Move(0.1);
        Assert.That(view.Version, Is.GreaterThan(version));
    }
}
