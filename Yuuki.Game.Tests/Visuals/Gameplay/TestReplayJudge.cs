// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// A saved run judged again from its replay, as the results screen does: the same judgements watching it gives, holds
/// broken with their times, stopping where health ran out, and spread over frames by its budget.
/// </summary>
public partial class TestReplayJudge : YuukiTestScene
{
    private ReplayJudge judge = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    private static PlayableBeatmap chart() => new PlayableBeatmap(new[]
    {
        new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 300, EndTime = 300 },
        new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane2, StartTime = 500, EndTime = 500 },
        new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane3, StartTime = 700, EndTime = 1100 },
        new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane4, StartTime = 1300, EndTime = 1300 }
    }, laneCount: 4);

    /// <summary>On time, 30 ms late, a hold held through to its end, then 60 ms late (as <see cref="TestReplay"/>'s).</summary>
    private static ReplayData replay() => new ReplayData
    {
        ChartHash = ReplayData.ChartHashOf(chart()),
        Inputs = new List<ReplayInput>
        {
            new ReplayInput(300, ReplayInputKind.LanePress, 0),
            new ReplayInput(340, ReplayInputKind.LaneRelease, 0),
            new ReplayInput(530, ReplayInputKind.LanePress, 1),
            new ReplayInput(570, ReplayInputKind.LaneRelease, 1),
            new ReplayInput(690, ReplayInputKind.LanePress, 2),
            new ReplayInput(1100, ReplayInputKind.LaneRelease, 2),
            new ReplayInput(1360, ReplayInputKind.LanePress, 3),
            new ReplayInput(1400, ReplayInputKind.LaneRelease, 3)
        }
    };

    private void add(PlayableBeatmap playing, ReplayData played, double songSpeed = double.PositiveInfinity) => AddStep("judge it again", () =>
    {
        Clear();
        Add(judge = new ReplayJudge(playing, played) { StartTime = 0, SongSpeed = songSpeed });
    });

    [Test]
    public void TestJudgedAsWatchingItWould()
    {
        add(chart(), replay());
        AddUntilStep("done", () => judge.IsDone);
        AddAssert("judged as the inputs say", () => judge.Judgements.Select(j => j.Result).SequenceEqual(new[]
        {
            HitResult.Bloom, HitResult.Critical, HitResult.Bloom, HitResult.Bloom, HitResult.Break
        }));
        AddAssert("every note", () => judge.JudgedNotes == 4 && judge.TotalNotes == 4 && judge.Progress == 1);
        AddAssert("no hold broken, still alive", () => judge.HoldBreakTimes.Count == 0 && judge.FailedAt == null);
    }

    /// <summary>
    /// A play saved before BLOOM is judged as it was then: its on-time presses are Criticals.
    /// </summary>
    [Test]
    public void TestWithoutBlooms()
    {
        AddStep("judge it again, Blooms as Criticals", () =>
        {
            Clear();
            Add(judge = new ReplayJudge(chart(), replay()) { StartTime = 0, CountsBloomAsCritical = true });
        });
        AddUntilStep("done", () => judge.IsDone);
        AddAssert("no Blooms, the rest the same", () => judge.Judgements.Select(j => j.Result).SequenceEqual(new[]
        {
            HitResult.Critical, HitResult.Critical, HitResult.Critical, HitResult.Critical, HitResult.Break
        }));
    }

    /// <summary>
    /// A hold let go of too early for its release to count breaks the combo with no judgement: it's timed, where it was
    /// let go of.
    /// </summary>
    [Test]
    public void TestAHoldLetGoOfIsTimed()
    {
        ReplayData letGo = replay();
        letGo.Inputs[5] = new ReplayInput(780, ReplayInputKind.LaneRelease, 2);

        add(chart(), letGo);
        AddUntilStep("done", () => judge.IsDone);
        AddAssert("one hold broken, where it was let go of", () => judge.HoldBreakTimes.Count == 1 && judge.HoldBreakTimes[0] >= 780 && judge.HoldBreakTimes[0] < 800);
    }

    /// <summary>
    /// Nothing pressed, health runs out with the fifteenth miss, and the run stops there, as it did when it was played.
    /// </summary>
    [Test]
    public void TestStopsWhereHealthRanOut()
    {
        var notes = Enumerable.Range(0, 40).Select(i => new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 500 + i * 200, EndTime = 500 + i * 200 }).ToArray();
        var playing = new PlayableBeatmap(notes, laneCount: 4);

        add(playing, new ReplayData { ChartHash = ReplayData.ChartHashOf(playing) });
        AddUntilStep("done", () => judge.IsDone);
        AddAssert("failed at the fifteenth", () => judge.Judgements.Count == 15 && judge.FailedAt is > 3300 and < 3700);
    }

    /// <summary>
    /// How far it's got can be read as it goes, from the update thread, and only ever goes forward.
    /// </summary>
    [Test]
    public void TestProgressGoesForward()
    {
        var progress = new List<float>();

        add(ChartGenerator.Generate(150, 60_000, seed: 3), new ReplayData(), songSpeed: 20);
        AddUntilStep("done", () =>
        {
            progress.Add(judge.Progress);
            judge.Snapshot();
            return judge.IsDone;
        });
        AddAssert("over many frames, never going back", () => progress.Count > 10 && progress.Zip(progress.Skip(1)).All(p => p.Second >= p.First));
    }

    /// <summary>
    /// It works on a thread of its own from the pool, so the update thread's frames aren't what pays for it; what it says
    /// is done it says on the update thread.
    /// </summary>
    [Test]
    public void TestJudgesOffTheUpdateThread()
    {
        int updateThread = 0;
        int completedOn = 0;

        AddStep("judge it again", () =>
        {
            Clear();

            updateThread = Environment.CurrentManagedThreadId;
            Add(judge = new ReplayJudge(chart(), replay()) { StartTime = 0 });
            judge.Completed += () => completedOn = Environment.CurrentManagedThreadId;
        });
        AddUntilStep("done", () => judge.IsDone);
        AddAssert("judged on another thread", () => judge.JudgingThreadId != 0 && judge.JudgingThreadId != updateThread);
        AddAssert("said so on the update thread", () => completedOn == updateThread);
    }

    /// <summary>
    /// Taken away part way (the results screen left), it stops rather than judging on with nobody to tell.
    /// </summary>
    [Test]
    public void TestTakenAwayPartWayItStops()
    {
        double stoppedAt = 0;

        add(ChartGenerator.Generate(150, 60_000, seed: 3), new ReplayData(), songSpeed: 5);
        AddUntilStep("under way", () => judge.JudgedTime > 100);
        AddStep("take it away", () =>
        {
            Remove(judge);
            judge.Dispose();
            stoppedAt = judge.JudgedTime;
        });
        AddWaitStep("a moment", 200);
        AddAssert("it stopped there", () => judge.JudgedTime == stoppedAt && !judge.IsDone);
    }
}
