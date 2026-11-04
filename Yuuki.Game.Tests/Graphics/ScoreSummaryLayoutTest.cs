// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// The results screen's score summary in the width its column has, with the widths its pieces measure in a real window
/// (Quicksand and Nunito): a run with a best, so every statistic has its change.
/// </summary>
[TestFixture]
public class ScoreSummaryLayoutTest
{
    // "SCORE", NEW BEST and "+48,672" in the title row; the number under it is a little narrower.
    private const float score = 283.1f;

    // Accuracy 97.98% (+9.69%), max combo 742x / 742 (+611), unstable rate 308.
    private static readonly SummaryStatisticWidths[] chart_of_742 =
    {
        new SummaryStatisticWidths(82.2f, 115.1f, 57.8f),
        new SummaryStatisticWidths(96.2f, 178.1f, 38.4f),
        new SummaryStatisticWidths(124.7f, 61.2f)
    };

    // A long chart: 1999x / 1999 and −1234 against the best.
    private static readonly SummaryStatisticWidths[] chart_of_1999 =
    {
        new SummaryStatisticWidths(82.2f, 115.1f, 67.4f),
        new SummaryStatisticWidths(96.2f, 208.8f, 48f),
        new SummaryStatisticWidths(124.7f, 61.2f)
    };

    /// <summary>
    /// The summary's width in each column: desktop's, a wide phone's, an iPhone Pro Max's (inside its safe area, as it was
    /// reported), and a 16:9 phone's.
    /// </summary>
    [TestCase(1024, ScoreSummaryArrangement.OneLine)]
    [TestCase(814, ScoreSummaryArrangement.Rows)]
    [TestCase(644, ScoreSummaryArrangement.Rows)]
    [TestCase(544, ScoreSummaryArrangement.Stacked)]
    public void TestTheArrangementForTheColumn(float width, ScoreSummaryArrangement expected) =>
        Assert.That(ScoreSummaryLayout.Choose(width, score, chart_of_742), Is.EqualTo(expected));

    /// <summary>
    /// One line only where the statistics clear the score: at 814 they'd have been 1 apart, and closer still with longer
    /// numbers.
    /// </summary>
    [Test]
    public void TestOneLineNeedsRoomForItsGap()
    {
        float oneLine = score + ScoreSummaryLayout.ONE_LINE_GAP + ScoreSummaryLayout.LineWidth(chart_of_742, ScoreSummaryLayout.COLUMN_SPACING);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(oneLine, Is.EqualTo(845f).Within(0.1f));
            Assert.That(ScoreSummaryLayout.Choose(oneLine, score, chart_of_742), Is.EqualTo(ScoreSummaryArrangement.OneLine));
            Assert.That(ScoreSummaryLayout.Choose(oneLine - 1, score, chart_of_742), Is.EqualTo(ScoreSummaryArrangement.Rows));
        }
    }

    /// <summary>
    /// A long chart's numbers still go beside the score on the Pro Max, and under it once the column is narrower.
    /// </summary>
    [Test]
    public void TestALongChartsNumbers()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScoreSummaryLayout.Choose(644, score, chart_of_1999), Is.EqualTo(ScoreSummaryArrangement.Rows));
            Assert.That(ScoreSummaryLayout.Choose(631, score, chart_of_1999), Is.EqualTo(ScoreSummaryArrangement.Stacked));
        }
    }

    /// <summary>
    /// Each statistic in a line is as wide as its widest piece; in rows, the titles take one column and the values (at the
    /// row's size) and changes follow, a change only where there is one.
    /// </summary>
    [Test]
    public void TestTheWidths()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScoreSummaryLayout.LineWidth(chart_of_742, 56), Is.EqualTo(115.1f + 178.1f + 124.7f + 2 * 56).Within(0.01f));
            Assert.That(ScoreSummaryLayout.TitleColumnWidth(chart_of_742), Is.EqualTo(124.7f));
            Assert.That(ScoreSummaryLayout.RowsWidth(chart_of_742), Is.EqualTo(124.7f + 8 + 178.1f * 26 / 36 + 8 + 38.4f).Within(0.01f));
            Assert.That(ScoreSummaryLayout.RowsWidth(new[] { new SummaryStatisticWidths(124.7f, 61.2f) }),
                Is.EqualTo(124.7f + 8 + 61.2f * 26 / 36).Within(0.01f), "no change, no space for one");
            Assert.That(ScoreSummaryLayout.LineWidth(System.Array.Empty<SummaryStatisticWidths>(), 56), Is.Zero);
        }
    }

    /// <summary>
    /// As tall as what's in it: the score and its statistics side by side, or one over the other with the gap, never a
    /// height the pieces don't fit (stacked was once 180 for 219 of them, so the statistics ran over the score).
    /// </summary>
    [Test]
    public void TestTheHeight()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScoreSummaryLayout.Height(ScoreSummaryArrangement.Stacked, 120, 91), Is.EqualTo(219));
            Assert.That(ScoreSummaryLayout.Height(ScoreSummaryArrangement.Rows, 120, 105.5f), Is.EqualTo(120));
            Assert.That(ScoreSummaryLayout.Height(ScoreSummaryArrangement.OneLine, 110, 91), Is.EqualTo(110));
        }
    }
}
