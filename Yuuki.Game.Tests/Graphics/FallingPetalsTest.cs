// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// The results screen's falling petals: how many, where, and how they move.
/// </summary>
[TestFixture]
public class FallingPetalsTest
{
    private const double frame = 1000 / 60.0;

    [Test]
    public void TheyStartAtTheRateAsked()
    {
        var fall = new FallingPetals { Rate = 20 };
        run(fall, 1000);

        // Twenty in the first second, none fallen out of a 1080-high screen yet.
        Assert.That(fall.Petals.Count, Is.InRange(19, 20));
    }

    [Test]
    public void NoneStartWithoutARate()
    {
        var fall = new FallingPetals();
        run(fall, 2000);

        Assert.That(fall.Petals, Is.Empty);
    }

    [Test]
    public void TheyNeverPassTheLimit()
    {
        var fall = new FallingPetals { Rate = 400, Limit = 50 };
        run(fall, 3000);

        Assert.That(fall.Petals.Count, Is.EqualTo(50));
    }

    /// <summary>
    /// They start above the screen across its whole width, upwind margins included, not in one corner.
    /// </summary>
    [Test]
    public void TheyStartAboveTheScreenAcrossItsWidth()
    {
        // Six hundred in a millisecond, so they've hardly moved.
        var fall = new FallingPetals { Rate = 600_000, Limit = 600 };
        fall.Step(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fall.Petals.All(p => p.Position.Y < 0), Is.True);
            Assert.That(fall.Petals.Count(p => p.Position.X < 960), Is.InRange(240, 360));
            Assert.That(fall.Petals.Min(p => p.Position.X), Is.LessThan(100));
            Assert.That(fall.Petals.Max(p => p.Position.X), Is.GreaterThan(1820));
        }
    }

    /// <summary>
    /// The far ones first, so the near ones are drawn over them.
    /// </summary>
    [Test]
    public void TheyAreKeptFarToNear()
    {
        var fall = new FallingPetals { Rate = 60 };
        run(fall, 4000);

        float[] depths = fall.Petals.Select(p => p.Depth).ToArray();
        Assert.That(depths, Is.Ordered);
    }

    /// <summary>
    /// Across a 1080-high screen in about 10–25 s, and gone once below it.
    /// </summary>
    [Test]
    public void TheyFallOffTheScreenAndAreLetGo()
    {
        var fall = new FallingPetals { Rate = 30 };
        run(fall, 1000);
        fall.Rate = 0;

        run(fall, 8000);
        Assert.That(fall.Petals, Is.Not.Empty, "still falling after 9 s");

        run(fall, 30000);
        Assert.That(fall.Petals, Is.Empty, "all fallen after 39 s");
    }

    /// <summary>
    /// The swing slows the fall at its ends and speeds it through the middle; on average a petal falls at its own speed.
    /// </summary>
    [Test]
    public void APetalFallsAtItsOwnSpeedOnAverage()
    {
        var fall = new FallingPetals { Rate = 1, Gustiness = 0 };
        fall.Step(1000);

        FallingPetals.Petal start = fall.Petals.Single();
        fall.Rate = 0;
        run(fall, 6000);

        FallingPetals.Petal end = fall.Petals.Single();
        float speed = (end.Position.Y - start.Position.Y) / 6000;

        Assert.That(speed, Is.EqualTo(start.FallSpeed).Within(start.FallSpeed * 0.2));
    }

    [Test]
    public void ThePointerPushesThemAside()
    {
        var pushed = new FallingPetals { Rate = 1, Gustiness = 0 };
        var left = new FallingPetals { Rate = 1, Gustiness = 0 };
        pushed.Step(1000);
        left.Step(1000);
        pushed.Rate = left.Rate = 0;

        // Just to the petal's left: it should be pushed right.
        FallingPetals.Petal petal = pushed.Petals.Single();
        pushed.Pointer = petal.DrawPosition - new Vector2(40, 0);

        run(pushed, 300);
        run(left, 300);

        Assert.That(pushed.Petals.Single().Position.X, Is.GreaterThan(left.Petals.Single().Position.X + 10));
    }

    [Test]
    public void ABlastThrowsThemOutward()
    {
        var fall = new FallingPetals { Rate = 200, Limit = 200 };
        run(fall, 1000);
        fall.Rate = 0;

        var middle = new Vector2(960, -60);
        float before = fall.Petals.Average(p => (p.Position - middle).Length());

        fall.Blast(middle, 0.9f);
        run(fall, 300);

        Assert.That(fall.Petals.Average(p => (p.Position - middle).Length()), Is.GreaterThan(before + 80));
    }

    [Test]
    public void TheSameSeedFallsTheSameWay()
    {
        var a = new FallingPetals(5) { Rate = 20 };
        var b = new FallingPetals(5) { Rate = 20 };
        run(a, 3000);
        run(b, 3000);

        Assert.That(a.Petals.Select(p => p.Position), Is.EqualTo(b.Petals.Select(p => p.Position)));
    }

    private static void run(FallingPetals fall, double duration)
    {
        for (double t = 0; t < duration; t += frame)
            fall.Step(Math.Min(frame, duration - t));
    }
}
