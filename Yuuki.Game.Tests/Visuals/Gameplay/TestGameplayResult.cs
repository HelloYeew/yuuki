// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Logic coverage for <see cref="GameplayResult"/>'s derived stats: rank thresholds, Full Combo /
/// All Perfect, the early/late split and the timing statistics shown on the score screen.
/// </summary>
public partial class TestGameplayResult : TestScene
{
    private static JudgementResult judgement(HitResult result, double error) =>
        new JudgementResult(new BeatmapNote { Lane = NoteLane.Lane1, Type = NoteType.Tap }, result, error);

    [Test]
    public void TestRankThresholds()
    {
        AddAssert("100% is S", () => ScoreRankExtensions.FromAccuracy(1) == ScoreRank.S);
        AddAssert("95% is S", () => ScoreRankExtensions.FromAccuracy(0.95) == ScoreRank.S);
        AddAssert("94.99% is A", () => ScoreRankExtensions.FromAccuracy(0.9499) == ScoreRank.A);
        AddAssert("80% is B", () => ScoreRankExtensions.FromAccuracy(0.80) == ScoreRank.B);
        AddAssert("70% is C", () => ScoreRankExtensions.FromAccuracy(0.70) == ScoreRank.C);
        AddAssert("69.99% is D", () => ScoreRankExtensions.FromAccuracy(0.6999) == ScoreRank.D);
        AddAssert("0% is D", () => ScoreRankExtensions.FromAccuracy(0) == ScoreRank.D);
    }

    [Test]
    public void TestSNeedsNoMiss()
    {
        AddAssert("98% with a miss is A", () => ScoreRankExtensions.ForRun(0.98, 1) == ScoreRank.A);
        AddAssert("98% clean is S", () => ScoreRankExtensions.ForRun(0.98, 0) == ScoreRank.S);
        AddAssert("a miss doesn't lower A", () => ScoreRankExtensions.ForRun(0.92, 3) == ScoreRank.A);
        AddAssert("a miss doesn't lower B", () => ScoreRankExtensions.ForRun(0.85, 3) == ScoreRank.B);
    }

    [Test]
    public void TestLongSongWithOneMissIsA()
    {
        // 99.9% accurate: S by accuracy alone, A once the miss counts.
        var result = new GameplayResult { CriticalCount = 999, MissCount = 1 };

        AddAssert("accuracy reaches S", () => result.AccuracyRank == ScoreRank.S);
        AddAssert("is A", () => result.Rank == ScoreRank.A);
        AddAssert("lost its rank to the miss", () => result.RankLostToMiss);
    }

    [Test]
    public void TestHoldBreakKeepsS()
    {
        var result = new GameplayResult { CriticalCount = 100, HoldBreakCount = 1 };

        AddAssert("is S", () => result.Rank == ScoreRank.S);
        AddAssert("lost nothing", () => !result.RankLostToMiss);
    }

    [Test]
    public void TestAllPerfect()
    {
        var result = new GameplayResult { CriticalCount = 10 };

        AddAssert("is all perfect", () => result.IsAllPerfect);
        AddAssert("is full combo", () => result.IsFullCombo);
        AddAssert("is S", () => result.Rank == ScoreRank.S);
    }

    [Test]
    public void TestFullComboWithoutAllPerfect()
    {
        var result = new GameplayResult { CriticalCount = 9, BreakCount = 1 };

        AddAssert("is full combo", () => result.IsFullCombo);
        AddAssert("is not all perfect", () => !result.IsAllPerfect);
    }

    [Test]
    public void TestMissBreaksFullCombo()
    {
        var result = new GameplayResult { CriticalCount = 99, MissCount = 1 };

        AddAssert("is not full combo", () => !result.IsFullCombo);
        AddAssert("is not all perfect", () => !result.IsAllPerfect);
    }

    [Test]
    public void TestHoldBreakBreaksFullCombo()
    {
        var result = new GameplayResult { CriticalCount = 10, HoldBreakCount = 1 };

        AddAssert("is not full combo", () => !result.IsFullCombo);
        AddAssert("is not all perfect", () => !result.IsAllPerfect);
    }

    [Test]
    public void TestHoldReleasesExcludedFromTimingStatistics()
    {
        var hold = new BeatmapNote { Lane = NoteLane.Lane1, Type = NoteType.Hold };
        var result = new GameplayResult
        {
            CriticalCount = 2,
            Judgements = new[]
            {
                new JudgementResult(hold, HitResult.Critical, 10),
                // Releases lean early by habit; they mustn't pull the offset suggestion with them.
                new JudgementResult(hold, HitResult.Critical, -60, isHoldTail: true)
            }
        };

        AddAssert("mean is the press only", () => Math.Abs(result.MeanError!.Value - 10) < 1e-9);
        AddAssert("release still counts as early", () => result.EarlyCount(HitResult.Critical) == 1);
    }

    [Test]
    public void TestSuggestedOffsetCorrectsMeanError()
    {
        // 30 presses, all 44 ms late, played at offset 0 -> offset should drop by 44.
        var late = new GameplayResult { CriticalCount = 30, Judgements = repeat(judgement(HitResult.Critical, 44), 30) };
        AddAssert("suggests -44", () => late.SuggestedOffset == -44);

        // Already at −10 and still 5 ms early -> raise to −5.
        var early = new GameplayResult { CriticalCount = 30, AudioOffset = -10, Judgements = repeat(judgement(HitResult.Critical, -5), 30) };
        AddAssert("suggests -5", () => early.SuggestedOffset == -5);
    }

    [Test]
    public void TestSuggestedOffsetNeedsEnoughHits()
    {
        int tooFew = GameplayResult.MINIMUM_OFFSET_SAMPLES - 1;
        var result = new GameplayResult { CriticalCount = tooFew, Judgements = repeat(judgement(HitResult.Critical, 44), tooFew) };

        AddAssert("no suggestion", () => result.SuggestedOffset == null);
    }

    private static JudgementResult[] repeat(JudgementResult judgement, int count)
    {
        var judgements = new JudgementResult[count];
        Array.Fill(judgements, judgement);
        return judgements;
    }

    [Test]
    public void TestEmptyRunHasNoBadge()
    {
        var result = new GameplayResult();

        AddAssert("not full combo", () => !result.IsFullCombo);
        AddAssert("not all perfect", () => !result.IsAllPerfect);
        AddAssert("no mean error", () => result.MeanError == null);
        AddAssert("no unstable rate", () => result.UnstableRate == null);
    }

    [Test]
    public void TestTimingStatistics()
    {
        var result = new GameplayResult
        {
            CriticalCount = 2,
            BreakCount = 1,
            MissCount = 1,
            Judgements = new[]
            {
                judgement(HitResult.Critical, -10),
                judgement(HitResult.Critical, 20),
                judgement(HitResult.Break, 80),
                // A miss has no press, so it must not drag the mean or count as late.
                judgement(HitResult.Miss, 250)
            }
        };

        AddAssert("critical early 1", () => result.EarlyCount(HitResult.Critical) == 1);
        AddAssert("critical late 1", () => result.LateCount(HitResult.Critical) == 1);
        AddAssert("break late 1", () => result.LateCount(HitResult.Break) == 1);
        AddAssert("mean excludes misses", () => Math.Abs(result.MeanError!.Value - 30) < 1e-9);

        // Errors −10, 20, 80 around mean 30 -> deviations −40, −10, 50 -> variance 1400.
        AddAssert("unstable rate is 10 × stddev", () => Math.Abs(result.UnstableRate!.Value - Math.Sqrt(1400) * 10) < 1e-9);
    }
}
