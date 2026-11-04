// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Gameplay.Replays;

/// <summary>
/// A play saved before BLOOM judged again as it was played: every Bloom is the Critical it was then (a Bloom is a
/// Critical within half its window), so its replay agrees with the counts it was saved with. Only for plays the migration
/// couldn't bring up to date (<see cref="Database.ScoreMigration"/>), which stay as they were played.
/// </summary>
public static class BloomlessJudgement
{
    /// <summary>
    /// Whether a saved play is one to judge without Blooms: it's still under older rules, has notes, and none was a Bloom.
    /// </summary>
    public static bool AppliesTo(ScoreInfo score) =>
        score.RulesVersion < ScoreInfo.CURRENT_RULES && score.BloomCount == 0 && score.CriticalCount + score.BreakCount + score.HitCount + score.MissCount > 0;

    /// <summary>
    /// <paramref name="judgement"/>, a Bloom made a Critical when <paramref name="bloomless"/>.
    /// </summary>
    public static JudgementResult Apply(JudgementResult judgement, bool bloomless) =>
        bloomless && judgement.Result == HitResult.Bloom
            ? new JudgementResult(judgement.Note, HitResult.Critical, judgement.TimingError, judgement.IsHoldTail)
            : judgement;
}
