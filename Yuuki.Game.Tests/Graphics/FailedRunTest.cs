// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// What a results screen says of a run that ran out of health.
/// </summary>
[TestFixture]
public class FailedRunTest
{
    /// <summary>
    /// Rounded down: nearly there isn't there.
    /// </summary>
    [Test]
    public void TestThePercentIsRoundedDown()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FailedRun.Percent(0.996), Is.EqualTo(99));
            Assert.That(FailedRun.Percent(0.7999), Is.EqualTo(79));
            Assert.That(FailedRun.Percent(0.57), Is.EqualTo(57), "a share that's a little under in binary");
            Assert.That(FailedRun.Percent(1.2), Is.EqualTo(100));
            Assert.That(FailedRun.Percent(-0.1), Is.EqualTo(0));
        }
    }

    [Test]
    public void TestTheLines()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FailedRun.Footer(0.72), Is.EqualTo("Failed · reached 72% of the song"));
            Assert.That(FailedRun.Footer(null), Is.EqualTo("Failed"));
            Assert.That(FailedRun.Ring(107_400, 150_000), Is.EqualTo("reached 1:47 of 2:30"));
            Assert.That(FailedRun.Standing(107_400), Is.EqualTo("Failed at 1:47 · not ranked"));
            Assert.That(FailedRun.Standing(null), Is.EqualTo("Failed · not ranked"));
            Assert.That(FailedRun.Tries(3, 987_654, 0.96131), Is.EqualTo("Try 3 today · your best stays 0987654 · 96.13%"));
            Assert.That(FailedRun.Tries(1, null, null), Is.EqualTo("Try 1 today · no best yet"));
            Assert.That(FailedRun.Time(61_999), Is.EqualTo("1:01"));
        }
    }

    /// <summary>
    /// How far a run got comes from where it ran out over the chart's length; a run that didn't fail, or one whose
    /// judging isn't known yet, has none.
    /// </summary>
    [Test]
    public void TestHowFarARunGot()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new GameplayResult { Failed = true, FailedAt = 90_000, ChartLength = 120_000 }.Reached, Is.EqualTo(0.75).Within(1e-9));
            Assert.That(new GameplayResult { Failed = true, FailedAt = 130_000, ChartLength = 120_000 }.Reached, Is.EqualTo(1));
            Assert.That(new GameplayResult { Failed = true, ChartLength = 120_000 }.Reached, Is.Null);
            Assert.That(new GameplayResult { Failed = true, FailedAt = 90_000 }.Reached, Is.Null);
        }
    }

    /// <summary>
    /// Not ranked, so nothing is lost to a miss, and nothing is full, whatever the counts say.
    /// </summary>
    [Test]
    public void TestAFailedRunHoldsNothing()
    {
        var result = new GameplayResult { Failed = true, CriticalCount = 40, MissCount = 0, Accuracy = 0.97 };
        var missed = new GameplayResult { Failed = true, CriticalCount = 40, MissCount = 3, Accuracy = 0.97 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFullCombo, Is.False);
            Assert.That(result.IsAllPerfect, Is.False);
            Assert.That(result.IsFullBloom, Is.False);
            Assert.That(missed.RankLostToMiss, Is.False);
            Assert.That(new GameplayResult { CriticalCount = 40, MissCount = 3, Accuracy = 0.97 }.RankLostToMiss, Is.True, "the same run, not failed");
        }
    }

    /// <summary>
    /// Judging a saved play again brings where it ran out, which the play itself doesn't keep.
    /// </summary>
    [Test]
    public void TestJudgingAgainBringsWhereItRanOut()
    {
        var saved = new GameplayResult { Failed = true, ChartLength = 120_000 };
        var judged = saved.WithJudgements(System.Array.Empty<JudgementResult>(), System.Array.Empty<double>(), 4, 120_000, 60_000);

        Assert.That(judged.FailedAt, Is.EqualTo(60_000));
        Assert.That(judged.Reached, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(judged.WithJudgements(judged.Judgements, judged.HoldBreakTimes, 4, 120_000).FailedAt, Is.EqualTo(60_000), "kept when not told again");
    }
}
