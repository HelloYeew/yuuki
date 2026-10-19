// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Screens;
using Yuuki.Game.Graphics.Screens;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestScreenExitCascade : TestYuukiScreenStack
{
    [Test]
    public void TestCascadeAppliesLandingScreenState()
    {
        var baseScreen = new CascadeTestScreen(hideFooter: false, exitOnResume: false);
        var setup = new CascadeTestScreen(hideFooter: false, exitOnResume: true);
        var gameplay = new CascadeTestScreen(hideFooter: true, exitOnResume: true);
        var results = new CascadeTestScreen(hideFooter: false, exitOnResume: false);

        AddStep("push base (song select)", () => screenStack.Push(baseScreen));
        AddStep("push setup", () => screenStack.Push(setup));
        AddStep("push gameplay", () => screenStack.Push(gameplay));
        AddStep("push results", () => screenStack.Push(results));

        AddUntilStep("results is applied", () => screenStack.AppliedScreen == results);

        // Exiting results cascades all the way back to the base screen in one chain.
        AddStep("exit results (cascade)", () => results.Exit());

        AddUntilStep("landed on base screen", () => screenStack.CurrentScreen == baseScreen);
        // The bug applied gameplay's state (footer hidden) last; the fix must apply the base screen's.
        AddUntilStep("state applied to base, not a stale intermediate", () => screenStack.AppliedScreen == baseScreen);
    }

    [Test]
    public void TestExitToStopsAtTheScreenAsked()
    {
        var menu = new MenuTestScreen();
        var setup = new CascadeTestScreen(hideFooter: false, exitOnResume: true);
        var gameplay = new CascadeTestScreen(hideFooter: true, exitOnResume: true);
        var results = new CascadeTestScreen(hideFooter: false, exitOnResume: false);

        AddStep("push menu", () => screenStack.Push(menu));
        AddStep("push setup", () => screenStack.Push(setup));
        AddStep("push gameplay", () => screenStack.Push(gameplay));
        AddStep("push results", () => screenStack.Push(results));

        AddAssert("nothing to stop at for a screen that isn't there", () => !screenStack.ExitTo<AbsentTestScreen>() && screenStack.CurrentScreen == results);

        // Leaving results sets the cascade off part way; ExitTo has to pick up where it lands.
        AddAssert("exit to the menu", () => screenStack.ExitTo<MenuTestScreen>());
        AddAssert("the menu is current", () => screenStack.CurrentScreen == menu);
        AddUntilStep("its state applied", () => screenStack.AppliedScreen == menu);
        AddStep("leave the menu", () => menu.Exit());
    }

    private partial class MenuTestScreen : YuukiScreen
    {
    }

    private partial class AbsentTestScreen : YuukiScreen
    {
    }

    /// <summary>A minimal <see cref="YuukiScreen"/> that can auto-exit on resume to drive the cascade.</summary>
    private partial class CascadeTestScreen : YuukiScreen
    {
        private readonly bool hideFooter;
        private readonly bool exitOnResume;

        public override bool HideFooter => hideFooter;

        public CascadeTestScreen(bool hideFooter, bool exitOnResume)
        {
            this.hideFooter = hideFooter;
            this.exitOnResume = exitOnResume;
        }

        public override void OnResuming(Screen? last)
        {
            base.OnResuming(last);
            if (exitOnResume)
                Exit();
        }
    }
}
