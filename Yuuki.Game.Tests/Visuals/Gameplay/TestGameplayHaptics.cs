// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Hits felt through the device's motor: a tap each, firmer the better the hit, none for a miss, and none at all
/// with haptics off or in a replay. The motor here only remembers what it was asked to play.
/// </summary>
public partial class TestGameplayHaptics : YuukiManualInputManagerTestScene
{
    private readonly RememberingHaptics motor = new RememberingHaptics();
    private GameplayContainer gameplay = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache<IPlatformHaptics>(motor);
    }

    [Test]
    public void TestBetterHitsAreFirmer()
    {
        play(true, tap(1000), tap(2000));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the first", () => gameplay.GameplayClock.Seek(1000));
        AddStep("hit it on time", () => PressKeyOnce(Key.D));
        AddStep("seek 100 ms past the second", () => gameplay.GameplayClock.Seek(2100));
        AddStep("hit it late", () => PressKeyOnce(Key.D));
        AddAssert("both felt, the first firmer", () => motor.Taps.Count == 2 && motor.Taps[0].Intensity > motor.Taps[1].Intensity);
    }

    [Test]
    public void TestAMissIsntFelt()
    {
        play(true, tap(1000));
        AddStep("start", () => gameplay.Restart());
        // Past its window, but not so far it's gone from the field before it's judged.
        AddStep("seek past it", () => gameplay.GameplayClock.Seek(1250));
        AddUntilStep("missed", () => gameplay.BuildResult().MissCount == 1);
        AddAssert("nothing felt", () => motor.Taps.Count == 0);
    }

    [Test]
    public void TestOffFeelsNothing()
    {
        play(false, tap(1000));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to it", () => gameplay.GameplayClock.Seek(1000));
        AddStep("hit it", () => PressKeyOnce(Key.D));
        AddAssert("hit, not felt", () => gameplay.BuildResult().CriticalCount == 1 && motor.Taps.Count == 0);
    }

    [Test]
    public void TestAReplayIsntFelt()
    {
        var chart = new PlayableBeatmap(new[] { tap(300) }, laneCount: 4);

        AddStep("a replay", () =>
        {
            motor.Taps.Clear();
            TestContent.Add(gameplay = new GameplayContainer(chart)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                LeadInTime = 0,
                Haptics = true,
                Replay = new ReplayData
                {
                    ChartHash = ReplayData.ChartHashOf(chart),
                    Inputs = new List<ReplayInput> { new ReplayInput(300, ReplayInputKind.LanePress, 0), new ReplayInput(340, ReplayInputKind.LaneRelease, 0) }
                }
            });
        });
        AddStep("start", () => gameplay.Restart());
        // Playback runs on a real-time stopwatch, which a headless step timeout outpaces.
        AddUntilStep("judged", () => gameplay.AllJudged, 60_000);
        AddAssert("hit, not felt", () => gameplay.BuildResult().CriticalCount == 1 && motor.Taps.Count == 0);
    }

    private static BeatmapNote tap(double time) => new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = time, EndTime = time };

    private void play(bool haptics, params BeatmapNote[] notes) => AddStep(haptics ? "a chart, felt" : "a chart, not felt", () =>
    {
        motor.Taps.Clear();
        TestContent.Add(gameplay = new GameplayContainer(new PlayableBeatmap(notes, laneCount: 4))
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            LeadInTime = 0,
            Haptics = haptics
        });
    });

    private class RememberingHaptics : IPlatformHaptics
    {
        public readonly List<(float Intensity, float Sharpness)> Taps = new List<(float Intensity, float Sharpness)>();

        public bool IsSupported => true;

        public void PlayTap(float intensity, float sharpness) => Taps.Add((intensity, sharpness));
    }
}
