// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Utilities;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

/// <summary>
/// The judgements, row by row, as the results' summary shows them; closed up where its column is short.
/// </summary>
public partial class TestJudgementBreakdown : YuukiTestScene
{
    private JudgementBreakdown breakdown = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the judgements", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(breakdown = new JudgementBreakdown(ResultsTestRuns.Rich(), ResultsTestRuns.Palette)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 644,
                Progress = 1
            });
            breakdown.ShowTiming(ResultsTestRuns.Rich());
        });
    }

    [Test]
    public void TestRollingUp() => AddSliderStep("rolled up", 0f, 1f, 1f, progress => breakdown.Progress = progress);

    /// <summary>
    /// Closed up, the rows are 28 shorter in all; how tall they'd be open stays the same, so the screen can judge by it.
    /// </summary>
    [Test]
    public void TestClosedUp()
    {
        AddStep("open", () => breakdown.Compact = false);
        AddAssert("five rows of 34, 6 apart", () => Precision.AlmostEquals(breakdown.DrawHeight, 194) && Precision.AlmostEquals(breakdown.FullHeight, 194));
        AddStep("closed up", () => breakdown.Compact = true);
        AddAssert("five rows of 30, 4 apart", () => Precision.AlmostEquals(breakdown.DrawHeight, 166) && Precision.AlmostEquals(breakdown.FullHeight, 194));
        AddStep("open again", () => breakdown.Compact = false);
        AddAssert("as it was", () => Precision.AlmostEquals(breakdown.DrawHeight, 194));
    }
}
