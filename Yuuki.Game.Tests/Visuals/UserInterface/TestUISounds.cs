// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class TestUISounds : YuukiManualInputManagerTestScene
{
    private readonly UISoundPlayer sounds = new UISoundPlayer();
    private readonly List<UISound> played = new List<UISound>();

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(sounds);
        Add(sounds);
        sounds.Played += (sound, _) => played.Add(sound);
    }

    // The player is shared across tests (each with its own clock) and debounces repeats: start each from silence.
    private void reset() => AddStep("forget earlier sounds", () =>
    {
        sounds.ForgetRecent();
        played.Clear();
    });

    private void hover(System.Func<Drawable> target) => AddStep("hover", () => InputManager.MoveMouseTo(target()));

    private void moveAway() => AddStep("move away", () => InputManager.MoveMouseTo(Vector2.Zero));

    private void press() => AddStep("press", () =>
    {
        InputManager.PressButton(MouseButton.Left);
        InputManager.ReleaseButton(MouseButton.Left);
    });

    [Test]
    public void TestSoundsPassThroughToTheControl()
    {
        ClickableContainer button = null!;
        int clicks = 0;
        bool hovered = false;

        AddStep("add a button with sounds", () =>
        {
            clicks = 0;
            TestContent.Add(button = new HoverProbe(() => hovered = true)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(200, 80),
                Action = () => clicks++,
                Child = new HoverClickSounds()
            });
        });
        reset();

        hover(() => button);
        AddAssert("hover sound, and the button still hovered", () => played.Contains(UISound.Hover) && hovered);
        press();
        AddAssert("click sound, and the button still clicked", () => played.Contains(UISound.Click) && clicks == 1);
        moveAway();
        AddAssert("un-hover sound", () => played.Contains(UISound.HoverLost));
    }

    [Test]
    public void TestUnusableControlIsDenied()
    {
        ClickableContainer button = null!;

        AddStep("add a locked button", () => TestContent.Add(button = new ClickableContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(200, 80),
            Child = new HoverClickSounds { Enabled = () => false }
        }));
        reset();

        hover(() => button);
        press();
        AddAssert("silent hover, denied press", () => !played.Contains(UISound.Hover) && played.Contains(UISound.Denied) && !played.Contains(UISound.Click));
    }

    [Test]
    public void TestToggleSwitch()
    {
        YuukiCheckbox toggle = null!;

        AddStep("add a toggle", () => TestContent.Add(toggle = new YuukiCheckbox { Anchor = Anchor.Centre, Origin = Anchor.Centre }));
        AddUntilStep("loaded", () => toggle.IsLoaded);
        AddAssert("its first state made no sound", () => !played.Contains(UISound.ToggleOn) && !played.Contains(UISound.ToggleOff));
        reset();

        hover(() => toggle);
        press();
        AddAssert("on", () => played.Contains(UISound.ToggleOn));
        AddWaitStep("past the debounce", 100);
        press();
        AddAssert("off", () => played.Contains(UISound.ToggleOff));
    }

    [Test]
    public void TestDropdown()
    {
        YuukiDropdown<int> dropdown = null!;

        AddStep("add a dropdown", () => TestContent.Add(dropdown = new YuukiDropdown<int>
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Y = 100,
            Items = new[] { 1, 2, 3 }
        }));
        reset();

        AddStep("open it", () => dropdown.OpenMenu());
        AddAssert("opening sound", () => played.Contains(UISound.DropdownOpen));
        AddStep("pick with the keyboard", () =>
        {
            InputManager.PressKey(Key.Down);
            InputManager.ReleaseKey(Key.Down);
            InputManager.PressKey(Key.Enter);
            InputManager.ReleaseKey(Key.Enter);
        });
        AddAssert("moving ticks, picking selects, then it closes", () =>
            played.Contains(UISound.Hover) && played.Contains(UISound.Select) && played.Contains(UISound.DropdownClose));
    }

    [Test]
    public void TestSliderNotches()
    {
        YuukiSliderBar<double> slider = null!;

        AddStep("add a slider", () => TestContent.Add(slider = new YuukiSliderBar<double>(Sakura.Framework.Graphics.Colors.Color.White, Sakura.Framework.Graphics.Colors.Color.Gray)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.None,
            Width = 400,
            MinValue = 0,
            MaxValue = 1,
            KeyboardStep = 0.1
        }));
        AddUntilStep("loaded", () => slider.IsLoaded);
        reset();

        AddStep("move it", () => slider.Current.Value = 0.5);
        AddAssert("a notch", () => played.Contains(UISound.SliderTick));
    }

    [Test]
    public void TestBackAndLockedButtons()
    {
        LeftBottomShearButton back = null!;
        LeftBottomShearButton locked = null!;

        AddStep("add buttons", () =>
        {
            TestContent.Add(back = new LeftBottomShearButton { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, X = 100, Action = () => { } });
            TestContent.Add(locked = new LeftBottomShearButton { Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, X = -100, Locked = true, Text = "Replay" });
        });
        AddUntilStep("loaded", () => back.IsLoaded && locked.IsLoaded);
        reset();

        hover(() => back);
        press();
        AddAssert("back sounds like going back", () => played.Contains(UISound.Back));
        hover(() => locked);
        press();
        AddAssert("locked says no", () => played.Contains(UISound.Denied));
    }

    [Test]
    public void TestScreenTransitions()
    {
        Yuuki.Game.Graphics.ScreenStacks.YuukiScreenStack stack = null!;
        Yuuki.Game.Graphics.Screens.YuukiScreen second = null!;

        AddStep("add a screen stack", () =>
        {
            TestContent.Add(stack = new Yuuki.Game.Graphics.ScreenStacks.YuukiScreenStack { RelativeSizeAxes = Axes.Both });
            stack.Push(new Yuuki.Game.Graphics.Screens.YuukiScreen { RelativeSizeAxes = Axes.Both });
        });
        AddAssert("the first screen is quiet", () => !played.Contains(UISound.ScreenForward));
        reset();

        AddStep("go forward", () => stack.Push(second = new Yuuki.Game.Graphics.Screens.YuukiScreen { RelativeSizeAxes = Axes.Both }));
        AddAssert("forward whoosh", () => played.Contains(UISound.ScreenForward));
        AddStep("go back", () => second.Exit());
        AddAssert("back whoosh", () => played.Contains(UISound.ScreenBack));
    }

    private partial class HoverProbe : ClickableContainer
    {
        private readonly System.Action onHover;

        public HoverProbe(System.Action onHover)
        {
            this.onHover = onHover;
        }

        public override bool OnHover(MouseEvent e)
        {
            onHover();
            return base.OnHover(e);
        }
    }
}
