// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Edit;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// A touch game has no Escape to pause with, so its HUD has a pause button in the top-right corner.
/// </summary>
public partial class TestGameplayPause : YuukiManualInputManagerTestScene
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
    public void TestATapOnThePauseButtonPauses()
    {
        play(touch: true);

        AddStep("tap the corner's button", () => InputManager.Tap(pauseButton.ToScreenSpace(pauseButton.DrawSize / 2)));
        AddUntilStep("paused", () => pauseMenuShown);
    }

    /// <summary>
    /// A finger that slides off the button before lifting changes its mind, as on any button.
    /// </summary>
    [Test]
    public void TestSlidingOffTheButtonDoesNotPause()
    {
        play(touch: true);

        AddStep("press the button, slide off and lift", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, pauseButton.ToScreenSpace(pauseButton.DrawSize / 2));
            InputManager.MoveTouchTo(TouchSource.Touch1, pauseButton.ToScreenSpace(new Vector2(-pauseButton.DrawSize.X, pauseButton.DrawSize.Y * 2)));
            InputManager.TouchUp(TouchSource.Touch1);
        });
        AddWaitStep("a moment", 200);
        AddAssert("still playing", () => !pauseMenuShown);
    }

    /// <summary>
    /// The editor's test play has no pausing; by touch, its corner button goes back to the editor, as Escape does.
    /// </summary>
    [Test]
    public void TestTheEditorsTestPlayGoesBackByTouch()
    {
        EditorTestPlayScreen testPlay = null!;

        AddStep("test play by touch", () =>
        {
            gameLayout.Touch.Value = true;
            TestContent.Add(stack);

            // A note well ahead, so the test doesn't end by itself.
            var chart = new PlayableBeatmap(new[] { new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 60_000, EndTime = 60_000 } }, 4);
            stack.Push(testPlay = new EditorTestPlayScreen(chart, 0, null)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("playing", () => stack.CurrentScreen == testPlay && testPlay.IsLoaded);
        AddStep("tap the corner's button", () =>
        {
            Drawable button = descendants(testPlay).First(d => d.GetType().Name == "PauseButton");
            InputManager.Tap(button.ToScreenSpace(button.DrawSize / 2));
        });
        AddUntilStep("back", () => !testPlay.IsCurrentScreen);
    }

    [Test]
    public void TestWithKeysThereIsNoButton()
    {
        play(touch: false);

        AddAssert("no button", () => pauseButton.Alpha == 0);
    }

    private void play(bool touch)
    {
        AddStep(touch ? "play by touch" : "play with keys", () =>
        {
            gameLayout.Touch.Value = touch;
            TestContent.Add(stack);
            stack.Push(gameplay = new GameplayScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("playing", () => stack.CurrentScreen == gameplay && gameplay.IsLoaded && pauseButtonOrNull != null);
    }

    private Drawable? pauseButtonOrNull => descendants(gameplay).FirstOrDefault(d => d.GetType().Name == "PauseButton");

    private Drawable pauseButton => pauseButtonOrNull!;

    private bool pauseMenuShown => descendants(gameplay).OfType<GameplayMenuOverlay>().Any(menu =>
        menu.IsShown && descendants(menu).OfType<SpriteText>().Any(t => t.Text == "PAUSED"));

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
