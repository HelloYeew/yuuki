// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestScreenPalette : YuukiManualInputManagerTestScene
{
    private YuukiScreenStack screenStack = null!;
    private Probe onScreen = null!;
    private Probe offScreen = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [Test]
    public void TestOnAnEditScreen()
    {
        AddStep("an Edit screen, and something outside it", () =>
        {
            TestContent.Add(screenStack = new YuukiScreenStack { RelativeSizeAxes = Axes.Both });
            TestContent.Add(offScreen = new Probe());

            var screen = new PartScreen(YuukiScreenPart.Edit);
            screen.Add(onScreen = new Probe());
            screenStack.Push(screen);
        });
        AddUntilStep("loaded", () => onScreen.IsLoaded && offScreen.IsLoaded);

        AddAssert("on the screen: Edit's", () => onScreen.Resolved.Part == YuukiScreenPart.Edit
                                                 && onScreen.Resolved.Palette.Control == DefaultYuukiColorProvider.PALETTES[YuukiScreenPart.Edit].Control);
        AddAssert("outside: none, so Play's", () => offScreen.Palette.Control == DefaultYuukiColorProvider.PALETTES[YuukiScreenPart.Play].Control);
    }

    [Test]
    public void TestRolesKeepTheDesignColours()
    {
        AddAssert("Play's dark fill and backdrop", () =>
            DefaultYuukiColorProvider.PALETTES[YuukiScreenPart.Play].Control == ColorExtensions.FromHex("#3B324A")
            && DefaultYuukiColorProvider.PALETTES[YuukiScreenPart.Play].Backdrop == ColorExtensions.FromHex("#151D30"));
        AddAssert("Edit's dark fill is its dark colour", () => DefaultYuukiColorProvider.PALETTES[YuukiScreenPart.Edit].Control == ColorExtensions.FromHex("#045384"));
        AddAssert("Edit's light accent is its light teal", () => DefaultYuukiColorProvider.PALETTES[YuukiScreenPart.Edit].AccentLight == ColorExtensions.FromHex("#94C4D0"));
        AddAssert("The main menu's dialog dim is unchanged", () => DefaultYuukiColorProvider.GetDefaultPalette().Backdrop == ColorExtensions.FromHex("#632621"));
    }

    private partial class PartScreen : YuukiScreen
    {
        private readonly YuukiScreenPart part;

        public PartScreen(YuukiScreenPart part)
        {
            this.part = part;
        }

        public override YuukiScreenPart ScreenPart => part;
    }

    private partial class Probe : Container
    {
        public ScreenPalette Resolved = null!;
        public YuukiColorPalette Palette;

        [BackgroundDependencyLoader]
        private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
        {
            Resolved = screenPalette!;
            Palette = ScreenPalette.Resolve(screenPalette, colorProvider);
        }
    }
}
