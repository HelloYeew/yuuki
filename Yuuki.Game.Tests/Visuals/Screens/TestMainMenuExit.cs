// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface.Overlay;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestMainMenuExit : YuukiManualInputManagerTestScene
{
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();
    private readonly BeatmapSet set = BeatmapTestUtility.MakeBeatmapSet(1);

    private YuukiScreenStack screenStack = null!;
    private HoldToExitGameOverlay holdToExit = null!;
    private MainMenuScreen menu = null!;

    private int exits;

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

        holdToExit = new HoldToExitGameOverlay
        {
            Action = () => exits++,
            DisposeOnRemoval = false
        };
        Cache(holdToExit);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("a song playing", () => workingBeatmap.BeatmapSet = set);
        AddStep("show the main menu", () =>
        {
            exits = 0;

            // The stack outlives each test; start it empty.
            while (screenStack.CurrentScreen != null)
                screenStack.Exit();

            TestContent.Add(screenStack);
            TestContent.Add(holdToExit);
            screenStack.Push(menu = new MainMenuScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("it's up", () => menu.IsLoaded && menu.IsCurrentScreen);
    }

    [Test]
    public void TestHoldingEscapeQuits()
    {
        AddStep("hold Escape", () => InputManager.PressKey(Key.Escape));
        AddAssert("holding", () => holdToExit.Holding);
        AddUntilStep("quits", () => exits == 1);
        AddAssert("the screen went dark", () => holdToExit.Darkness > 0.69f);
        AddStep("let go", () => InputManager.ReleaseKey(Key.Escape));
        AddAssert("quit only once", () => exits == 1);
    }

    [Test]
    public void TestTapDoesNothing()
    {
        AddStep("tap Escape", () =>
        {
            InputManager.PressKey(Key.Escape);
            InputManager.ReleaseKey(Key.Escape);
        });
        AddAssert("not holding", () => !holdToExit.Holding);
        AddUntilStep("the dark lifts", () => holdToExit.Darkness == 0);
        AddAssert("still here", () => exits == 0);
    }

    [Test]
    public void TestLeavingTheMenuLetsGo()
    {
        AddStep("hold Escape", () => InputManager.PressKey(Key.Escape));
        AddStep("leave for another screen", () => screenStack.Push(new YuukiScreen()));
        AddAssert("not holding", () => !holdToExit.Holding);
        AddUntilStep("the dark lifts", () => holdToExit.Darkness == 0);
        AddAssert("still here", () => exits == 0);
        AddStep("let go", () => InputManager.ReleaseKey(Key.Escape));
        AddStep("back to the menu", () => screenStack.CurrentScreen!.Exit());
    }

    [Test]
    public void TestFadeOutGameRunsWhenDark()
    {
        // Its own, since quitting can't be undone.
        HoldToExitGameOverlay quitting = null!;
        bool closed = false;

        AddStep("add another overlay", () => TestContent.Add(quitting = new HoldToExitGameOverlay()));
        AddStep("quit", () => quitting.FadeOutGame(300, () => closed = true));
        AddUntilStep("closed", () => closed);
        AddAssert("fully dark", () => quitting.Darkness == 1);
        AddStep("try to hold", () => quitting.BeginHold());
        AddAssert("no hold on the way out", () => !quitting.Holding);
    }
}
