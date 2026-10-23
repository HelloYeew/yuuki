// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestMainMenuIdle : YuukiManualInputManagerTestScene
{
    [Cached]
    private readonly GameLayout gameLayout = new GameLayout();

    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();
    private readonly BeatmapSet set = BeatmapTestUtility.MakeBeatmapSet(1);

    private YuukiScreenStack screenStack = null!;
    private MainMenuScreen menu = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(workingBeatmap);
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());

        screenStack = new YuukiScreenStack
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,

            // Built once for the fixture and re-added by each test after the scene is cleared.
            DisposeOnRemoval = false
        };
        Cache(screenStack);
    }

    private void showMenu()
    {
        AddStep("a song playing", () => workingBeatmap.BeatmapSet = set);
        AddStep("show the main menu", () =>
        {
            TestContent.Add(screenStack);
            screenStack.Push(menu = new MainMenuScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("it's up", () => menu.IsLoaded);
        AddStep("a short wait before idle", () => menu.Idle.Timeout = 300);
    }

    private void leaveMenu() => AddStep("leave the menu", () => menu.Exit());

    /// <summary>
    /// A phone being read gets no input at all, where a resting mouse twitches, so by touch the menu waits longer.
    /// </summary>
    [Test]
    public void TestByTouchItWaitsLongerToGoIdle()
    {
        AddStep("by touch", () => gameLayout.Touch.Value = true);
        AddStep("show the main menu", () =>
        {
            TestContent.Add(screenStack);
            screenStack.Push(menu = new MainMenuScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("it's up", () => menu.IsLoaded);
        AddAssert("half a minute", () => menu.Idle.Timeout == 30_000);
        AddStep("a mouse again", () => gameLayout.Touch.Value = false);
        AddAssert("ten seconds", () => menu.Idle.Timeout == 10_000);
        leaveMenu();
    }

    private void goIdle()
    {
        AddUntilStep("goes idle", () => menu.Idle.IsIdle.Value);
        AddUntilStep("the idle look settles", () => menu.IdleAmount >= 1);
    }

    [Test]
    public void TestGoesIdleAndWakesOnAMouseMove()
    {
        showMenu();
        AddAssert("starts awake, logo top-left", () => !menu.Idle.IsIdle.Value && menu.LogoPosition == new Vector2(48));

        goIdle();
        AddAssert("the logo is at the bottom-left", () => menu.LogoPosition.X == 48 && menu.LogoPosition.Y > menu.DrawSize.Y / 2);
        AddAssert("the song panel is up", () => menu.IdlePanel.Alpha >= 1);
        AddAssert("it shows the song", () => menu.IdlePanel.ShownTitle == set.Title);
        AddAssert("and where it's from", () => menu.IdlePanel.ShownSource == "Test Source");
        AddAssert("the cursor is tucked away", () => menu.CursorHidden.Value);

        AddStep("move the mouse", () =>
        {
            // A long wait from here, so it stays awake while the menu comes back.
            menu.Idle.Timeout = 60_000;
            InputManager.MoveMouseTo(menu);
        });
        AddUntilStep("awake", () => !menu.Idle.IsIdle.Value);
        AddUntilStep("the menu look is back", () => menu.IdleAmount <= 0);
        AddAssert("the logo is top-left again, the panel gone", () => menu.LogoPosition == new Vector2(48) && menu.IdlePanel.Alpha <= 0);
        AddAssert("the cursor is back", () => !menu.CursorHidden.Value);

        leaveMenu();
    }

    /// <summary>The key that wakes the menu is its "Play" shortcut, which must not also start playing.</summary>
    [Test]
    public void TestWakingKeyOnlyWakes()
    {
        showMenu();
        goIdle();

        AddStep("press P", () =>
        {
            InputManager.PressKey(Key.P);
            InputManager.ReleaseKey(Key.P);
        });
        AddUntilStep("awake", () => !menu.Idle.IsIdle.Value);
        AddAssert("still on the main menu", () => screenStack.CurrentScreen == menu);

        leaveMenu();
    }

    [Test]
    public void TestWakingClickOnlyWakes()
    {
        showMenu();
        goIdle();

        AddStep("click", () =>
        {
            InputManager.PressButton(MouseButton.Left);
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddUntilStep("awake", () => !menu.Idle.IsIdle.Value);
        AddAssert("still on the main menu", () => screenStack.CurrentScreen == menu);

        leaveMenu();
    }

    [Test]
    public void TestNoIdleWhileAnotherScreenIsUp()
    {
        YuukiScreen other = null!;

        showMenu();
        AddStep("open another screen", () => screenStack.Push(other = new YuukiScreen { RelativeSizeAxes = Axes.Both }));
        AddWaitStep("well past the wait", 1000);
        AddAssert("the menu stayed awake", () => !menu.Idle.IsIdle.Value);

        AddStep("back to the menu", () => other.Exit());
        leaveMenu();
    }
}
