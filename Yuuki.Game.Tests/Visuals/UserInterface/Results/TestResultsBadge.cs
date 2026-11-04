// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestResultsBadge : YuukiTestScene
{
    private ResultsBadge badge = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show a badge", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(badge = new ResultsBadge { Scale = new Vector2(2) });
        });
    }

    [Test]
    public void TestStamping()
    {
        AddSliderStep("zoom", 0.5f, 4f, 2f, scale => badge.Scale = new Vector2(scale));
        AddStep("a full combo's", () =>
        {
            badge.Look(JudgementColors.FULL_COMBO, "FULL COMBO");
            badge.Shining = false;
            badge.Stamp();
        });
        AddWaitStep("let it settle", 1000);
        AddStep("an all perfect's: it keeps shining", () =>
        {
            badge.Look(JudgementColors.ALL_PERFECT, "ALL PERFECT");
            badge.Shining = true;
            badge.Stamp();
        });
        AddStep("clear", () => badge.Clear());
    }

    /// <summary>
    /// Stamped at a time already past, it shows as it would be by now: there, at its size.
    /// </summary>
    [Test]
    public void TestStampingInThePast()
    {
        AddStep("stamp a second ago", () => badge.Stamp(badge.Clock.CurrentTime - 1000));
        AddUntilStep("it's there, at its size", () => badge.Children[0].Alpha == 1 && Precision.AlmostEquals(badge.Children[0].Scale.X, 1));
    }
}
