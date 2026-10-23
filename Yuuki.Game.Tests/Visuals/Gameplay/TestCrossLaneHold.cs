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
/// On a keyboard, a hold that changes lanes is played as a normal hold: keys can't follow it across, so the
/// key that takes it holds it to the end, wherever it drifts, and letting go of that key judges the tail.
/// Another key doesn't carry it. (By touch a finger follows it: see <see cref="TestTouchPlay"/>.) Inputs are
/// fed at exact times through the replay path, which judges them as live presses.
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
    public void TestHeldByItsKeyToTheEnd()
    {
        play(crossing(), press(300, 0), release(900, 0));

        AddAssert("head and tail on time", () => results.SequenceEqual(new[] { HitResult.Critical, HitResult.Critical }));
        AddAssert("never broken", () => gameplay.BuildResult().HoldBreakCount == 0);
    }

    /// <summary>
    /// The keys of the lanes it drifts into don't take it over: letting go of its own key early breaks it.
    /// </summary>
    [Test]
    public void TestAnotherKeyDoesntCarryIt()
    {
        play(crossing(), press(300, 0), press(500, 1), release(520, 0), release(900, 1));
        AddAssert("broken", () => gameplay.BuildResult().HoldBreakCount == 1);
    }

    [Test]
    public void TestLettingGoEarlyBreaks()
    {
        play(crossing(), press(300, 0), release(500, 0));
        AddAssert("a break, then the tail runs out", () =>
            gameplay.BuildResult().HoldBreakCount == 1 && results.SequenceEqual(new[] { HitResult.Critical, HitResult.Miss }));
    }

    /// <summary>
    /// Dropped, it's taken up again by the key of any lane it crosses, which then holds it to the end; the
    /// tail is capped at Hit for the drop.
    /// </summary>
    [Test]
    public void TestTakenUpAgainFromAnyLaneItCrosses()
    {
        play(crossing(), press(300, 0), release(450, 0), press(600, 2), release(900, 2));
        AddAssert("a break, then the tail", () =>
            gameplay.BuildResult().HoldBreakCount == 1 && results.SequenceEqual(new[] { HitResult.Critical, HitResult.Hit }));
    }

    [Test]
    public void TestStraightHoldUnchanged()
    {
        play(straight(), press(300, 1), release(900, 1));
        AddAssert("head and tail on time", () => results.SequenceEqual(new[] { HitResult.Critical, HitResult.Critical }));
    }
}
