// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// Runs the skills set off by <see cref="NoteType.Skill"/> notes. Heal is instant (the
/// <see cref="HealthProcessor"/> applies it); Break -> Critical lasts <see cref="BREAK_TO_CRITICAL_DURATION"/>
/// and upgrades judgments through <see cref="Filter"/> before they're shown or scored.
/// </summary>
public class SkillProcessor
{
    public const double BREAK_TO_CRITICAL_DURATION = 10_000;

    // Gameplay time the Break -> Critical window closes; −∞ when it isn't running.
    private double breakToCriticalEnd = double.NegativeInfinity;

    /// <summary>
    /// Time left on Break -> Critical, in ms (0 when it isn't running). For the HUD.
    /// </summary>
    public Reactive<double> BreakToCriticalRemaining { get; } = new Reactive<double>(0);

    /// <summary>
    /// Starts a skill when its note is hit.
    /// </summary>
    public void Apply(JudgementResult result, double time)
    {
        if (result.Note.Type != NoteType.Skill || !result.Result.IsHit())
            return;

        if (result.Note.Skill == SkillType.BreakToCritical)
            breakToCriticalEnd = time + BREAK_TO_CRITICAL_DURATION;
    }

    /// <summary>
    /// A judgement as the running skills would have it — a Break counts as a Critical while Break -> Critical runs.
    /// </summary>
    public JudgementResult Filter(JudgementResult result, double time)
    {
        if (result.Result == HitResult.Break && time < breakToCriticalEnd)
            return new JudgementResult(result.Note, HitResult.Critical, result.TimingError, result.IsHoldTail);

        return result;
    }

    public void Update(double time) => BreakToCriticalRemaining.Value = Math.Max(0, breakToCriticalEnd - time);

    public void Reset()
    {
        breakToCriticalEnd = double.NegativeInfinity;
        BreakToCriticalRemaining.Value = 0;
    }
}
