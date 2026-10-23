// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Replays: a live run records its inputs; a recorded run, played back, is judged the same way; and
/// seeking a replay (forward, back, to the end) rebuilds exactly the state straight playback reaches.
/// </summary>
public partial class TestReplay : YuukiManualInputManagerTestScene
{
    private GameplayContainer gameplay = null!;

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

    /// <summary>On time, 20 ms late, a hold held through to its end, then 60 ms late.</summary>
    private static ReplayData replay() => new ReplayData
    {
        ChartHash = ReplayData.ChartHashOf(chart()),
        Inputs = new List<ReplayInput>
        {
            new ReplayInput(300, ReplayInputKind.LanePress, 0),
            new ReplayInput(340, ReplayInputKind.LaneRelease, 0),
            new ReplayInput(520, ReplayInputKind.LanePress, 1),
            new ReplayInput(560, ReplayInputKind.LaneRelease, 1),
            new ReplayInput(690, ReplayInputKind.LanePress, 2),
            new ReplayInput(1100, ReplayInputKind.LaneRelease, 2),
            new ReplayInput(1360, ReplayInputKind.LanePress, 3),
            new ReplayInput(1400, ReplayInputKind.LaneRelease, 3)
        }
    };

    private static readonly HitResult[] expected =
    {
        HitResult.Critical, HitResult.Critical, HitResult.Critical, HitResult.Critical, HitResult.Break
    };

    private void addReplay() => AddStep("add replay", () =>
    {
        TestContent.Add(gameplay = new GameplayContainer(chart())
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            LeadInTime = 0,
            Replay = replay()
        });
    });

    private HitResult[] results() => gameplay.BuildResult().Judgements.Select(j => j.Result).ToArray();

    // Playback runs on a real-time stopwatch, which a headless step timeout outpaces; allow for it.
    private const double playback_timeout = 60_000;

    [Test]
    public void TestRecordedInputsAreJudgedAsRecorded()
    {
        addReplay();
        AddStep("play", () => gameplay.Restart());
        AddUntilStep("everything judged", () => gameplay.AllJudged, playback_timeout);
        AddAssert("judged as the inputs say", () => results().SequenceEqual(expected));
    }

    [Test]
    public void TestDoubleSpeedJudgesTheSame()
    {
        addReplay();
        AddStep("play at 2×", () =>
        {
            gameplay.Restart();
            gameplay.Rate = 2;
        });
        AddUntilStep("everything judged", () => gameplay.AllJudged, playback_timeout);
        AddAssert("judged as the inputs say", () => results().SequenceEqual(expected));
    }

    [Test]
    public void TestKeyboardIsIgnoredWhileWatching()
    {
        addReplay();
        AddStep("play", () => gameplay.Restart());
        AddUntilStep("first note judged", () => gameplay.BuildResult().Judgements.Count >= 1);

        // A stray press on the second note's lane must not take it before the replay does.
        AddStep("press F", () => PressKeyOnce(Key.F));
        AddUntilStep("everything judged", () => gameplay.AllJudged, playback_timeout);
        AddAssert("still judged as recorded", () => results().SequenceEqual(expected));
    }

    [Test]
    public void TestSeekingRebuildsTheRun()
    {
        addReplay();
        AddStep("play then stop", () =>
        {
            gameplay.Restart();
            gameplay.Stop();
        });

        // Mid-hold: the two taps and the hold's head are in, the hold is still being held.
        AddStep("seek to 800", () => gameplay.SeekReplay(800));
        AddAssert("three judgements, combo 3", () => gameplay.BuildResult().Judgements.Count == 3 && gameplay.Score.Combo.Value == 3);
        AddAssert("clock at 800", () => System.Math.Abs(gameplay.GameplayClock.CurrentTime - 800) < 1);

        AddStep("seek back to 0", () => gameplay.SeekReplay(0));
        AddAssert("nothing judged, no score", () => gameplay.BuildResult().Judgements.Count == 0 && gameplay.Score.Score.Value == 0);

        AddStep("seek past the end", () => gameplay.SeekReplay(2000));
        AddAssert("everything judged as recorded", () => gameplay.AllJudged && results().SequenceEqual(expected));
        AddAssert("same score as playing it through", () => gameplay.Score.Score.Value == scoreOfStraightPlayback());
    }

    // The score a straight run of the same judgements reaches, from a fresh processor.
    private static long scoreOfStraightPlayback()
    {
        var score = new ScoreProcessor();
        score.Prepare(chart());

        foreach (HitResult result in expected)
            score.Apply(new JudgementResult(new BeatmapNote(), result, 0));

        return score.Score.Value;
    }

    [Test]
    public void TestLiveRunIsRecorded()
    {
        AddStep("add live run", () =>
        {
            TestContent.Add(gameplay = new GameplayContainer(chart())
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                LeadInTime = 0
            });
        });
        AddStep("start", () => gameplay.Restart());
        AddStep("tap D", () => PressKeyOnce(Key.D));
        AddStep("pause and resume", () =>
        {
            gameplay.InputPaused = true;
            gameplay.InputPaused = false;
        });

        AddAssert("press, release, pause and resume recorded", () =>
        {
            var kinds = gameplay.BuildReplay().Inputs.Select(i => i.Kind).ToArray();
            return kinds.SequenceEqual(new[] { ReplayInputKind.LanePress, ReplayInputKind.LaneRelease, ReplayInputKind.Pause, ReplayInputKind.Resume });
        });
        AddAssert("on lane 0", () => gameplay.BuildReplay().Inputs.Take(2).All(i => i.Lane == 0));
        AddAssert("chart fingerprinted", () => gameplay.BuildReplay().ChartHash == ReplayData.ChartHashOf(chart()));
    }

    /// <summary>
    /// A song offset nudged mid-run moves the clock at once, and the replay keeps where: played back, the offsets change
    /// at the same point in the song, judged the same as ever, and seeking back puts them as they started.
    /// </summary>
    [Test]
    public void TestOffsetChangesAreRecordedAndPlayedBack()
    {
        AddStep("add a live run", () => TestContent.Add(gameplay = new GameplayContainer(chart())
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            LeadInTime = 0,
            AudioOffset = 10,
            SongOffset = 5
        }));
        AddUntilStep("loaded", () => gameplay.IsLoaded);
        AddAssert("both on the clock", () => gameplay.GameplayClock.Offset == 15);

        AddStep("nudge the song", () => gameplay.SongOffset = 20);
        AddAssert("the clock moved", () => gameplay.GameplayClock.Offset == 30);
        AddAssert("recorded", () =>
        {
            ReplayData recorded = gameplay.BuildReplay();
            return recorded.AudioOffset == 10 && recorded.SongOffset == 5
                   && recorded.OffsetChanges.Count == 1 && recorded.OffsetChanges[0] is { AudioOffset: 10, SongOffset: 20 };
        });

        AddStep("watch a run nudged at 200", () =>
        {
            ReplayData nudged = replay();
            nudged.AudioOffset = 10;
            nudged.SongOffset = 5;
            nudged.OffsetChanges.Add(new ReplayOffsetChange(200, 10, 20));

            TestContent.Clear();
            TestContent.Add(gameplay = new GameplayContainer(chart())
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                LeadInTime = 0,
                Replay = nudged,
                AudioOffset = nudged.AudioOffset,
                SongOffset = nudged.SongOffset
            });
        });
        AddUntilStep("loaded", () => gameplay.IsLoaded);
        AddAssert("starts as the run did", () => gameplay.GameplayClock.Offset == 15);

        AddStep("play", () => gameplay.Restart());
        AddUntilStep("nudged where the run was", () => gameplay.GameplayClock.Offset == 30, playback_timeout);
        AddUntilStep("everything judged", () => gameplay.AllJudged, playback_timeout);
        AddAssert("judged as the inputs say", () => results().SequenceEqual(expected));

        AddStep("seek back to 0", () => gameplay.SeekReplay(0));
        AddUntilStep("as it started", () => gameplay.GameplayClock.Offset == 15);
    }
}
