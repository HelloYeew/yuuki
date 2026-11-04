// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Tests.Gameplay;

/// <summary>
/// What the results' More info page reads off a run.
/// </summary>
[TestFixture]
public class PlayAnalysisTest
{
    private static JudgementResult tap(double time, HitResult result, double error = 0, int lane = 0, NoteType type = NoteType.Tap) =>
        new JudgementResult(new BeatmapNote { Type = type, Lane = (NoteLane)lane, StartTime = time, EndTime = time }, result, error);

    private static PlayAnalysis analyse(IEnumerable<JudgementResult> judgements, double length = 0, int lanes = 4, params double[] holdBreaks) =>
        new PlayAnalysis(new GameplayResult { Judgements = judgements.ToArray(), HoldBreakTimes = holdBreaks, ChartLength = length, LaneCount = lanes });

    [Test]
    public void TestAMissHasNoTiming()
    {
        var analysis = analyse(new[] { tap(1000, HitResult.Critical, 30), tap(2000, HitResult.Miss, 190) });

        Assert.That(analysis.Points.Select(p => p.Error), Is.EqualTo(new double?[] { 30, null }));
        Assert.That(analysis.TimingErrors, Is.EqualTo(new double[] { 30 }));
    }

    /// <summary>
    /// A hold's release goes where the hold ends.
    /// </summary>
    [Test]
    public void TestAReleaseGoesAtTheHoldsEnd()
    {
        var hold = new BeatmapNote { Type = NoteType.Hold, StartTime = 1000, EndTime = 1800 };
        var analysis = analyse(new[] { new JudgementResult(hold, HitResult.Bloom, 5), new JudgementResult(hold, HitResult.Critical, -20, true) });

        Assert.That(analysis.Points.Select(p => p.Time), Is.EqualTo(new double[] { 1000, 1800 }));
        Assert.That(analysis.NoteTypes, Is.EqualTo(new[] { new NoteTypeFigures("Hold", 1, 1), new NoteTypeFigures("Release", 1, 1) }));
    }

    /// <summary>
    /// The drift follows the presses round each second, once there are enough of them.
    /// </summary>
    [Test]
    public void TestTheDrift()
    {
        // Ten presses 20 ms late over the first five seconds, then ten 20 ms early over the next.
        var judgements = Enumerable.Range(0, 10).Select(i => tap(i * 500, HitResult.Critical, 20))
                                   .Concat(Enumerable.Range(10, 10).Select(i => tap(i * 500, HitResult.Critical, -20)));
        var analysis = analyse(judgements, length: 20_000);

        Assert.That(analysis.Drift, Has.Count.EqualTo(21));
        Assert.That(analysis.Drift[0], Is.EqualTo(20));
        Assert.That(analysis.Drift[9], Is.EqualTo(-20));
        Assert.That(analysis.Drift[5], Is.EqualTo(0).Within(5), "half and half round the change");
        Assert.That(analysis.Drift[20], Is.Null, "too few presses near the end");
    }

    [Test]
    public void TestTheDensity()
    {
        // Four notes a second through the first two seconds.
        var analysis = analyse(Enumerable.Range(0, 8).Select(i => tap(i * 250, HitResult.Critical)), length: 4000);

        Assert.That(analysis.Density[2], Is.EqualTo(4), "at one second");
        Assert.That(analysis.Density[8], Is.Zero, "at four");
    }

    /// <summary>
    /// Health and combo as they went: a miss costs health and breaks the combo, and so does a hold let go of, with no
    /// judgement of its own.
    /// </summary>
    [Test]
    public void TestHealthAndCombo()
    {
        var analysis = analyse(new[]
        {
            tap(1000, HitResult.Critical), tap(2000, HitResult.Critical), tap(3000, HitResult.Miss, 180), tap(4000, HitResult.Break, 50), tap(6000, HitResult.Hit)
        }, 8000, 4, 5000);

        Assert.That(analysis.Combo.Select(c => c.Combo), Is.EqualTo(new[] { 0, 1, 2, 0, 1, 0, 1 }));
        Assert.That(analysis.ComboBreaks, Is.EqualTo(new double[] { 3180, 5000 }));
        Assert.That(analysis.MaxCombo, Is.EqualTo(2));
        Assert.That(analysis.Health.Last().Health, Is.EqualTo(1 - 0.07 + 0.02).Within(1e-9));
        Assert.That(analysis.FailedAt, Is.Null);
    }

    [Test]
    public void TestWhereHealthRanOut()
    {
        var analysis = analyse(Enumerable.Range(0, 20).Select(i => tap(1000 + i * 100, HitResult.Miss, 180)));

        // The fifteenth miss, judged as its window closed.
        Assert.That(analysis.FailedAt, Is.EqualTo(1000 + 14 * 100 + 180));
    }

    /// <summary>
    /// The hardest stretch is where the most was lost, a miss more than a Break: the first stretch to hold both misses,
    /// from its first note to its last.
    /// </summary>
    [Test]
    public void TestTheHardestStretch()
    {
        var judgements = Enumerable.Range(0, 60).Select(i => tap(i * 500, i is 10 or 11 ? HitResult.Break : i is 44 or 46 ? HitResult.Miss : HitResult.Critical, i is 44 or 46 ? 180 : 0));
        HardestStretch? hardest = analyse(judgements, length: 30_000).Hardest;

        Assert.That(hardest, Is.Not.Null);
        Assert.That((hardest!.Value.Start, hardest.Value.End, hardest.Value.Misses), Is.EqualTo((15_500.0, 23_000.0, 2)));
        Assert.That(hardest.Value.Accuracy, Is.EqualTo(14 / 16.0).Within(1e-9));
    }

    /// <summary>
    /// A run that went worst at its start, after an intro with no notes: the stretch starts at the first note, not in the
    /// silence before it.
    /// </summary>
    [Test]
    public void TestAStretchStartsAtItsNotes()
    {
        var judgements = Enumerable.Range(0, 40).Select(i => tap(3000 + i * 400, i < 4 ? HitResult.Break : HitResult.Critical, i < 4 ? -60 : 0));
        HardestStretch? hardest = analyse(judgements, length: 20_000).Hardest;

        Assert.That(hardest?.Start, Is.EqualTo(3000));
        Assert.That(hardest?.End, Is.LessThan(3000 + PlayAnalysis.HARDEST_LENGTH));
    }

    [Test]
    public void TestNothingLostHasNoHardestStretch() =>
        Assert.That(analyse(Enumerable.Range(0, 30).Select(i => tap(i * 500, HitResult.Bloom)), length: 15_000).Hardest, Is.Null);

    /// <summary>
    /// Each lane on its own: its accuracy, how early or late it was pressed, and its misses.
    /// </summary>
    [Test]
    public void TestTheLanes()
    {
        var analysis = analyse(new[]
        {
            tap(0, HitResult.Critical, 10, lane: 0), tap(500, HitResult.Critical, 20, lane: 0),
            tap(1000, HitResult.Break, -60, lane: 1), tap(1500, HitResult.Miss, 180, lane: 1)
        });

        Assert.That(analysis.Lanes, Has.Count.EqualTo(4));
        Assert.That(analysis.Lanes[0], Is.EqualTo(new LaneFigures(0, 1, 15, 0, 2)));
        Assert.That(analysis.Lanes[1].Accuracy, Is.EqualTo(1 / 3.0).Within(1e-9));
        Assert.That(analysis.Lanes[1].MeanError, Is.EqualTo(-60));
        Assert.That(analysis.Lanes[1].Misses, Is.EqualTo(1));
        Assert.That(analysis.Lanes[2], Is.EqualTo(new LaneFigures(2, 1, null, 0, 0)), "nothing in it");
    }

    [Test]
    public void TestWhatWasOffAndTheStreak()
    {
        var analysis = analyse(new[]
        {
            tap(0, HitResult.Bloom), tap(100, HitResult.Critical, -40), tap(200, HitResult.Break, -60), tap(300, HitResult.Critical),
            tap(400, HitResult.Bloom), tap(500, HitResult.Bloom), tap(600, HitResult.Hit, 100), tap(700, HitResult.Break, 50)
        });

        Assert.That((analysis.EarlyOff, analysis.LateOff), Is.EqualTo((1, 2)));
        Assert.That(analysis.LongestCriticalStreak, Is.EqualTo(3));
    }
}
