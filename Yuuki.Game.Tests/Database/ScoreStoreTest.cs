// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Tests.Database;

/// <summary>
/// The local score history behind play counts, personal bests, and the Ranking tab.
/// </summary>
[TestFixture]
public class ScoreStoreTest
{
    private string tempDir = null!;
    private NativeStorage storage = null!;

    private readonly BeatmapSet set = new BeatmapSet { ID = 7 };
    private Beatmap easy = null!;
    private Beatmap hard = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-scorestore-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);

        easy = new Beatmap { ID = 70, BeatmapSet = set };
        hard = new Beatmap { ID = 71, BeatmapSet = set };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    private static ScoreInfo play(Beatmap beatmap, long score, bool failed = false) => new ScoreInfo
    {
        BeatmapId = beatmap.ID,
        BeatmapSetId = beatmap.BeatmapSet.ID,
        Score = score,
        Failed = failed
    };

    [Test]
    public void PlayCounts_PerDifficultyAndPerSong_IncludeFails()
    {
        var store = new ScoreStore(storage);

        store.Add(play(easy, 500_000));
        store.Add(play(easy, 100_000, failed: true));
        store.Add(play(hard, 900_000));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.GetPlayCount(easy), Is.EqualTo(2));
            Assert.That(store.GetPlayCount(hard), Is.EqualTo(1));
            Assert.That(store.GetPlayCount(set), Is.EqualTo(3));
        }
    }

    [Test]
    public void Ranking_BestFirst_WithoutFails()
    {
        var store = new ScoreStore(storage);

        store.Add(play(easy, 500_000));
        store.Add(play(easy, 990_000, failed: true));
        store.Add(play(easy, 800_000));

        var ranking = store.GetRanking(easy, 5);

        Assert.That(ranking, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ranking[0].Score, Is.EqualTo(800_000));
            Assert.That(store.GetPersonalBest(easy)!.Score, Is.EqualTo(800_000));
        }
    }

    [Test]
    public void History_SurvivesReload()
    {
        new ScoreStore(storage).Add(play(hard, 750_000));

        var reloaded = new ScoreStore(storage);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded.GetPlayCount(hard), Is.EqualTo(1));
            Assert.That(reloaded.GetPersonalBest(hard)!.Score, Is.EqualTo(750_000));
        }
    }

    [Test]
    public void DamagedHistory_StartsEmpty()
    {
        File.WriteAllText(Path.Combine(tempDir, "scores.json"), "{ not json");

        Assert.That(new ScoreStore(storage).Scores, Is.Empty);
    }

    private static ReplayData replay() => new ReplayData
    {
        ChartHash = "ABC123",
        AudioOffset = -12,
        ChartLength = 90_000,
        Inputs = new List<ReplayInput>
        {
            new ReplayInput(1000.25, ReplayInputKind.LanePress, 2),
            new ReplayInput(1080.5, ReplayInputKind.LaneRelease, 2),
            new ReplayInput(1500, ReplayInputKind.SwipeLeft),
            new ReplayInput(2000, ReplayInputKind.Pause),
            new ReplayInput(2000, ReplayInputKind.Resume),
            // By touch, with the finger: the first one, 0, is kept as well.
            new ReplayInput(2100, ReplayInputKind.TouchDown, 1, 0),
            new ReplayInput(2200, ReplayInputKind.TouchSlide, 2, 0),
            new ReplayInput(2300, ReplayInputKind.TouchUp, 2, 0)
        },
        Judgements = new List<RecordedJudgement>
        {
            new RecordedJudgement(NoteType.Hold, 1000, 1400, 2, HitResult.Critical, 0.25, false),
            new RecordedJudgement(NoteType.Swipe, 1500, 1500, 0, HitResult.Break, -50, false)
        }
    };

    [Test]
    public void Replay_SavedWithScore_SurvivesReload()
    {
        var score = play(hard, 750_000);
        new ScoreStore(storage).Add(score, replay());

        var reloaded = new ScoreStore(storage);
        ScoreInfo saved = reloaded.Scores.Single();
        ReplayData? loaded = reloaded.GetReplay(saved);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.HasReplay, Is.True);
            Assert.That(loaded, Is.Not.Null);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded!.ChartHash, Is.EqualTo("ABC123"));
            Assert.That(loaded.AudioOffset, Is.EqualTo(-12));
            Assert.That(loaded.Inputs, Is.EqualTo(replay().Inputs));
            Assert.That(loaded.Judgements, Is.EqualTo(replay().Judgements));
        }
    }

    [Test]
    public void ScoreWithoutReplay_HasNone()
    {
        var store = new ScoreStore(storage);
        var score = play(easy, 1);
        store.Add(score);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(score.HasReplay, Is.False);
            Assert.That(store.GetReplay(score), Is.Null);
        }
    }

    [Test]
    public void WithoutStorage_KeepsReplaysInMemory()
    {
        var store = new ScoreStore(null);
        var score = play(easy, 1);
        store.Add(score, replay());

        Assert.That(store.GetReplay(score)!.Inputs, Has.Count.EqualTo(replay().Inputs.Count));
    }

    [Test]
    public void WithoutStorage_WorksInMemory()
    {
        var store = new ScoreStore(null);
        store.Add(play(easy, 1));

        Assert.That(store.GetPlayCount(easy), Is.EqualTo(1));
    }
}
