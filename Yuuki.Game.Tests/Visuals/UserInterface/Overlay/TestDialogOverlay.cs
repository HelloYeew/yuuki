// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Dialog;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestDialogOverlay : YuukiManualInputManagerTestScene
{
    private DialogOverlay overlay = null!;

    private int confirmed;
    private int cancelled;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("add overlay", () => TestContent.Add(overlay = new DialogOverlay()));
        AddUntilStep("loaded", () => overlay.IsLoaded);
        AddStep("ask to exit", () =>
        {
            confirmed = cancelled = 0;
            overlay.Push(new ConfirmExitDialog(() => confirmed++, () => cancelled++));
        });
        AddAssert("shown", () => overlay.State == Visibility.Visible);
    }

    [Test]
    public void TestEnterTakesTheFirstAnswer()
    {
        press(Key.Enter);
        AddAssert("confirmed once", () => confirmed == 1 && cancelled == 0);
        assertPutAway();
    }

    [Test]
    public void TestEscapeCancels()
    {
        press(Key.Escape);
        AddAssert("cancelled once", () => cancelled == 1 && confirmed == 0);
        assertPutAway();
    }

    [Test]
    public void TestNumberKeyTakesThatAnswer()
    {
        press(Key.Number2);
        AddAssert("the second answer, cancel", () => cancelled == 1 && confirmed == 0);
        assertPutAway();
    }

    [Test]
    public void TestDownThenEnter()
    {
        press(Key.Down);
        press(Key.Enter);
        AddAssert("cancelled", () => cancelled == 1 && confirmed == 0);
    }

    [Test]
    public void TestAnsweredOnlyOnce()
    {
        press(Key.Enter);
        press(Key.Enter);
        press(Key.Escape);
        AddAssert("just the one answer", () => confirmed == 1 && cancelled == 0);
    }

    [Test]
    public void TestOkActionTakesTheConfirm()
    {
        AddStep("perform ok", () => overlay.CurrentDialog!.PerformOkAction());
        AddAssert("confirmed", () => confirmed == 1 && cancelled == 0);
        assertPutAway();
    }

    [Test]
    public void TestPushReplacesTheDialog()
    {
        ConfirmExitDialog? second = null;

        AddStep("ask again", () => overlay.Push(second = new ConfirmExitDialog(() => confirmed += 10)));
        AddAssert("the new one is up", () => overlay.CurrentDialog == second);
        press(Key.Enter);
        AddAssert("the new one answered", () => confirmed == 10 && cancelled == 0);
        assertPutAway();
    }

    [Test]
    public void TestClickOutsideCancels()
    {
        AddStep("click the dim", () =>
        {
            InputManager.MoveMouseTo(overlay.ToScreenSpace(new Vector2(20, 20)));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("cancelled once", () => cancelled == 1 && confirmed == 0);
        assertPutAway();
    }

    [Test]
    public void TestClickOnTheDialogItselfDoesNothing()
    {
        AddStep("click the dialog's icon", () =>
        {
            var dialog = overlay.CurrentDialog!;
            InputManager.MoveMouseTo(dialog.ToScreenSpace(new Vector2(dialog.DrawWidth / 2, 20)));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("not answered", () => confirmed == 0 && cancelled == 0);
        AddAssert("still up", () => overlay.CurrentDialog != null && overlay.State == Visibility.Visible);
    }

    [Test]
    public void TestLongAnswersFitTheirButtons()
    {
        AddStep("ask a wordy question", () => overlay.Push(new Yuuki.Game.Edit.CreateNewDifficultyDialog(_ => { })));
        AddUntilStep("laid out", () => answerButtons().Count == 3 && answerButtons().All(b => b.DrawWidth > 0));
        AddAssert("one width for all", () => answerButtons().Select(b => b.DrawWidth).Distinct().Count() == 1);
        AddAssert("every label inside", () => answerButtons().All(b => b.NaturalWidth <= b.DrawWidth + 0.5f));
        AddAssert("the header on the window", () =>
        {
            var header = descendants(overlay.CurrentDialog!).OfType<SpriteText>().First(t => t.Text.StartsWith("Would you like"));
            return header.DrawWidth * header.Scale.X <= overlay.DrawWidth;
        });
    }

    /// <summary>
    /// The answers are drawn in the main menu's way: each in the color of what it does, its label a darker
    /// shade of that color, never white. The selected one is its color at full strength (not lightened,
    /// which washed the beige out toward white); the others are muted.
    /// </summary>
    [Test]
    public void TestAnswersInTheirOwnColors()
    {
        var palette = DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.MainMenu);

        AddUntilStep("laid out", () => answerButtons().Count == 2);
        AddUntilStep("the selected one's label isn't white", () => label(answerButtons()[0]).Color.ToArgb() != Color.White.ToArgb());
        AddUntilStep("the exit answer in the menu's color", () => label(answerButtons()[0]).Color.ToArgb() == palette.Accent.Darken(0.5f).ToArgb());
        AddUntilStep("the back answer in its light base", () => label(answerButtons()[1]).Color.ToArgb() == palette.Surface.Darken(0.5f).ToArgb());

        AddUntilStep("the selected one at full strength, the other muted", () => fill(answerButtons()[0]).Alpha == 1 && fill(answerButtons()[1]).Alpha < 0.7f);

        press(Key.Down);
        AddUntilStep("selecting keeps the label's shade", () => label(answerButtons()[1]).Color.ToArgb() == palette.Surface.Darken(0.5f).ToArgb());
        AddUntilStep("and the fill its own color, not lightened", () => fill(answerButtons()[1]).Alpha == 1 && fill(answerButtons()[1]).Color.ToArgb() == palette.Surface.ToArgb());
    }

    private static Box fill(DialogButton button) => descendants(button).OfType<Box>().First();

    private static SpriteText label(DialogButton button) => descendants(button).OfType<SpriteText>().First(t => t is not IconSprite);

    private System.Collections.Generic.List<DialogButton> answerButtons() =>
        descendants(overlay.CurrentDialog!).OfType<DialogButton>().ToList();

    private static System.Collections.Generic.IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is not Container container)
            yield break;

        foreach (var child in container.Children)
        {
            foreach (var descendant in descendants(child))
                yield return descendant;
        }
    }

    private void press(Key key) => AddStep($"press {key}", () =>
    {
        InputManager.PressKey(key);
        InputManager.ReleaseKey(key);
    });

    private void assertPutAway()
    {
        AddAssert("no dialog", () => overlay.CurrentDialog == null);
        AddAssert("overlay hidden", () => overlay.State == Visibility.Hidden);
    }
}
