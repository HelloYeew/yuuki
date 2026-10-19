// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestShearButtonFlash : YuukiManualInputManagerTestScene
{
    private TestNextButton next = null!;
    private TestMenuButton locked = null!;
    private int pressed;

    [SetUp]
    public void SetUp()
    {
        AddStep("add buttons", () =>
        {
            pressed = 0;

            TestContent.Add(next = new TestNextButton
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                X = -200,
                BackgroundColor = Color.Black,
                Action = () => pressed++
            });
            TestContent.Add(locked = new TestMenuButton
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                X = 200,
                BackgroundColor = Color.Black,
                Locked = true,
                Action = () => pressed++
            });
        });
        AddUntilStep("loaded", () => next.IsLoaded && locked.IsLoaded);
    }

    [Test]
    public void TestUsableButtonFlashes()
    {
        AddStep("flash it", () => flashAndCheck(next, () => next.Background, flashes: true));
    }

    [Test]
    public void TestSwitchedOffButtonDoesNotFlash()
    {
        AddStep("switch it off", () => next.Enabled.Value = false);

        AddStep("flash it", () => flashAndCheck(next, () => next.Background, flashes: false));

        AddStep("click it", () =>
        {
            InputManager.MoveMouseTo(next);
            InputManager.Click(MouseButton.Left);
            Assert.That(next.Background, Is.Not.EqualTo(Color.White), "a click shouldn't have flashed it");
        });
        AddAssert("not pressed", () => pressed == 0);

        AddStep("switch it back on", () => next.Enabled.Value = true);
        AddStep("flash it", () => flashAndCheck(next, () => next.Background, flashes: true));
    }

    [Test]
    public void TestLockedButtonDoesNotFlash()
    {
        AddStep("flash it", () => flashAndCheck(locked, () => locked.Background, flashes: false));
    }

    /// <summary>
    /// Checked in the same step: a flash sets the colour at once, then starts fading it back.
    /// </summary>
    private static void flashAndCheck(ShearButton button, Func<Color> background, bool flashes)
    {
        button.Flash(Color.White);
        Assert.That(background() == Color.White, Is.EqualTo(flashes), flashes ? "should have flashed" : "shouldn't have flashed");
    }

    private partial class TestNextButton : RightBottomShearButton
    {
        public Color Background => BackgroundBox.Color;
    }

    private partial class TestMenuButton : MenuShearButton
    {
        public Color Background => BackgroundBox.Color;
    }
}
