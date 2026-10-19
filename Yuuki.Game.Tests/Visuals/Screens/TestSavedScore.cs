// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
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
        },
        Judgements = new List<RecordedJudgement>
        {
            new RecordedJudgement(NoteType.Tap, 300, 300, 0, HitResult.Critical, 0, false),
            new RecordedJudgement(NoteType.Tap, 500, 500, 1, HitResult.Critical, 0, false)
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
    }

    [Test]
    public void TestReplayPlaysThroughAndComesBack()
    {
        open(score(), replay());
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen);
        AddAssert("full breakdown, replay open", () => scoreScreen.Result.Judgements.Count == 2 && scoreScreen.CanWatchReplay);

        AddStep("watch", () => scoreScreen.WatchReplay());
        AddUntilStep("replay playing", () => screenStack.CurrentScreen is ReplayScreen { IsPlaying: true });
        AddStep("at double speed", () => ((ReplayScreen)screenStack.CurrentScreen!).Gameplay!.Rate = 2);

        // Real-time playback outruns a headless step timeout; allow for it.
        AddUntilStep("judged as recorded", () => screenStack.CurrentScreen is ReplayScreen { Gameplay: { AllJudged: true } } replayScreen
                                                 && replayScreen.Gameplay.BuildResult().CriticalCount == 2, 60_000);
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
        AddStep("watch", () => scoreScreen.WatchReplay());
        AddUntilStep("on the replay screen", () => screenStack.CurrentScreen is ReplayScreen);
        AddAssert("nothing plays", () => screenStack.CurrentScreen is ReplayScreen { Gameplay: null, IsPlaying: false });
    }
}
