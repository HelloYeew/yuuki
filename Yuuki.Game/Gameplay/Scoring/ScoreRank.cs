// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// The letter grade of a finished run, worst to best. Ranked from accuracy rather than score so
/// beatmap bonuses (which can push score past 1,000,000) never change the grade, with one extra
/// rule: S also needs a run without a miss (see <see cref="ScoreRankExtensions.ForRun"/>).
/// </summary>
public enum ScoreRank
{
    D,
    C,
    B,
    A,
    S
}

public static class ScoreRankExtensions
{
    /// <summary>
    /// The ranks that have a threshold to cross, in ascending order (D is the floor).
    /// </summary>
    public static readonly ScoreRank[] THRESHOLD_RANKS = { ScoreRank.C, ScoreRank.B, ScoreRank.A, ScoreRank.S };

    /// <summary>
    /// The minimum accuracy (0–1) needed for <paramref name="rank"/>.
    /// </summary>
    public static double MinimumAccuracy(this ScoreRank rank) => rank switch
    {
        ScoreRank.S => 0.95,
        ScoreRank.A => 0.90,
        ScoreRank.B => 0.80,
        ScoreRank.C => 0.70,
        _ => 0
    };

    /// <summary>
    /// The best rank a run with a miss can get. Accuracy alone let a long song with one miss still
    /// reach S, so S is kept for runs without one.
    /// </summary>
    public const ScoreRank MAX_RANK_WITH_MISS = ScoreRank.A;

    /// <summary>
    /// The rank a run earns: <see cref="FromAccuracy"/>, held to <see cref="MAX_RANK_WITH_MISS"/> when
    /// it has a miss. Hold breaks aren't misses, so they don't count against it.
    /// </summary>
    public static ScoreRank ForRun(double accuracy, int missCount)
    {
        ScoreRank rank = FromAccuracy(accuracy);
        return missCount > 0 && rank > MAX_RANK_WITH_MISS ? MAX_RANK_WITH_MISS : rank;
    }

    /// <summary>
    /// The rank earned by an accuracy in [0, 1], before the no-miss rule for S.
    /// </summary>
    public static ScoreRank FromAccuracy(double accuracy)
    {
        for (int i = THRESHOLD_RANKS.Length - 1; i >= 0; i--)
        {
            if (accuracy >= THRESHOLD_RANKS[i].MinimumAccuracy())
                return THRESHOLD_RANKS[i];
        }

        return ScoreRank.D;
    }
}
