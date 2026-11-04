// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Path = System.IO.Path;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Platform;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The replay of a run that just ran out of health, opened from its results
/// </summary>
public partial class TestReplayAfterAFail : TestYuukiScreenStack
{
    private string tempDir = null!;
    private readonly BeatmapsCollection collection = new BeatmapsCollection();
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();
    private readonly YuukiAudioManager audio = new YuukiAudioManager
    {
        DisposeOnRemoval = false
    };

    private readonly Beatmap beatmap = new Beatmap
    {
        ID = 71,
        DifficultyName = "Hard",
        LaneCount = 4,
        Notes = new[]
        {
            new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 300, EndTime = 300 },
            new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane2, StartTime = 500, EndTime = 500 }
        }
    };

    private IAudioChannel? song;
    private ScoreScreen scoreScreen = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-replay-after-fail-test", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(tempDir, "songs"));
        TestWav.Write(Path.Combine(tempDir, "songs"), "song.wav", seconds: 5);

        beatmap.BeatmapSet = new BeatmapSet { ID = 71, Title = "Song", TrackStoragePath = "songs/song.wav" };

        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache<Storage>(new NativeStorage(tempDir));
        Cache(collection);
        Cache(workingBeatmap);
        Cache(audio);
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [SetUp]
    public void SetUpAudio()
    {
        AddStep("the audio manager", () =>
        {
            if (audio.Parent == null)
                Add(audio);
        });
        AddUntilStep("loaded", () => audio.IsLoaded);
    }

    [TearDown]
    public void TearDown()
    {
        audio.EndResultsMusic();
        audio.EndGameplay();
    }

    [Test]
    public void TestPlaysAtTheSongsOwnSpeed()
    {
        AddStep("a run that failed, its song wound down", () =>
        {
            workingBeatmap.BeatmapSet = beatmap.BeatmapSet;
            workingBeatmap.Beatmap = beatmap;

            song = audio.BeginGameplay();

            // As GameplayScreen's fail leaves it: slowed through the frequency, so the pitch drops with it, then paused.
            var failingClock = new TrackClock(song!) { PreservePitch = false };
            song!.Play();
            failingClock.Rate = 0.05;
            song.Pause();
        });
        AddAssert("the song is wound down", () => song!.Frequency.Value < 0.1);

        AddStep("its results", () =>
        {
            screenStack.Exit();
            screenStack.Push(scoreScreen = new ScoreScreen(new GameplayResult { Failed = true, FailedAt = 400, ChartLength = 600 }, onRetry: () => { },
                score: score(), replay: replay()));
        });
        AddUntilStep("showing", () => screenStack.CurrentScreen == scoreScreen && scoreScreen.IsLoaded);

        AddStep("click Replay", () => descendants(scoreScreen).OfType<MenuShearButton>().First(b => b.Text == "Replay").TriggerClick());
        AddUntilStep("the replay plays", () => screenStack.CurrentScreen is ReplayScreen { IsPlaying: true });
        AddAssert("at the song's own speed", () => song!.Frequency.Value * song.Tempo.Value == 1);

        AddStep("click Exit", () => descendants(screenStack.CurrentScreen!).OfType<PillButton>().First(b => b.Text.StartsWith("Exit", StringComparison.Ordinal)).TriggerClick());
        AddUntilStep("back on the results", () => screenStack.CurrentScreen == scoreScreen);
    }

    private ScoreInfo score() => new ScoreInfo
    {
        BeatmapId = beatmap.ID,
        BeatmapSetId = 71,
        Score = 400_000,
        Accuracy = 0.5,
        MaxCombo = 1,
        CriticalCount = 1,
        MissCount = 1,
        Rank = ScoreRank.D,
        Failed = true,
        Date = DateTimeOffset.Now,
        ReplayId = "test"
    };

    private ReplayData replay() => new ReplayData
    {
        ChartHash = ReplayData.ChartHashOf(PlayableBeatmap.FromBeatmap(beatmap, laneCount: 4)),
        Inputs = new List<ReplayInput>
        {
            new ReplayInput(300, ReplayInputKind.LanePress, 0),
            new ReplayInput(330, ReplayInputKind.LaneRelease, 0)
        }
    };

    private static IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (Drawable d in descendants(child))
                    yield return d;
            }
        }
    }
}
