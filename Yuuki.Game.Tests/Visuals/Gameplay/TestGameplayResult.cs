// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
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

    /// <summary>
    /// The judging moves to meet the taps, and nothing else does: the audio offset by the mean error one way, the visual
    /// offset by it the other, so their sum (where the notes are seen against the music) stays.
    /// </summary>
    [Test]
    public void TestSuggestedOffsetsMoveTheJudgingOnly()
    {
        // 30 presses, all 44 ms late, played at 0 and 0.
        var late = new GameplayResult { CriticalCount = 30, Judgements = repeat(judgement(HitResult.Critical, 44), 30) };
        AddAssert("audio -44, visual +44", () => late.SuggestedOffsets == (-44, 44));

        // At -10 and +20, and still 5 ms early.
        var early = new GameplayResult { CriticalCount = 30, AudioOffset = -10, VisualOffset = 20, Judgements = repeat(judgement(HitResult.Critical, -5), 30) };
        AddAssert("audio -5, visual +15", () => early.SuggestedOffsets == (-5, 15));
        AddAssert("the sum kept", () => early.SuggestedOffsets is var (audio, visual) && audio + visual == -10 + 20);
    }

    /// <summary>
    /// Where either offset would pass its limit, both move as far as it can go, so the sum still stays.
    /// </summary>
    [Test]
    public void TestSuggestedOffsetsStopAtTheLimits()
    {
        var result = new GameplayResult
        {
            CriticalCount = 30,
            AudioOffset = YuukiConfigManager.MIN_AUDIO_OFFSET + 10,
            VisualOffset = 0,
            Judgements = repeat(judgement(HitResult.Critical, 44), 30)
        };

        AddAssert("moved 10, both", () => result.SuggestedOffsets == (YuukiConfigManager.MIN_AUDIO_OFFSET, 10));
    }

    /// <summary>
    /// What a run's lean cost: presses that would have been Critical with it taken off.
    /// </summary>
    [Test]
    public void TestCriticalsLostToLean()
    {
        // 30 presses 60 ms late, every one a Break: centred, every one Critical.
        var late = new GameplayResult { CriticalCount = 0, BreakCount = 30, Judgements = repeat(judgement(HitResult.Break, 60), 30) };
        AddAssert("all 30", () => late.CriticalsLostToLean == 30);

        // Already Critical, if a little late: nothing to win.
        var close = new GameplayResult { CriticalCount = 30, Judgements = repeat(judgement(HitResult.Critical, 10), 30) };
        AddAssert("none", () => close.CriticalsLostToLean == 0);
    }

    [Test]
    public void TestSuggestedOffsetsNeedEnoughHits()
    {
        int tooFew = GameplayResult.MINIMUM_OFFSET_SAMPLES - 1;
        var result = new GameplayResult { CriticalCount = tooFew, Judgements = repeat(judgement(HitResult.Critical, 44), tooFew) };

        AddAssert("no suggestion", () => result.SuggestedOffsets == null);
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
