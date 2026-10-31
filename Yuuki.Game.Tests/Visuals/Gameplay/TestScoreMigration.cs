// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Saved plays brought up to the current judging rules from their replays: which are, which stay as they were played,
/// and that it waits while a song is being played.
/// </summary>
public partial class TestScoreMigration : YuukiTestScene
{
    private const int chart_id = 70;

    private ScoreStore store = null!;
    private ScoreMigration migration = null!;
    private bool holdingOff;
    private bool startHoldingOff;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    private static PlayableBeatmap chart() => new PlayableBeatmap(new[]
    {
        new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 300, EndTime = 300 },
        new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane2, StartTime = 500, EndTime = 500 }
    }, laneCount: 4);

    private static Beatmap beatmapOf(PlayableBeatmap playable) => new Beatmap { ID = chart_id, LaneCount = 4, Notes = playable.Notes };

    // Two presses on time.
    private static ReplayData onTime(string? chartHash = null) => new ReplayData
    {
        ChartHash = chartHash ?? ReplayData.ChartHashOf(chart()),
        Inputs = new List<ReplayInput>
        {
            new ReplayInput(300, ReplayInputKind.LanePress, 0),
            new ReplayInput(330, ReplayInputKind.LaneRelease, 0),
            new ReplayInput(500, ReplayInputKind.LanePress, 1),
            new ReplayInput(530, ReplayInputKind.LaneRelease, 1)
        }
    };

    // As saved before BLOOM: two Criticals, an all perfect.
    private static ScoreInfo oldPlay(int beatmap = chart_id) => new ScoreInfo
    {
        BeatmapId = beatmap,
        Score = 1_000_000,
        Accuracy = 1,
        MaxCombo = 2,
        CriticalCount = 2,
        Rank = ScoreRank.S,
        IsFullCombo = true,
        IsAllPerfect = true
    };

    private void run(params (ScoreInfo Score, ReplayData? Replay)[] plays) => AddStep("migrate", () =>
    {
        Clear();

        holdingOff = startHoldingOff;
        store = new ScoreStore(null);

        foreach ((ScoreInfo score, ReplayData? replay) in plays)
            store.Add(score, replay);

        Add(migration = new ScoreMigration(store, id => id == chart_id ? beatmapOf(chart()) : null, null) { HoldOff = () => holdingOff });
    });

    /// <summary>
    /// An old play's totals are worked out again under today's rules: its presses on time are Blooms now, each a point
    /// past the million, and all of them a Full Bloom.
    /// </summary>
    [Test]
    public void TestAPlayIsBroughtUpToDate()
    {
        ScoreInfo play = oldPlay();

        run((play, onTime()));
        AddUntilStep("done", () => migration.IsDone);
        AddAssert("one updated", () => migration.Updated == 1 && migration.Kept == 0);
        AddAssert("its totals, today's", () => play is { BloomCount: 2, CriticalCount: 0, Score: 1_000_002, MaxCombo: 2, Rank: ScoreRank.SS, IsFullBloom: true });
        AddAssert("under today's rules", () => play.RulesVersion == ScoreInfo.CURRENT_RULES);
        AddAssert("no longer judged without Blooms", () => !BloomlessJudgement.AppliesTo(play));
    }

    /// <summary>
    /// With no replay, its chart gone or changed, a play stays exactly as it was played, under its old rules.
    /// </summary>
    [Test]
    public void TestPlaysThatCantBeStayAsPlayed()
    {
        ScoreInfo noReplay = oldPlay(), chartGone = oldPlay(beatmap: 99), chartChanged = oldPlay();

        run((noReplay, null), (chartGone, onTime()), (chartChanged, onTime(chartHash: "not this chart")));
        AddUntilStep("done", () => migration.IsDone);
        AddAssert("all three kept", () => migration.Updated == 0 && migration.Kept == 3);
        AddAssert("just as they were", () => new[] { noReplay, chartGone, chartChanged }.All(s => s is { CriticalCount: 2, BloomCount: 0, Score: 1_000_000, RulesVersion: 0 }));
    }

    /// <summary>
    /// A play that passed but fails when judged again isn't rewritten into a fail: frame timing isn't worth that.
    /// </summary>
    [Test]
    public void TestARejudgeThatEndsDifferentlyIsKept()
    {
        ScoreInfo passed = oldPlay();
        var notes = Enumerable.Range(0, 40).Select(i => new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 500 + i * 200, EndTime = 500 + i * 200 }).ToArray();
        var hard = new PlayableBeatmap(notes, laneCount: 4);

        AddStep("migrate a pass whose inputs now fail", () =>
        {
            Clear();

            holdingOff = false;
            store = new ScoreStore(null);
            store.Add(passed, new ReplayData
            {
                ChartHash = ReplayData.ChartHashOf(hard),
                Inputs = new List<ReplayInput> { new ReplayInput(0, ReplayInputKind.LanePress, 3), new ReplayInput(10, ReplayInputKind.LaneRelease, 3) }
            });

            Add(migration = new ScoreMigration(store, _ => beatmapOf(hard), null));
        });
        AddUntilStep("done", () => migration.IsDone);
        AddAssert("kept as played", () => migration.Kept == 1 && passed is { CriticalCount: 2, Failed: false, RulesVersion: 0 });

        // Not tried again next launch, which would say so in a notification every time.
        AddStep("next launch", () =>
        {
            Clear();
            Add(migration = new ScoreMigration(store, _ => beatmapOf(hard), null));
        });
        AddUntilStep("done", () => migration.IsDone);
        AddAssert("left alone", () => migration.Kept == 0 && migration.Updated == 0);
    }

    /// <summary>
    /// A play already under today's rules isn't touched, however its totals read.
    /// </summary>
    [Test]
    public void TestAPlayUnderTodaysRulesIsLeftAlone()
    {
        ScoreInfo current = oldPlay();
        current.RulesVersion = ScoreInfo.CURRENT_RULES;

        run((current, onTime()));
        AddUntilStep("done", () => migration.IsDone);
        AddAssert("nothing done", () => migration.Updated == 0 && migration.Kept == 0 && current.CriticalCount == 2);
    }

    /// <summary>
    /// It waits while a song is being played, then carries on.
    /// </summary>
    [Test]
    public void TestWaitsWhileASongIsPlayed()
    {
        ScoreInfo play = oldPlay();

        AddStep("a song is playing", () => startHoldingOff = true);
        run((play, onTime()));
        AddStep("back to the usual", () => startHoldingOff = false);
        AddWaitStep("while it plays", 300);
        AddAssert("nothing done", () => !migration.IsDone && play.RulesVersion == 0);
        AddStep("the song ends", () => holdingOff = false);
        AddUntilStep("carries on", () => migration.IsDone && play.RulesVersion == ScoreInfo.CURRENT_RULES);
    }
}
