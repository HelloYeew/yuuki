// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// A hold that changes lanes has to be followed: any key in the lanes it crosses keeps it held (roll
/// across, or jump to the end key), and its tail is judged when it's let go from its end lane. Let go
/// anywhere else at the end and it wasn't followed: a Miss. Inputs are fed at exact times through the
/// replay path, which judges them as live presses.
/// </summary>
public partial class TestCrossLaneHold : TestScene
{
    private GameplayContainer gameplay = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    /// <summary>Lane 1 to lane 3 (indices 0 -> 2), held from 300 to 900 ms.</summary>
    private static PlayableBeatmap crossing() => new PlayableBeatmap(new[]
    {
        new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane1, EndLane = NoteLane.Lane3, StartTime = 300, EndTime = 900 }
    }, laneCount: 4);

    private static PlayableBeatmap straight() => new PlayableBeatmap(new[]
    {
        new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane2, StartTime = 300, EndTime = 900 }
    }, laneCount: 4);

    private static ReplayInput press(double time, int lane) => new ReplayInput(time, ReplayInputKind.LanePress, lane);
    private static ReplayInput release(double time, int lane) => new ReplayInput(time, ReplayInputKind.LaneRelease, lane);

    private void play(PlayableBeatmap chart, params ReplayInput[] inputs)
    {
        AddStep("play", () =>
        {
            Clear();
            Add(gameplay = new GameplayContainer(chart)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                LeadInTime = 0,
                Replay = new ReplayData { ChartHash = ReplayData.ChartHashOf(chart), Inputs = inputs.ToList() }
            });
        });
        AddStep("start at double speed", () =>
        {
            gameplay.Restart();
            gameplay.Rate = 2;
        });
        // Playback runs on a real-time stopwatch, which a headless step timeout outpaces.
        AddUntilStep("judged", () => gameplay.AllJudged, 60_000);
    }

    private IEnumerable<HitResult> results => gameplay.BuildResult().Judgements.Select(j => j.Result);

    [Test]
    public void TestRollingAcross()
    {
        play(crossing(),
            press(300, 0), press(500, 1), release(520, 0), press(700, 2), release(720, 1), release(900, 2));

        AddAssert("head and tail on time", () => results.SequenceEqual(new[] { HitResult.Critical, HitResult.Critical }));
        AddAssert("never broken", () => gameplay.BuildResult().HoldBreakCount == 0);
        AddAssert("the character followed it", () => gameplay.CharacterLane == 2);
    }

    [Test]
    public void TestJumpingToTheEndKey()
    {
        play(crossing(), press(300, 0), press(850, 2), release(860, 0), release(900, 2));
        AddAssert("head and tail on time", () => results.SequenceEqual(new[] { HitResult.Critical, HitResult.Critical }));
    }

    [Test]
    public void TestNotFollowedIsAMiss()
    {
        play(crossing(), press(300, 0), release(900, 0));
        AddAssert("the tail misses", () => results.SequenceEqual(new[] { HitResult.Critical, HitResult.Miss }));
    }

    [Test]
    public void TestEndKeyLetGoOnTimeWhileStillHoldingTheStart()
    {
        play(crossing(), press(300, 0), press(800, 2), release(900, 2), release(950, 0));
        AddAssert("head and tail on time", () => results.SequenceEqual(new[] { HitResult.Critical, HitResult.Critical }));
    }

    [Test]
    public void TestLettingGoEarlyBreaks()
    {
        play(crossing(), press(300, 0), release(500, 0));
        AddAssert("a break, then the tail runs out", () =>
            gameplay.BuildResult().HoldBreakCount == 1 && results.SequenceEqual(new[] { HitResult.Critical, HitResult.Miss }));
    }

    [Test]
    public void TestStraightHoldUnchanged()
    {
        play(straight(), press(300, 1), release(900, 1));
        AddAssert("head and tail on time", () => results.SequenceEqual(new[] { HitResult.Critical, HitResult.Critical }));
    }
}
