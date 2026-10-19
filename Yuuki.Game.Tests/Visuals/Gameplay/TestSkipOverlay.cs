// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Testing;
using Yuuki.Game.Gameplay.HUD;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// The intro skip overlay: its count-down drops one even step per beat and holds between beats, runs
/// out at the skip time, and a skip goes through only while one is possible. The look (the band, the
/// button, the bar stepping with the beat) is for the test browser's <see cref="TestWatchItCountDown"/>.
/// </summary>
public partial class TestSkipOverlay : TestScene
{
    // 120 BPM, with the skip ten beats after the start.
    private const double beat = 500;
    private const double start = -2000;
    private const double skip_time = 3000;

    private SkipOverlay overlay = null!;
    private double time;
    private bool running;
    private int skips;

    private void build(double beatLength)
    {
        AddStep("add overlay", () =>
        {
            Clear();
            time = start;
            running = false;
            skips = 0;

            Add(overlay = new SkipOverlay(start, skip_time, beatLength, () => time)
            {
                RequestSkip = () =>
                {
                    skips++;
                    time = skip_time;
                }
            });
        });
    }

    [Test]
    public void TestStepsOnTheBeat()
    {
        build(beat);

        AddAssert("full at the start", () => overlay.Remaining == 1);
        AddStep("just past the start", () => time = start + 1);
        AddAssert("still full", () => overlay.Remaining == 1);
        AddStep("first beat", () => time = start + beat);
        AddAssert("down one step", () => Math.Abs(overlay.Remaining - 0.9) < 1e-9);
        AddStep("between beats", () => time = start + beat * 1.8);
        AddAssert("holds the step", () => Math.Abs(overlay.Remaining - 0.9) < 1e-9);
        AddStep("fifth beat", () => time = start + beat * 5);
        AddAssert("half left", () => Math.Abs(overlay.Remaining - 0.5) < 1e-9);
        AddStep("the skip time", () => time = skip_time);
        AddAssert("empty", () => overlay.Remaining == 0);
        AddAssert("no longer skippable", () => !overlay.CanSkip);
    }

    [Test]
    public void TestSlidesWithoutTempo()
    {
        build(0);

        AddStep("halfway", () => time = (start + skip_time) / 2);
        AddAssert("half left", () => Math.Abs(overlay.Remaining - 0.5) < 1e-9);
        AddStep("a little later", () => time += 100);
        AddAssert("keeps sliding", () => overlay.Remaining < 0.5);
    }

    [Test]
    public void TestSkipOnlyWhilePossible()
    {
        build(beat);

        AddStep("pause", () => overlay.Active = false);
        AddAssert("no skip while paused", () => !overlay.TriggerSkip() && skips == 0);
        AddStep("resume", () => overlay.Active = true);
        AddAssert("skips", () => overlay.TriggerSkip() && skips == 1);
        AddAssert("only once", () => !overlay.TriggerSkip() && skips == 1);
    }

    [Test]
    public void TestWatchItCountDown()
    {
        build(beat);
        AddStep("run the clock", () => running = true);
    }

    public override void Update()
    {
        base.Update();

        if (running)
            time += Clock.ElapsedFrameTime;
    }
}
