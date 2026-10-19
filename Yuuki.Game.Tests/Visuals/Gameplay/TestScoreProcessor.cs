// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Logic coverage for <see cref="ScoreProcessor"/> and <see cref="HealthProcessor"/> — combo/score
/// and health clamping.
/// </summary>
public partial class TestScoreProcessor : TestScene
{
    private static JudgementResult judgement(HitResult result) =>
        new JudgementResult(new BeatmapNote { Lane = NoteLane.Lane1, Type = NoteType.Tap }, result, 0);

    [Test]
    public void TestComboScoreAndReset()
    {
        var score = new ScoreProcessor();

        AddStep("apply 3 criticals", () =>
        {
            for (int i = 0; i < 3; i++)
                score.Apply(judgement(HitResult.Critical));
        });
        AddAssert("combo is 3", () => score.Combo.Value == 3);
        AddAssert("max combo is 3", () => score.MaxCombo.Value == 3);
        AddAssert("score is positive", () => score.Score.Value > 0);

        AddStep("apply a miss", () => score.Apply(judgement(HitResult.Miss)));
        AddAssert("combo reset to 0", () => score.Combo.Value == 0);
        AddAssert("max combo preserved", () => score.MaxCombo.Value == 3);

        AddStep("reset", () => score.Reset());
        AddAssert("score cleared", () => score.Score.Value == 0);
    }

    [Test]
    public void TestHealthClamps()
    {
        var health = new HealthProcessor();

        AddStep("apply many misses", () =>
        {
            for (int i = 0; i < 50; i++)
                health.Apply(judgement(HitResult.Miss));
        });
        AddAssert("health floored at 0", () => health.Health.Value == 0);
        AddAssert("is failed", () => health.IsFailed);

        AddStep("apply many hits", () =>
        {
            for (int i = 0; i < 500; i++)
                health.Apply(judgement(HitResult.Critical));
        });
        AddAssert("health capped at 1", () => health.Health.Value == 1);
    }

    [Test]
    public void TestPerfectRunIsExactlyMaxScore()
    {
        var score = new ScoreProcessor();
        var chart = new PlayableBeatmap(new[]
        {
            new BeatmapNote { Type = NoteType.Tap, StartTime = 0, EndTime = 0 },
            new BeatmapNote { Type = NoteType.Hold, StartTime = 100, EndTime = 300 },
            new BeatmapNote { Type = NoteType.Swipe, StartTime = 400, EndTime = 400 }
        });

        AddStep("prepare and play perfectly", () =>
        {
            score.Prepare(chart);
            // Tap, hold press, hold release, swipe: four judgements.
            foreach (BeatmapNote note in new[] { chart.Notes[0], chart.Notes[1], chart.Notes[1], chart.Notes[2] })
                score.Apply(new JudgementResult(note, HitResult.Critical, 0));
        });
        AddAssert("exactly max score", () => score.Score.Value == ScoreProcessor.MAX_SCORE);
        AddAssert("accuracy 100%", () => score.Accuracy.Value == 1);
    }

    [Test]
    public void TestScoreRisesThroughTheRun()
    {
        var score = new ScoreProcessor();
        var chart = new PlayableBeatmap(new[]
        {
            new BeatmapNote { Type = NoteType.Tap, StartTime = 0, EndTime = 0 },
            new BeatmapNote { Type = NoteType.Tap, StartTime = 100, EndTime = 100 }
        });

        AddStep("prepare and hit one", () =>
        {
            score.Prepare(chart);
            score.Apply(new JudgementResult(chart.Notes[0], HitResult.Critical, 0));
        });
        AddAssert("partway, not max", () => score.Score.Value > 0 && score.Score.Value < ScoreProcessor.MAX_SCORE);
        AddStep("break the second", () => score.Apply(new JudgementResult(chart.Notes[1], HitResult.Break, 0)));
        AddAssert("accuracy is the weighted mean", () => Math.Abs(score.Accuracy.Value - (1 + 2.0 / 3) / 2) < 1e-9);
        AddAssert("below max", () => score.Score.Value < ScoreProcessor.MAX_SCORE);
    }
}
