// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Drawables;

namespace Yuuki.Game.Tests.Visuals.Drawables;

public partial class TestScreenFooter : YuukiTestScene
{
    private ScreenFooter footer = null!;
    private TextBox nameTextbox = null!;
    private TextBox helperTextbox = null!;
    private BasicDropdown<YuukiScreenPart> partDropdown = null!;

    [Resolved]
    private IYuukiColorProvider colorProvider { get; set; } = null!;

    [SetUp]
    public void Setup()
    {
        AddStep("Add footer", () =>
        {
            Add(footer = new ScreenFooter());
            footer.ColorPalette.Value = colorProvider.GetPalette(YuukiScreenPart.MainMenu);
            Add(new FlowContainer()
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(0, 10),
                Direction = FlowDirection.Vertical,
                Margin = new MarginPadding(10),
                Children = new Drawable[]
                {
                    nameTextbox = new BasicTextBox()
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        PlaceholderText = "Name...",
                    },
                    helperTextbox = new BasicTextBox()
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        PlaceholderText = "HelperText..."
                    }
                }
            });
            nameTextbox.Text.Value = footer.ScreenName.Value;
            helperTextbox.Text.Value = footer.ScreenDescription.Value;
            nameTextbox.Text.ValueChanged += e => footer.ScreenName.Value = e.NewValue;
            helperTextbox.Text.ValueChanged += e => footer.ScreenDescription.Value = e.NewValue;
            Add(partDropdown = new BasicDropdown<YuukiScreenPart>()
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Width = 200,
                Margin = new MarginPadding(10),
                Items = Enum.GetValues<YuukiScreenPart>().ToList(),
                Current =
                {
                    Value = YuukiScreenPart.MainMenu
                }
            });
            partDropdown.Current.ValueChanged += e =>
            {
                footer.ColorPalette.Value = colorProvider.GetPalette(e.NewValue);
            };
        });
    }

    [Test]
    public void TestFooter()
    {

    }

    [Test]
    public void TestFooterTextChange()
    {
        AddStep("Change footer text", () =>
        {
            nameTextbox.Text.Value = "Awesome Screen";
            helperTextbox.Text.Value = "Why are you here?";
        });
        AddAssert("Footer text changed", () => footer.ScreenName == "Awesome Screen" && footer.ScreenDescription == "Why are you here?");
    }

    [Test]
    public void TestLongName()
    {
        AddStep("Change footer text to long name", () => nameTextbox.Text.Value = "This is a very longgggggggggggggggggggg screen name more text here blablabla i dunno what to add here but Waguri is cute");
    }

    [Test]
    public void TestLongDescription()
    {
        AddStep("Change footer text to long description", () => helperTextbox.Text.Value = "This is a very longgggggggggggggggggggg screen description more text here blablabla i dunno what to add here but Waguri is cute. More long");
    }
}
