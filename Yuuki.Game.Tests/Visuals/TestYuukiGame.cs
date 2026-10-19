// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
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
