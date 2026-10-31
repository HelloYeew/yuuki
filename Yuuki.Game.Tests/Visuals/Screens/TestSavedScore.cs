// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestSavedScore : TestYuukiScreenStack
{
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    // Built up front: test methods lay out their steps (and so read this) before the scene loads.
    private readonly Beatmap beatmap = new Beatmap
    {
        ID = 70,
        BeatmapSet = new BeatmapTestUtility().ThemeSet,
        DifficultyName = "Hard",
        LaneCount = 4,
        Notes = new[]
        {
            new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 300, EndTime = 300 },
            new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane2, StartTime = 500, EndTime = 500 }
        }
    };

    private ScoreScreen scoreScreen = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());

        workingBeatmap.BeatmapSet = beatmap.BeatmapSet;
        workingBeatmap.Beatmap = beatmap;
        Cache(workingBeatmap);
    }

    private ScoreInfo score() => new ScoreInfo
    {
        BeatmapId = beatmap.ID,
        BeatmapSetId = beatmap.BeatmapSet.ID,
        Score = 1_000_000,
        Accuracy = 1,
        MaxCombo = 2,
        CriticalCount = 2,
        Rank = ScoreRank.S,
        IsFullCombo = true,
        IsAllPerfect = true,
        ReplayId = "test"
    };

    private ReplayData replay(string? chartHash = null) => new ReplayData
    {
        ChartHash = chartHash ?? ReplayData.ChartHashOf(PlayableBeatmap.FromBeatmap(beatmap, laneCount: 4)),
        Inputs = new List<ReplayInput>
        {
            new ReplayInput(300, ReplayInputKind.LanePress, 0),
            new ReplayInput(330, ReplayInputKind.LaneRelease, 0),
            new ReplayInput(500, ReplayInputKind.LanePress, 1),
            new ReplayInput(530, ReplayInputKind.LaneRelease, 1)
        }
    };

    private void open(ScoreInfo saved, ReplayData? savedReplay) => AddStep("open the saved score", () =>
    {
        screenStack.Exit();
        screenStack.Push(scoreScreen = new ScoreScreen(saved, savedReplay));
    });

    [Test]
    public void TestWithoutReplayIsSummaryOnly()
    {
        open(score(), null);
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddAssert("no breakdown, replay locked", () => scoreScreen.Result.Judgements.Count == 0 && !scoreScreen.CanWatchReplay);
        AddAssert("totals kept", () => scoreScreen.Result.Score == 1_000_000 && scoreScreen.Result.IsAllPerfect);
        AddAssert("more info says why there's no more", () => !scoreScreen.Judging && scoreScreen.MoreInfo.Analysis == null);
    }

    /// <summary>
    /// Its judgements aren't saved: its replay is judged again for them, and its totals are kept as they were played. It
    /// was saved with no Blooms (before there were any), so it's judged without them: its two presses on time are the
    /// Criticals it counted, not the Blooms today's windows would make them.
    /// </summary>
    [Test]
    public void TestJudgedAgainFromItsReplay()
    {
        open(score(), replay());
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddUntilStep("judged again", () => !scoreScreen.Judging && scoreScreen.Result.Judgements.Count == 2);
        AddAssert("as it was judged then", () => scoreScreen.Result.Judgements.All(j => j.Result == HitResult.Critical));
        AddAssert("the totals as played", () => scoreScreen.Result.CriticalCount == 2 && scoreScreen.Result.BloomCount == 0 && scoreScreen.Result.Score == 1_000_000);
        AddUntilStep("more info has it", () => scoreScreen.MoreInfo.Analysis is { Points.Count: 2 });
    }

    /// <summary>
    /// A play saved with Blooms keeps them when its replay is judged again.
    /// </summary>
    [Test]
    public void TestAPlayWithBloomsKeepsThem()
    {
        ScoreInfo bloomed = score();
        bloomed.CriticalCount = 0;
        bloomed.BloomCount = 2;
        bloomed.Rank = ScoreRank.SS;
        bloomed.IsFullBloom = true;

        open(bloomed, replay());
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddUntilStep("judged again", () => !scoreScreen.Judging && scoreScreen.Result.Judgements.Count == 2);
        AddAssert("its Blooms", () => scoreScreen.Result.Judgements.All(j => j.Result == HitResult.Bloom));
    }

    /// <summary>
    /// More info's "watch from" opens the replay there, not at its start.
    /// </summary>
    [Test]
    public void TestWatchingFromAMoment()
    {
        open(score(), replay());
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddStep("watch from 0.4 s", () => scoreScreen.WatchReplay(400));
        AddUntilStep("the replay, from there", () => screenStack.CurrentScreen is ReplayScreen { Gameplay: { } gameplay } && gameplay.GameplayClock.CurrentTime >= 390);
        AddStep("back", () => screenStack.CurrentScreen!.Exit());
        AddUntilStep("back on the score", () => screenStack.CurrentScreen == scoreScreen);
    }

    [Test]
    public void TestReplayPlaysThroughAndComesBack()
    {
        open(score(), replay());
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddAssert("replay open", () => scoreScreen.CanWatchReplay);

        AddStep("watch", () => scoreScreen.WatchReplay());
        AddUntilStep("replay playing", () => screenStack.CurrentScreen is ReplayScreen { IsPlaying: true });
        AddStep("at double speed", () => ((ReplayScreen)screenStack.CurrentScreen!).Gameplay!.Rate = 2);

        // Real-time playback outruns a headless step timeout; allow for it.
        // Its inputs judged again as they were then: saved with no Blooms (before there were any), its two presses on
        // time are the Criticals it counted.
        AddUntilStep("judged again from its inputs", () => screenStack.CurrentScreen is ReplayScreen { Gameplay: { AllJudged: true } } replayScreen
                                                           && replayScreen.Gameplay.BuildResult() is { CriticalCount: 2, BloomCount: 0 }, 60_000);
        AddUntilStep("back on the score", () => screenStack.CurrentScreen == scoreScreen, 60_000);
    }

    [Test]
    public void TestReplayRightAfterAPlay()
    {
        AddStep("show a play's results", () =>
        {
            screenStack.Exit();
            screenStack.Push(scoreScreen = new ScoreScreen(GameplayResult.FromScore(score(), replay()), onRetry: () => { }, score: score(), replay: replay()));
        });
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddAssert("replay offered beside retry", () => scoreScreen.CanWatchReplay);

        AddStep("watch", () => scoreScreen.WatchReplay());
        AddUntilStep("replay playing", () => screenStack.CurrentScreen is ReplayScreen { IsPlaying: true });
        AddStep("at double speed", () => ((ReplayScreen)screenStack.CurrentScreen!).Gameplay!.Rate = 2);
        AddUntilStep("back on the results", () => screenStack.CurrentScreen == scoreScreen, 60_000);
    }

    [Test]
    public void TestUnsavedPlayHasNoReplay()
    {
        AddStep("show an unsaved play's results", () =>
        {
            screenStack.Exit();
            screenStack.Push(scoreScreen = new ScoreScreen(GameplayResult.FromScore(score(), null), onRetry: () => { }));
        });
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddAssert("no replay", () => !scoreScreen.CanWatchReplay);
    }

    [Test]
    public void TestReplayOfDifferentNotesWontPlay()
    {
        open(score(), replay(chartHash: "not this chart"));
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddAssert("not judged again either", () => !scoreScreen.Judging && scoreScreen.Result.Judgements.Count == 0);
        AddStep("watch", () => scoreScreen.WatchReplay());
        AddUntilStep("on the replay screen", () => screenStack.CurrentScreen is ReplayScreen);
        AddAssert("nothing plays", () => screenStack.CurrentScreen is ReplayScreen { Gameplay: null, IsPlaying: false });
    }
}
