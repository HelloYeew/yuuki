// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestSettingsOverlay : YuukiTestScene
{
    private SettingsOverlay overlay = null!;

    [Cached]
    private readonly YuukiConfigManager configManager = new YuukiInMemoryConfigManager();

    [SetUp]
    public void SetUp()
    {
        AddStep("Add overlay", () => Add(overlay = new SettingsOverlay()));
        AddStep("Pop in overlay", () => overlay.ToggleVisibility());
    }

    [Test]
    public void TestOverlayVisibility()
    {
    }
}
