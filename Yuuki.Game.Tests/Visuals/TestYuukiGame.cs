// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics.Cursor;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;

namespace Yuuki.Game.Tests.Visuals;

[TestFixture]
public partial class TestYuukiGame : TestScene
{
    private AppHost host = null!;
    private YuukiGame game = null!;

    [BackgroundDependencyLoader]
    private void load(AppHost host)
    {
        this.host = host;
    }

    // A fresh game per test: a game that's been shown is disposed when the scene is cleared.
    private void addGame() => AddStep("Add yuuki game", () =>
    {
        game = new YuukiGame()
        {
            RelativeSizeAxes = Axes.Both,
            Clock = Clock
        };
        game.SetHost(host);
        game.OnReady();
        AddApp(game);
    });

    [Test]
    public void Test()
    {
        addGame();
    }

    /// <summary>
    /// The game's cursor follows the current screen's wish to hide it (gameplay, the idle menu). The
    /// screen stack can only do that if it found the cursor when it loaded.
    /// </summary>
    [Test]
    public void TestCursorFollowsTheScreen()
    {
        YuukiScreenStack stack = null!;
        YuukiCursorContainer cursor = null!;

        addGame();
        AddUntilStep("a screen is up", () =>
        {
            stack = descendants(game).OfType<YuukiScreenStack>().FirstOrDefault()!;
            cursor = descendants(game).OfType<YuukiCursorContainer>().FirstOrDefault()!;
            return stack?.CurrentScreen is YuukiScreen && stack.AppliedScreen == stack.CurrentScreen && cursor != null;
        });

        AddStep("the screen hides the cursor", () => ((YuukiScreen)stack.CurrentScreen!).CursorHidden.Value = true);
        AddAssert("the cursor hides", () => cursor.Hidden.Value);
        AddStep("and shows it again", () => ((YuukiScreen)stack.CurrentScreen!).CursorHidden.Value = false);
        AddAssert("the cursor is back", () => !cursor.Hidden.Value);
    }

    /// <summary>
    /// A phone's Back is Escape: it closes what's open. At the main menu, with nothing open, it's the
    /// system's, which puts the game in the background.
    /// </summary>
    [Test]
    public void TestBackIsEscapeButAtTheMainMenu()
    {
        YuukiScreenStack stack = null!;
        bool handled = false;

        addGame();
        AddUntilStep("a screen is up", () =>
        {
            stack = descendants(game).OfType<YuukiScreenStack>().FirstOrDefault()!;
            return stack?.CurrentScreen is YuukiScreen && stack.AppliedScreen == stack.CurrentScreen;
        });

        AddStep("open the settings", () => game.ToggleSettingsOverlay());
        AddUntilStep("open", () => game.IsOverlayOpen);
        AddStep("Back", () => handled = back());
        AddAssert("taken", () => handled);
        AddUntilStep("closed", () => !game.IsOverlayOpen);

        AddStep("go to the main menu", () => stack.Push(new MainMenuScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("at the main menu", () => stack.CurrentScreen is MainMenuScreen && stack.AppliedScreen == stack.CurrentScreen);
        AddStep("Back", () => handled = back());
        AddAssert("left to the system", () => !handled);
    }

    /// <summary>
    /// Every finger reaches the touch effects, whatever takes it, and they keep out of a screen that hides the
    /// cursor (a song being played).
    /// </summary>
    [Test]
    public void TestFingersReachTheTouchEffects()
    {
        YuukiScreenStack stack = null!;
        TouchEffects effects = null!;

        addGame();
        AddUntilStep("a screen is up", () =>
        {
            stack = descendants(game).OfType<YuukiScreenStack>().FirstOrDefault()!;
            effects = descendants(game).OfType<TouchEffects>().FirstOrDefault()!;
            return stack?.CurrentScreen is YuukiScreen && stack.AppliedScreen == stack.CurrentScreen && effects != null;
        });

        AddStep("the screen shows the cursor", () => ((YuukiScreen)stack.CurrentScreen!).CursorHidden.Value = false);
        AddStep("tap", () => tap(effects));
        AddAssert("a burst", () => effects.ActiveParticles > 0);
        AddUntilStep("gone", () => effects.ActiveParticles == 0);

        AddStep("the screen hides the cursor", () => ((YuukiScreen)stack.CurrentScreen!).CursorHidden.Value = true);
        AddStep("tap", () => tap(effects));
        AddAssert("nothing", () => effects.ActiveParticles == 0);
    }

    private void tap(Drawable on)
    {
        var position = on.ToScreenSpace(on.DrawSize / 2);
        game.OnTouchDown(new TouchEvent(TouchSource.Touch1, position));
        game.OnTouchUp(new TouchEvent(TouchSource.Touch1, position));
    }

    private bool back()
    {
        var e = new KeyEvent(Key.SystemBack, default, false);
        bool taken = game.OnKeyDown(e);
        game.OnKeyUp(e);
        return taken;
    }

    private static IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (Drawable d in descendants(child))
                    yield return d;
            }
        }
    }
}
