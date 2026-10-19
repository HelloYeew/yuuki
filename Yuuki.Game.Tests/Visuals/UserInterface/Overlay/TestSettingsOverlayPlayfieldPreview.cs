// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Testing;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestSettingsOverlayPlayfieldPreview : TestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private SettingsOverlay overlay = null!;
    private PlayfieldAdjustmentContainer preview = null!;
    private Container settings = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(config);
    }

    private void open()
    {
        AddStep("open settings", () =>
        {
            Clear();
            Add(overlay = new SettingsOverlay());
            overlay.ToggleVisibility();
        });
        AddUntilStep("loaded", () => overlay.IsLoaded);
        AddStep("find the preview and the settings", () =>
        {
            preview = all(overlay).OfType<PlayfieldAdjustmentContainer>().Single();
            // The settings are the layer holding everything else, the back button included.
            settings = (Container)all(overlay).OfType<LeftBottomShearButton>().Single().Parent!;
        });
    }

    [Test]
    public void TestHiddenUntilChanged()
    {
        open();
        AddWaitStep("settle", 10);
        AddAssert("no preview", () => preview.Alpha == 0);
        AddAssert("settings at full", () => settings.Alpha == 1);
    }

    [TestCase(YuukiSetting.PlayfieldSize, 0.7)]
    [TestCase(YuukiSetting.PlayfieldPositionX, 0.2)]
    [TestCase(YuukiSetting.PlayfieldPositionY, 0.9)]
    public void TestChangeShowsPreviewAndDimsSettings(YuukiSetting setting, double value)
    {
        open();
        AddStep($"change {setting}", () => config.Get<double>(setting).Value = value);
        AddUntilStep("the preview shows", () => preview.Alpha > 0);
        AddUntilStep("the settings dim", () => settings.Alpha < 1);
        AddStep($"put {setting} back", () => config.Get<double>(setting).Value = config.Get<double>(setting).Default);
    }

    [Test]
    public void TestPreviewLetsInputThrough()
    {
        open();
        // It's drawn over the sliders it previews, which must still get the drag.
        AddAssert("doesn't take positional input", () => !preview.HandlePositionalInput);
    }

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
