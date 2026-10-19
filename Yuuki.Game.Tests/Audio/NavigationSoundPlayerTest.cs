// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Yuuki.Game.Audio;

namespace Yuuki.Game.Tests.Audio;

[TestFixture]
public class NavigationSoundPlayerTest
{
    private NavigationSoundPlayer player = null!;

    [SetUp]
    public void SetUp() => player = new NavigationSoundPlayer(null, null, new Random(1234));

    [Test]
    public void TestLoneTickIsFullVolumeNearNaturalPitch()
    {
        var tick = player.GetTick(1, true, 1000);

        Assert.That(tick, Is.Not.Null);
        Assert.That(tick!.Value.Direction, Is.EqualTo(1));
        Assert.That(tick.Value.Volume, Is.EqualTo(1).Within(1e-9));
        Assert.That(tick.Value.Frequency, Is.InRange(0.985, 1.015));
    }

    [Test]
    public void TestTicksTooCloseTogetherAreDropped()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(player.GetTick(1, true, 1000), Is.Not.Null);
            Assert.That(player.GetTick(1, true, 1000 + NavigationSoundPlayer.MIN_INTERVAL - 1), Is.Null);
            Assert.That(player.GetTick(-1, true, 1000 + NavigationSoundPlayer.MIN_INTERVAL), Is.Not.Null);
        }
    }

    [Test]
    public void TestFastRunGetsQuieterThenRecovers()
    {
        double volume = 1;

        // A fast mouse wheelspin: a step every 50 ms for a second.
        for (int i = 0; i < 20; i++)
        {
            var tick = player.GetTick(1, true, 1000 + i * 50);
            Assert.That(tick, Is.Not.Null);
            Assert.That(tick!.Value.Volume, Is.LessThanOrEqualTo(volume + 1e-9), "each tick in a run is no louder than the last");
            volume = tick.Value.Volume;
        }

        Assert.That(volume, Is.InRange(0.45, 0.6), "a long fast run settles at about half volume");

        var afterPause = player.GetTick(1, true, 1000 + 19 * 50 + 1500);
        Assert.That(afterPause!.Value.Volume, Is.GreaterThan(0.99), "a pause brings it back to full");
    }

    [Test]
    public void TestRepeatsVaryInPitch()
    {
        var first = player.GetTick(1, true, 1000)!.Value;
        var second = player.GetTick(1, true, 2000)!.Value;

        Assert.That(second.Frequency, Is.Not.EqualTo(first.Frequency));
    }

    [Test]
    public void TestEndStopIsLowerAndSofter()
    {
        var moved = player.GetTick(-1, true, 1000)!.Value;
        var stopped = player.GetTick(-1, false, 3000)!.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopped.Frequency, Is.LessThan(0.85));
            Assert.That(stopped.Volume, Is.LessThan(moved.Volume));
        }
    }
}
