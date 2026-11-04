// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// A run that ran out of health can be looked at: the fail menu's Results opens it on the results screen, whose Back comes
/// back to the menu and whose Retry starts over.
/// </summary>
public partial class TestGameplayFailResults : YuukiManualInputManagerTestScene
{
    [Cached]
    private readonly GameLayout gameLayout = new GameLayout();

    private YuukiScreenStack stack = null!;
    private GameplayScreen gameplay = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        // One stack for the fixture, as screens resolve it; each test puts it back after the scene's cleared.
        stack = new YuukiScreenStack
        {
            RelativeSizeAxes = Axes.Both,
            DisposeOnRemoval = false
        };
        Cache(stack);
    }

    [Test]
    public void TestTheFailMenuOpensTheResults()
    {
        fail();

        AddStep("click Results", () => click("Results"));
        AddUntilStep("the results of the run that failed", () => stack.CurrentScreen is ScoreScreen { Result.Failed: true, Result.FailedAt: > 0 });
        AddUntilStep("the menu went", () => !failMenuShown);

        AddStep("press Back", () => ((ScoreScreen)stack.CurrentScreen!).Exit());
        AddUntilStep("the fail menu again", () => stack.CurrentScreen == gameplay && failMenuShown);
        AddAssert("still failed, not gone to song select", () => gameplay.IsCurrentScreen);

        AddStep("click Results again", () => click("Results"));
        AddUntilStep("the results again", () => stack.CurrentScreen is ScoreScreen);
        AddStep("Retry from there", () => descendants(stack.CurrentScreen!).OfType<MenuShearButton>().First(b => b.Text == "Retry").TriggerClick());
        AddUntilStep("playing again", () => stack.CurrentScreen == gameplay && !failMenuShown && gameplayContainer.Health.Health.Value > 0.5);
    }

    /// <summary>
    /// Retry and Quit still do what they did: Retry starts over at once, without the results.
    /// </summary>
    [Test]
    public void TestRetryFromTheFailMenu()
    {
        fail();

        AddStep("click Retry", () => click("Retry"));
        AddUntilStep("playing again", () => stack.CurrentScreen == gameplay && !failMenuShown);
        AddAssert("no results on the way", () => stack.CurrentScreen is not ScoreScreen);
    }

    private void fail()
    {
        AddStep("play", () =>
        {
            gameLayout.Touch.Value = false;
            TestContent.Add(stack);
            stack.Push(gameplay = new GameplayScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("playing", () => stack.CurrentScreen == gameplay && gameplay.IsLoaded && descendants(gameplay).OfType<GameplayContainer>().Any());

        // On through the song a little each frame without a press, so every note comes and is missed (a seek far ahead
        // would pass over notes not yet shown): far more than health can take.
        AddUntilStep("let the notes go by", () =>
        {
            GameplayContainer container = gameplayContainer;

            if (!container.Health.IsFailed)
                container.GameplayClock.Seek(container.GameplayClock.CurrentTime + 250);

            return container.Health.IsFailed;
        });
        AddUntilStep("the fail menu", () => failMenuShown);
    }

    private GameplayContainer gameplayContainer => descendants(gameplay).OfType<GameplayContainer>().First();

    private void click(string label)
    {
        DialogButton button = descendants(gameplay).OfType<GameplayMenuOverlay>().Where(menu => menu.IsShown)
                                                   .SelectMany(descendants).OfType<DialogButton>()
                                                   .First(b => descendants(b).OfType<SpriteText>().Any(t => t.Text == label));
        InputManager.MoveMouseTo(button);
        InputManager.Click(MouseButton.Left);
    }

    private bool failMenuShown => descendants(gameplay).OfType<GameplayMenuOverlay>().Any(menu =>
        menu.IsShown && descendants(menu).OfType<SpriteText>().Any(t => t.Text == "FAILED"));

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
