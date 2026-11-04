// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestRainbowFill : YuukiTestScene
{
    private RainbowFill chip = null!;
    private RainbowFill bar = null!;
    private RainbowFill medal = null!;
    private RainbowFill badge = null!;
    private RainbowText letters = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the rainbow", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#B4B4C4") });
            Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 32),
                Children = new Drawable[]
                {
                    new FlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(28, 0),
                        Children = new Drawable[]
                        {
                            sheared(new Vector2(130, 32), "BLOOM", 16, out chip),
                            new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(480, 12),
                                Masking = true,
                                CornerRadius = 6,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#393947"), Alpha = 0.12f },
                                    new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Width = 0.68f,
                                        Masking = true,
                                        CornerRadius = 6,
                                        Child = bar = new RainbowFill { RelativeSizeAxes = Axes.Both }
                                    }
                                }
                            },
                            sheared(new Vector2(54, 34), "FB", 17, out medal)
                        }
                    },
                    sheared(new Vector2(300, 58), "FULL BLOOM", 27, out badge),
                    letters = new RainbowText
                    {
                        Text = "SS",
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 200, FontWeight.Bold)
                    },
                    new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Masking = true,
                                Child = new RainbowFill { RelativeSizeAxes = Axes.Both, Speed = 3 }
                            },
                            new RainbowText
                            {
                                Margin = new MarginPadding(8, 40),
                                Text = "FULL BLOOM",
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 96, FontWeight.Bold),
                                Stops = Enumerable.Repeat(Color.White, 2).ToArray()
                            }
                        }
                    }
                }
            });
        });
    }

    [Test]
    public void TestTheRainbowMoves()
    {
        AddSliderStep("speed", 0f, 4f, 1f, speed =>
        {
            foreach (RainbowFill fill in new[] { chip, bar, medal, badge })
                fill.Speed = speed;

            letters.Speed = speed;
        });
        AddSliderStep("bar", 0f, 1f, 0.68f, length => bar.Parent!.Width = length);
    }

    /// <summary>
    /// However far the colors have moved, the pieces cover the box edge to edge, and never go outside it.
    /// </summary>
    [Test]
    public void TestThePiecesFillTheBoxExactly()
    {
        AddWaitStep("let it move", 700);
        AddAssert("the badge's pieces end to end", () => covers(badge));
        AddAssert("the bar's", () => covers(bar));
        AddWaitStep("more", 1300);
        AddAssert("the badge's still", () => covers(badge));
    }

    /// <summary>
    /// The colors come from where in the word they are, and headless text has no width to run across.
    /// </summary>
    [Test]
    [VisualTestOnly("headless text is 0 wide, so there's nowhere for the colors to run")]
    public void TestTheColorsRunAcrossTheWord()
    {
        AddUntilStep("not one color", () => letters.ColorAt!(Vector2.Zero) != letters.ColorAt!(new Vector2(letters.ContentSize.X, 0)));
    }

    private static bool covers(RainbowFill fill)
    {
        Box[] shown = fill.Children.OfType<Box>().Where(b => b.Alpha > 0).OrderBy(b => b.X).ToArray();

        if (shown.Length == 0 || Math.Abs(shown[0].X) > 0.01f || Math.Abs(shown[^1].X + shown[^1].Width - fill.DrawWidth) > 0.01f)
            return false;

        for (int i = 1; i < shown.Length; i++)
        {
            if (Math.Abs(shown[i - 1].X + shown[i - 1].Width - shown[i].X) > 0.01f)
                return false;
        }

        return true;
    }

    /// <summary>
    /// A sheared, rounded tag filled with the rainbow, its text in the ink for it.
    /// </summary>
    private static Container sheared(Vector2 size, string text, int fontSize, out RainbowFill fill) => new Container
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Size = size,
        Children = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                Shear = UserInterfaceConstant.Shear,
                Child = fill = new RainbowFill { RelativeSizeAxes = Axes.Both }
            },
            new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = text,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, fontSize, FontWeight.Bold),
                Color = RainbowColors.INK_ON_PASTEL
            }
        }
    };
}
