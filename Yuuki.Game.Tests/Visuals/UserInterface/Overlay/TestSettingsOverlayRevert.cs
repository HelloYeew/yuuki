// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestSettingsOverlayRevert : YuukiManualInputManagerTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private SettingsOverlay overlay = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(config);
    }

    [Test]
    public void TestClickRevertsSetting()
    {
        RevertToDefaultButton[] shownBefore = null!;
        RevertToDefaultButton? revert = null;

        AddStep("open settings", () =>
        {
            TestContent.Add(overlay = new SettingsOverlay());
            overlay.ToggleVisibility();
        });
        AddUntilStep("loaded", () => overlay.IsLoaded);
        AddWaitStep("let the buttons settle", 5);
        AddStep("note the reverts already showing", () => shownBefore = enabledReverts(overlay).ToArray());

        AddStep("turn background video off", () => config.Get<bool>(YuukiSetting.DisableVideo).Value = true);
        AddUntilStep("its revert shows", () =>
        {
            revert = enabledReverts(overlay).Except(shownBefore).SingleOrDefault();
            return revert != null && revert.Alpha > 0;
        });

        AddStep("click it", () =>
        {
            InputManager.MoveMouseTo(revert!);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("setting back to default", () => !config.Get<bool>(YuukiSetting.DisableVideo).Value);
        AddUntilStep("revert hides", () => !revert!.Enabled.Value);
    }

    /// <summary>
    /// The hitsound volume row is bound to its setting both ways. (Checked through the slider rather
    /// than the revert: the Audio section sits below the fold here, and off-screen rows don't update.)
    /// </summary>
    [Test]
    public void TestHitsoundVolumeBound()
    {
        YuukiSliderBar<double>? slider = null;

        AddStep("open settings", () =>
        {
            TestContent.Add(overlay = new SettingsOverlay());
            overlay.ToggleVisibility();
        });
        AddUntilStep("loaded", () => overlay.IsLoaded);

        AddStep("lower the hitsound volume", () => config.Get<double>(YuukiSetting.HitsoundVolume).Value = 0.4);
        AddAssert("its slider follows", () =>
        {
            slider = all(overlay).OfType<YuukiSliderBar<double>>().SingleOrDefault(s => s.Current.Value == 0.4);
            return slider != null;
        });

        AddStep("move the slider", () => slider!.Current.Value = 0.7);
        AddAssert("the setting follows", () => config.Get<double>(YuukiSetting.HitsoundVolume).Value == 0.7);

        AddStep("back to full", () => config.Get<double>(YuukiSetting.HitsoundVolume).Value = 1.0);
    }

    private static IEnumerable<RevertToDefaultButton> enabledReverts(Drawable root) => all(root).OfType<RevertToDefaultButton>().Where(b => b.Enabled.Value);

    private static IEnumerable<Drawable> all(Drawable root)
    {
        yield return root;

        if (root is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (Drawable descendant in all(child))
                    yield return descendant;
            }
        }
    }
}
