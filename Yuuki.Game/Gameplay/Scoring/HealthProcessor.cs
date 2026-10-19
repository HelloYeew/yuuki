// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// Tracks character health (0–1) from judgments. Misses hurt, hits nudge health back up, and
/// <see cref="SkillType.Heal"/> skill notes restore a larger chunk.
/// </summary>
public class HealthProcessor
{
    public Reactive<double> Health { get; } = new Reactive<double>(1);

    public double MissDamage { get; set; } = 0.07;
    public double HitRecovery { get; set; } = 0.01;
    public double HealAmount { get; set; } = 0.1;

    /// <summary>
    /// Whether health has been fully depleted.
    /// </summary>
    public bool IsFailed => Health.Value <= 0;

    public void Apply(JudgementResult result)
    {
        if (result.Note.Type == NoteType.Skill && result.Note.Skill == SkillType.Heal && result.Result.IsHit())
        {
            adjust(HealAmount);
            return;
        }

        adjust(result.Result.IsHit() ? HitRecovery : -MissDamage);
    }

    private void adjust(double delta) => Health.Value = Math.Clamp(Health.Value + delta, 0, 1);

    public void Reset() => Health.Value = 1;
}
