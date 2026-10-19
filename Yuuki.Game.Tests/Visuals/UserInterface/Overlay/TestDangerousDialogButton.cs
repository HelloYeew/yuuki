// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Dialog;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestDangerousDialogButton : YuukiManualInputManagerTestScene
{
    private readonly UISoundPlayer sounds = new UISoundPlayer();
    private readonly List<UISound> played = new List<UISound>();

    private DialogOverlay overlay = null!;
    private int confirmed;
    private int cancelled;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(sounds);
        Add(sounds);
        sounds.Played += (sound, _) => played.Add(sound);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("add overlay", () => TestContent.Add(overlay = new DialogOverlay()));
        AddUntilStep("loaded", () => overlay.IsLoaded);
        AddStep("ask to exit mid-import", () =>
        {
            confirmed = cancelled = 0;
            sounds.ForgetRecent();
            played.Clear();
            overlay.Push(new ConfirmExitDialog(() => confirmed++, () => cancelled++, new[] { "Importing 2 beatmaps" }));
        });
        AddUntilStep("its buttons are up", () => buttons().Count == 2 && buttons().All(b => b.IsLoaded));
    }

    private List<DialogButton> buttons() => descendants(overlay).OfType<DialogButton>().ToList();

    private DialogButton letMeOut => buttons()[0];

    [Test]
    public void TestOnlyThePlainExitIsSafe()
    {
        AddAssert("leaving is the dangerous answer", () => overlay.CurrentDialog!.Answers[0].IsDangerous);
        AddAssert("without tasks it isn't", () => !new ConfirmExitDialog(() => { }).Answers.Any(a => a.IsDangerous));
    }

    [Test]
    public void TestAClickDoesNotTakeIt()
    {
        AddStep("click it", () =>
        {
            InputManager.MoveMouseTo(letMeOut);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("not taken", () => confirmed == 0 && overlay.CurrentDialog != null);
    }

    [Test]
    public void TestHoldingTakesIt()
    {
        AddStep("hold it", () =>
        {
            InputManager.MoveMouseTo(letMeOut);
            InputManager.PressButton(MouseButton.Left);
        });
        AddUntilStep("filling", () => letMeOut.HoldProgress > 0.3f);
        AddAssert("not yet", () => confirmed == 0);
        AddAssert("ticking", () => played.Contains(UISound.DialogDangerousTick));
        AddUntilStep("taken", () => confirmed == 1);
        AddStep("let go", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("with the heavier sound", () => played.Contains(UISound.DialogDangerousSelect));
        AddAssert("once, and put away", () => confirmed == 1 && overlay.CurrentDialog == null);
    }

    [Test]
    public void TestLettingGoEarlyDrainsIt()
    {
        AddStep("hold it", () =>
        {
            InputManager.MoveMouseTo(letMeOut);
            InputManager.PressButton(MouseButton.Left);
        });
        AddUntilStep("partly filled", () => letMeOut.HoldProgress > 0.2f);
        AddStep("let go", () => InputManager.ReleaseButton(MouseButton.Left));
        AddUntilStep("drained", () => letMeOut.HoldProgress == 0);
        AddAssert("not taken", () => confirmed == 0 && overlay.CurrentDialog != null);
    }

    [Test]
    public void TestSlidingOffStopsIt()
    {
        AddStep("hold it", () =>
        {
            InputManager.MoveMouseTo(letMeOut);
            InputManager.PressButton(MouseButton.Left);
        });
        AddUntilStep("partly filled", () => letMeOut.HoldProgress > 0.2f);
        AddStep("slide off onto the dialog's icon", () =>
        {
            var dialog = overlay.CurrentDialog!;
            InputManager.MoveMouseTo(dialog.ToScreenSpace(new Vector2(dialog.DrawWidth / 2, 20)));
        });
        AddUntilStep("drained", () => letMeOut.HoldProgress == 0);
        AddStep("let go", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("not taken", () => confirmed == 0 && cancelled == 0 && overlay.CurrentDialog != null);
    }

    [Test]
    public void TestATappedKeyDoesNotTakeIt()
    {
        double tappedAt = 0;

        AddStep("tap enter", () =>
        {
            InputManager.PressKey(Key.Enter);
            InputManager.ReleaseKey(Key.Enter);
        });
        AddStep("tap 1", () =>
        {
            InputManager.PressKey(Key.Number1);
            InputManager.ReleaseKey(Key.Number1);
            tappedAt = Clock.CurrentTime;
        });
        AddUntilStep("longer than a hold goes by", () => Clock.CurrentTime > tappedAt + DialogButton.HOLD_DURATION * 2);
        AddAssert("not taken", () => confirmed == 0 && overlay.CurrentDialog != null);
    }

    [Test]
    public void TestHoldingEnterTakesIt()
    {
        AddStep("hold enter", () => InputManager.PressKey(Key.Enter));
        AddUntilStep("filling", () => letMeOut.HoldProgress > 0.3f);
        AddAssert("not yet", () => confirmed == 0);
        AddAssert("ticking", () => played.Contains(UISound.DialogDangerousTick));
        AddUntilStep("taken", () => confirmed == 1);
        AddStep("let go", () => InputManager.ReleaseKey(Key.Enter));
        AddAssert("with the heavier sound", () => played.Contains(UISound.DialogDangerousSelect));
        AddAssert("once, and put away", () => confirmed == 1 && overlay.CurrentDialog == null);
    }

    [Test]
    public void TestHoldingItsNumberTakesIt()
    {
        AddStep("hold 1", () => InputManager.PressKey(Key.Number1));
        AddUntilStep("taken", () => confirmed == 1);
        AddStep("let go", () => InputManager.ReleaseKey(Key.Number1));
    }

    [Test]
    public void TestLettingGoOfTheKeyEarlyDrainsIt()
    {
        AddStep("hold enter", () => InputManager.PressKey(Key.Enter));
        AddUntilStep("partly filled", () => letMeOut.HoldProgress > 0.2f);
        AddStep("let go", () => InputManager.ReleaseKey(Key.Enter));
        AddUntilStep("drained", () => letMeOut.HoldProgress == 0);
        AddAssert("not taken", () => confirmed == 0 && overlay.CurrentDialog != null);
    }

    [Test]
    public void TestMovingTheSelectionOffStopsIt()
    {
        AddStep("hold enter", () => InputManager.PressKey(Key.Enter));
        AddUntilStep("partly filled", () => letMeOut.HoldProgress > 0.2f);
        AddStep("move down to stay", () =>
        {
            InputManager.PressKey(Key.Down);
            InputManager.ReleaseKey(Key.Down);
        });
        AddUntilStep("drained", () => letMeOut.HoldProgress == 0);
        AddStep("let go", () => InputManager.ReleaseKey(Key.Enter));
        AddAssert("nothing taken", () => confirmed == 0 && cancelled == 0 && overlay.CurrentDialog != null);
    }

    [Test]
    public void TestNeverTakenForYou()
    {
        AddStep("perform ok", () => overlay.CurrentDialog!.PerformOkAction());
        AddAssert("not taken", () => confirmed == 0 && overlay.CurrentDialog != null);
    }

    private static IEnumerable<Drawable> descendants(Drawable drawable)
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
}
