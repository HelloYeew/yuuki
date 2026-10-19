// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// The game's styled controls built on the framework's
/// </summary>
public partial class TestYuukiControls : TestScene
{
    private YuukiDropdown<WindowMode> dropdown = null!;
    private YuukiCheckbox checkbox = null!;
    private YuukiSliderBar<double> slider = null!;
    private YuukiTextBox textBox = null!;

    [SetUp]
    public void SetUp() => AddStep("add controls", () =>
    {
        Clear();
        Add(new FlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 24),
            Children = new Sakura.Framework.Graphics.Drawables.Drawable[]
            {
                dropdown = new YuukiDropdown<WindowMode> { Items = System.Enum.GetValues<WindowMode>() },
                checkbox = new YuukiCheckbox(),
                textBox = new YuukiTextBox { PlaceholderText = "Automatic" },
                slider = new YuukiSliderBar<double>(Sakura.Framework.Graphics.Colors.Color.Purple, Sakura.Framework.Graphics.Colors.Color.Gray)
                {
                    RelativeSizeAxes = Axes.None,
                    Width = 300,
                    MinValue = 0,
                    MaxValue = 1
                }
            }
        });
    });

    [Test]
    public void TestDropdownPicksAndCloses()
    {
        AddStep("open", () => dropdown.OpenMenu());
        AddAssert("open", () => dropdown.IsMenuOpen);
        AddStep("pick the last", () => dropdown.Current.Value = System.Enum.GetValues<WindowMode>()[^1]);
        AddStep("close", () => dropdown.CloseMenu());
        AddAssert("closed", () => !dropdown.IsMenuOpen);
        AddAssert("value kept", () => dropdown.Current.Value == System.Enum.GetValues<WindowMode>()[^1]);
    }

    [Test]
    public void TestCheckboxToggles()
    {
        AddAssert("starts off", () => !checkbox.Current.Value);
        AddStep("flip", () => checkbox.Current.Value = !checkbox.Current.Value);
        AddAssert("on", () => checkbox.Current.Value);
    }

    [Test]
    public void TestDoubleSlider()
    {
        AddStep("set 0.4", () => slider.Current.Value = 0.4);
        AddAssert("holds 0.4", () => System.Math.Abs(slider.Current.Value - 0.4) < 1e-9);
    }

    [Test]
    public void TestTextBoxHoldsText()
    {
        AddAssert("placeholder showing", () => textBox.IsPlaceholderVisible);
        AddStep("type", () => textBox.Text.Value = "1280");
        AddAssert("holds it", () => textBox.Text.Value == "1280");
        AddAssert("placeholder hidden", () => !textBox.IsPlaceholderVisible);
    }

    [Test]
    public void TestFormatSplitsWords()
    {
        AddAssert("pascal case split", () => YuukiDropdown<WindowMode>.DefaultFormat(default) != null);
        AddAssert("FullScreen", () => YuukiDropdown<string>.DefaultFormat("FullScreen") == "Full Screen");
        AddAssert("Limit2x", () => YuukiDropdown<string>.DefaultFormat("Limit2x") == "Limit 2x");
    }
}
