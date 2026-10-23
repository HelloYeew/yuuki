// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Drawables;
using Yuuki.Game.Graphics.Screens;

namespace Yuuki.Game.Tests.Visuals.Drawables;

public partial class TestScreenFooter : YuukiTestScene
{
    private ScreenFooter footer = null!;
    private TextBox nameTextbox = null!;
    private TextBox helperTextbox = null!;
    private BasicDropdown<YuukiScreenPart> partDropdown = null!;

    [Resolved]
    private IYuukiColorProvider colorProvider { get; set; } = null!;

    [Cached]
    private readonly GameLayout gameLayout = new GameLayout();

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

    /// <summary>
    /// On a phone the bar slims to its lower strip; by touch it shows no actions (each screen has its own
    /// buttons for them, and there are no keys to hint at).
    /// </summary>
    [Test]
    public void TestPhoneAndTouch()
    {
        AddStep("a Back action", () => footer.ScreenActions.Value = new List<ScreenAction>
        {
            new ScreenAction { Name = "Back", Action = () => { }, KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape)) }
        });
        AddUntilStep("the full bar, the key and its name", () => footer.DrawHeight == 100 && shown("Escape") && shown("Back"));

        AddStep("a phone, by touch", () =>
        {
            gameLayout.Current.Value = DisplayLayout.Phone;
            gameLayout.Touch.Value = true;
        });
        AddUntilStep("the slim bar", () => footer.DrawHeight == 68);
        AddAssert("no actions", () => !shown("Back") && !shown("Escape"));

        AddStep("a new screen's actions, still by touch", () => footer.ScreenActions.Value = new List<ScreenAction>
        {
            new ScreenAction { Name = "Random", Action = () => { }, KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.F2)) }
        });
        AddAssert("none of those either", () => !shown("Random") && !shown("F2"));

        AddStep("back to the desktop", () =>
        {
            gameLayout.Current.Value = DisplayLayout.Desktop;
            gameLayout.Touch.Value = false;
        });
        AddUntilStep("the full bar and the action again", () => footer.DrawHeight == 100 && shown("F2") && shown("Random"));
    }

    // Shown: a text with this label, and nothing above it hidden.
    private bool shown(string text) => texts(footer).Any(t => t.Text == text && visible(t));

    private static bool visible(Drawable drawable)
    {
        for (Drawable? d = drawable; d != null; d = d.Parent)
        {
            if (d.Alpha <= 0)
                return false;
        }

        return true;
    }

    private static IEnumerable<SpriteText> texts(Drawable drawable)
    {
        if (drawable is SpriteText text)
            yield return text;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (var t in texts(child))
                    yield return t;
            }
        }
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
