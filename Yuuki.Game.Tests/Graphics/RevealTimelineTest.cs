// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// When a results screen's reveal does what, as the prototype timed it.
/// </summary>
[TestFixture]
public class RevealTimelineTest
{
    [Test]
    public void TestAnSFullCombo()
    {
        var timeline = new RevealTimeline(0.98, false, true);

        Assert.That(timeline.Crossings.Select(c => c.Rank), Is.EqualTo(new[] { ScoreRank.C, ScoreRank.B, ScoreRank.A, ScoreRank.S }));
        Assert.That(timeline.ChargeStart, Is.EqualTo(2400));
        Assert.That(timeline.Landing, Is.EqualTo(2960));
        Assert.That(timeline.Badge, Is.EqualTo(3520));
        Assert.That(timeline.Best, Is.EqualTo(4060));
        Assert.That(timeline.Music, Is.EqualTo(4760));
        Assert.That(timeline.Voice, Is.EqualTo(5110));
        Assert.That(timeline.End, Is.EqualTo(5460));
    }

    [Test]
    public void TestTheFillReachesEachRankWhenItCrossesIt()
    {
        var timeline = new RevealTimeline(0.93, false, false);

        foreach ((ScoreRank rank, double time) in timeline.Crossings)
            Assert.That(timeline.FillAt(time), Is.EqualTo(rank.MinimumAccuracy()).Within(1e-4), rank.ToString());

        Assert.That(timeline.Crossings.Select(c => c.Time), Is.Ordered);
        Assert.That(timeline.FillAt(timeline.FillEnd), Is.EqualTo(0.93f).Within(1e-6));
    }

    [Test]
    public void TestALostSBreaksThenTheRankLands()
    {
        var timeline = new RevealTimeline(0.97, true, false);

        Assert.That(timeline.Crossings.Last().Rank, Is.EqualTo(ScoreRank.S));
        Assert.That(timeline.FirstLanding, Is.EqualTo(2960));
        Assert.That(timeline.Crack, Is.EqualTo(3280));
        Assert.That(timeline.Landing, Is.EqualTo(4100));

        // At the crack the fill is still at the accuracy; once pulled back it's just under S.
        Assert.That(timeline.FillAt(timeline.Crack), Is.EqualTo(0.97f).Within(1e-6));
        Assert.That(timeline.FillAt(timeline.End), Is.LessThan(0.95f).And.GreaterThan(0.949f));
    }

    [Test]
    public void TestTicksOnlyWhileTheFillMoves()
    {
        var timeline = new RevealTimeline(0.85, false, false);
        double[] ticks = timeline.Ticks.ToArray();

        Assert.That(ticks.First(), Is.EqualTo(RevealTimeline.FILL_START));
        Assert.That(ticks.Last(), Is.LessThan(RevealTimeline.FILL_START + 0.75 * RevealTimeline.FILL_DURATION));
        Assert.That(ticks.Length, Is.EqualTo(32));
    }

    [Test]
    public void TestSkipping()
    {
        var timeline = new RevealTimeline(0.97, true, false);

        // First to just before the S lands, so its hit is still heard; then nearly to the end.
        Assert.That(timeline.SkipFrom(500), Is.EqualTo(2740));
        Assert.That(timeline.SkipFrom(2740), Is.EqualTo(timeline.End - 300));
        Assert.That(timeline.SkipFrom(timeline.End + 100), Is.EqualTo(timeline.End + 100));
    }

    [Test]
    public void TestAnEmptyRunCrossesNothing()
    {
        var timeline = new RevealTimeline(0, false, false);

        Assert.That(timeline.Crossings, Is.Empty);
        Assert.That(timeline.FillAt(timeline.End), Is.EqualTo(0));
    }

    /// <summary>
    /// A Full Bloom: the all perfect's badge, then the gather, the bloom, the cut-in and the badge again, and only then
    /// the comparison with the best and the music.
    /// </summary>
    [Test]
    public void TestAFullBloom()
    {
        var timeline = new RevealTimeline(1, false, true, true);

        Assert.That(timeline.FullBloom);
        Assert.That(timeline.Badge, Is.EqualTo(3520));
        Assert.That(timeline.Gather, Is.EqualTo(4040));
        Assert.That(timeline.Bloom, Is.EqualTo(4680));
        Assert.That(timeline.BloomBadge, Is.EqualTo(5830));
        Assert.That(timeline.Best, Is.EqualTo(6370));
        Assert.That(timeline.GatherAt(4040), Is.EqualTo(0));
        Assert.That(timeline.GatherAt(4040 + 560), Is.EqualTo(1));
        Assert.That(timeline.GatherAt(timeline.Bloom), Is.EqualTo(0));
    }

    [Test]
    public void TestOnlyAFullBloomBlooms()
    {
        var timeline = new RevealTimeline(1, false, true);

        Assert.That(timeline.FullBloom, Is.False);
        Assert.That(timeline.Best, Is.EqualTo(timeline.Badge + RevealTimeline.BEST_AFTER));
        Assert.That(timeline.GatherAt(4300), Is.EqualTo(0));
    }

    /// <summary>
    /// A run that failed: one fill with nothing building up, FAILED just after it settles, then the music. No badge, never
    /// compared with the best, and nothing lost or full, whatever it was told.
    /// </summary>
    [Test]
    public void TestAFailedRun()
    {
        var timeline = new RevealTimeline(0.97, true, true, true, failed: true);

        Assert.That(timeline.Failed);
        Assert.That(timeline.RankLost, Is.False);
        Assert.That(timeline.FullBloom, Is.False);
        Assert.That(timeline.FirstLanding, Is.EqualTo(2620));
        Assert.That(timeline.Landing, Is.EqualTo(2620));
        Assert.That(timeline.Badge, Is.NaN);
        Assert.That(timeline.Best, Is.EqualTo(double.PositiveInfinity));
        Assert.That(timeline.Music, Is.EqualTo(3520));
        Assert.That(timeline.Voice, Is.EqualTo(3870));
        Assert.That(timeline.End, Is.EqualTo(4220));

        // Nothing charges, but the fill still crosses the ranks for its color, and ends on the accuracy.
        Assert.That(Enumerable.Range(0, 45).Select(i => timeline.ChargeAt(i * 100)), Has.All.EqualTo(0));
        Assert.That(timeline.Crossings.Last().Rank, Is.EqualTo(ScoreRank.S));
        Assert.That(timeline.FillAt(timeline.End), Is.EqualTo(0.97f).Within(1e-6));
        Assert.That(timeline.SkipFrom(500), Is.EqualTo(2400));
    }
}
