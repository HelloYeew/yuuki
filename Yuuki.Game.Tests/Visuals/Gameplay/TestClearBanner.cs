// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// The banner at the end of a song: a clear, a full combo, an all perfect and a Full Bloom.
/// </summary>
public partial class TestClearBanner : YuukiTestScene
{
    private ClearBanner banner = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the playfield's dark", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(banner = new ClearBanner());
        });
    }

    [Test]
    public void TestEachBanner()
    {
        AddStep("clear", () => banner.Play("Clear", ColorExtensions.FromHex("#6E5262")));
        AddWaitStep("let it pass", (int)ClearBanner.DURATION);
        AddStep("full combo", () => banner.Play("Full Combo", JudgementColors.FULL_COMBO));
        AddWaitStep("let it pass", (int)ClearBanner.DURATION);
        AddStep("all perfect", () => banner.Play("All Perfect", JudgementColors.ALL_PERFECT));
        AddWaitStep("let it pass", (int)ClearBanner.DURATION);
        AddStep("full bloom", () => banner.PlayFullBloom());
        AddWaitStep("let it pass", (int)ClearBanner.DURATION);
        AddStep("and back to plain", () => banner.Play("Clear", ColorExtensions.FromHex("#6E5262")));
    }
}
