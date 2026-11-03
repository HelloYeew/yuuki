// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public class TestMenuShearButton : YuukiManualInputManagerTestScene
{
    private MenuShearButton button = null!;

    [SetUp]
    public void Setup()
    {
        AddStep("add button", () =>
        {
            TestContent.Add(button = new MenuShearButton()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                // Without an action it would be a switched-off button, which doesn't flash.
                Action = () => { }
            });
        });
    }

    [Test]
    public void TestButton()
    {

    }

    [Test]
    public void TestHoverAndClick()
    {
        AddStep("move mouse to button", () => InputManager.MoveMouseTo(button));
        AddStep("click", () => InputManager.Click(MouseButton.Left));
    }

    /// <summary>
    /// Every fill the game gives these buttons: the main menu's, Play's, Exit's and the results' (Replay and Retry on
    /// the play palette's surface, a failed run's Retry on its accent). Each label reads on its fill at rest, 4.5:1 or
    /// more; half the fill's brightness, as it was, gave 2.5–3.5:1.
    /// </summary>
    [Test]
    public void TestLabelsReadOnEveryFill()
    {
        var buttons = new List<MenuShearButton>();

        AddStep("a button in each fill", () =>
        {
            button.Expire();
            buttons.Clear();

            YuukiColorPalette play = DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);
            var fills = new (string Name, Color Fill)[]
            {
                ("Play", play.Accent),
                ("Edit", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Edit).Accent),
                ("List", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.List).Accent),
                ("More", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.More).Accent),
                ("Exit", UserInterfaceConstant.DangerColor),
                ("Replay", play.Surface),
                ("Multi", play.TextLight),
                ("Versus", play.TextMuted)
            };

            TestContent.Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                // Two rows of four.
                Width = 1400,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(60, 24),
                Children = fills.Select(f =>
                {
                    var b = new MenuShearButton
                    {
                        ButtonSize = new Vector2(290, 110),
                        Text = f.Name,
                        BackgroundColor = f.Fill,
                        Action = () => { }
                    };
                    buttons.Add(b);
                    return (Drawable)b;
                }).ToArray()
            });
        });
        AddUntilStep("shown", () => buttons.All(b => b.IsLoaded));
        AddAssert("every label reads on its fill", () => buttons.All(b =>
            ColorContrast.Ratio(label(b).Color, b.BackgroundColor) >= ColorContrast.TEXT));
        AddAssert("in the fill's own hue, as before", () => buttons.All(b => label(b).Color == b.LabelColor));
    }

    // Its text (an icon is a sprite text too).
    private static SpriteText label(Drawable drawable) => descendants(drawable).OfType<SpriteText>().Single(t => t is not IconSprite);

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
