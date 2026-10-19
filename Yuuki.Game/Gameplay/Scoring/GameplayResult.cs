// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Yuuki.Game.Gameplay.Replays;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// The final tally of a gameplay run, handed to the score screen.
/// </summary>
public class GameplayResult
{
    public int Score { get; init; }
    public int MaxCombo { get; init; }

    public int CriticalCount { get; init; }
    public int BreakCount { get; init; }
    public int HitCount { get; init; }
    public int MissCount { get; init; }

    /// <summary>
    /// Holds let go of well before their end: each broke the combo without being a judgement, so a
    /// run can have no misses and still not be a Full Combo.
    /// </summary>
    public int HoldBreakCount { get; init; }

    /// <summary>
    /// Every judgement of the run in order, for the detailed breakdown (early/late split, timing
    /// distribution, timeline). May be empty for a result built from counts alone.
    /// </summary>
    public IReadOnlyList<JudgementResult> Judgements { get; init; } = Array.Empty<JudgementResult>();

    /// <summary>The timing windows the run was judged with, so the timing graph can draw its zones.</summary>
    public HitWindows HitWindows { get; init; } = new HitWindows();

    /// <summary>The chart's length in ms (end of its last note), the span of the judgement timeline.</summary>
    public double ChartLength { get; init; }

    /// <summary>The audio offset (ms) the run was played with, the base for <see cref="SuggestedOffset"/>.</summary>
    public double AudioOffset { get; init; }

    /// <summary>Hit presses needed before the mean error is trusted enough to suggest an offset.</summary>
    public const int MINIMUM_OFFSET_SAMPLES = 20;

    /// <summary>
    /// A saved play, for viewing it again: its totals, and — when a replay was saved with it — the
    /// per-note judgements behind the timing breakdown. Without one the breakdown stays empty.
    /// </summary>
    public static GameplayResult FromScore(ScoreInfo score, ReplayData? replay) => new GameplayResult
    {
        Score = (int)score.Score,
        MaxCombo = score.MaxCombo,
        CriticalCount = score.CriticalCount,
        BreakCount = score.BreakCount,
        HitCount = score.HitCount,
        MissCount = score.MissCount,
        HoldBreakCount = score.HoldBreakCount,
        Accuracy = score.Accuracy,
        Failed = score.Failed,
        Judgements = replay?.Judgements.Select(j => j.ToJudgement()).ToArray() ?? Array.Empty<JudgementResult>(),
        HitWindows = replay?.HitWindows ?? new HitWindows(),
        ChartLength = replay?.ChartLength ?? 0,
        AudioOffset = replay?.AudioOffset ?? 0
    };

    /// <summary>The player ran out of health before the end.</summary>
    public bool Failed { get; init; }

    public int TotalNotes => CriticalCount + BreakCount + HitCount + MissCount;

    private readonly double? accuracy;

    /// <summary>
    /// Accuracy in [0, 1] as the run's <see cref="ScoreProcessor"/> measured it (note weights
    /// included). A result built from counts alone falls back to the unweighted mean of
    /// <see cref="HitResultExtensions.Value"/> over the counts.
    /// </summary>
    public double Accuracy
    {
        get
        {
            if (accuracy is double recorded)
                return recorded;

            if (TotalNotes == 0)
                return 1;

            return (CriticalCount * HitResult.Critical.Value() + BreakCount * HitResult.Break.Value() + HitCount * HitResult.Hit.Value()) / TotalNotes;
        }
        init => accuracy = value;
    }

    /// <summary>
    /// The rank the accuracy alone reaches, before the no-miss rule for S.
    /// </summary>
    public ScoreRank AccuracyRank => ScoreRankExtensions.FromAccuracy(Accuracy);

    /// <summary>
    /// The rank earned: <see cref="AccuracyRank"/>, but S needs no misses (<see cref="ScoreRankExtensions.ForRun"/>).
    /// </summary>
    public ScoreRank Rank => ScoreRankExtensions.ForRun(Accuracy, MissCount);

    /// <summary>
    /// Accurate enough for a higher rank than it got, and held back by a miss (S -> A).
    /// </summary>
    public bool RankLostToMiss => Rank < AccuracyRank;

    /// <summary>
    /// The combo never broke — no misses and no hold breaks (FC).
    /// </summary>
    public bool IsFullCombo => TotalNotes > 0 && MissCount == 0 && HoldBreakCount == 0;

    /// <summary>
    /// Every judgement was a Critical (AP). Implies <see cref="IsFullCombo"/>.
    /// </summary>
    public bool IsAllPerfect => IsFullCombo && CriticalCount == TotalNotes;

    /// <summary>
    /// How many judgements of <paramref name="result"/> were pressed early (negative error).
    /// </summary>
    public int EarlyCount(HitResult result) => countWhere(result, early: true);

    /// <summary>
    /// How many judgements of <paramref name="result"/> were pressed late (positive error).
    /// </summary>
    public int LateCount(HitResult result) => countWhere(result, early: false);

    private int countWhere(HitResult result, bool early)
    {
        int count = 0;

        foreach (JudgementResult judgement in Judgements)
        {
            if (judgement.Result != result)
                continue;

            if (early ? judgement.TimingError < 0 : judgement.TimingError > 0)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Whether a judgement's timing feeds the timing statistics: hit presses only. Misses have no
    /// press; hold releases are judged against wider windows and lean early by habit; Pass and Touch
    /// notes aren't pressed at all. Any of those would drag the mean away from what an offset
    /// adjustment should correct.
    /// </summary>
    public static bool AffectsTimingStatistics(JudgementResult judgement) =>
        judgement.Result.IsHit()
        && !judgement.IsHoldTail
        && judgement.Note.Type is not (Beatmaps.NoteType.Pass or Beatmaps.NoteType.Touch);

    /// <summary>
    /// The mean signed timing error of every hit press (see <see cref="AffectsTimingStatistics"/>), in
    /// ms; negative is early. Null when nothing was hit. A consistent non-zero mean is what an offset
    /// adjustment would correct.
    /// </summary>
    public double? MeanError
    {
        get
        {
            double sum = 0;
            int count = 0;

            foreach (JudgementResult judgement in Judgements)
            {
                if (!AffectsTimingStatistics(judgement))
                    continue;

                sum += judgement.TimingError;
                count++;
            }

            return count == 0 ? null : sum / count;
        }
    }

    /// <summary>
    /// The audio offset that would have centred this run's presses on the notes: the offset played
    /// with, minus the mean error (hitting 44 ms late suggests 44 ms lower). Rounded to whole ms. Null
    /// with fewer than <see cref="MINIMUM_OFFSET_SAMPLES"/> hit presses, where the mean is mostly noise.
    /// </summary>
    public double? SuggestedOffset
    {
        get
        {
            if (MeanError is not double mean)
                return null;

            int samples = 0;
            foreach (JudgementResult judgement in Judgements)
            {
                if (AffectsTimingStatistics(judgement))
                    samples++;
            }

            return samples < MINIMUM_OFFSET_SAMPLES ? null : Math.Round(AudioOffset - mean);
        }
    }

    /// <summary>
    /// The unstable rate: ten times the standard deviation of hit press timing errors.
    /// Lower is more consistent. Null when nothing was hit.
    /// </summary>
    public double? UnstableRate
    {
        get
        {
            if (MeanError is not double mean)
                return null;

            double sumSquares = 0;
            int count = 0;

            foreach (JudgementResult judgement in Judgements)
            {
                if (!AffectsTimingStatistics(judgement))
                    continue;

                double deviation = judgement.TimingError - mean;
                sumSquares += deviation * deviation;
                count++;
            }

            return Math.Sqrt(sumSquares / count) * 10;
        }
    }
}
