// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// Test for <see cref="HeroColorCycle"/>
/// </summary>
public partial class TestHeroColorCycle : YuukiTestScene
{
    private const int tile_count = 12;

    private HeroColorCycle cycle = null!;
    private Box[] tiles = null!;
    private Circle disc = null!;
    private SpriteText text = null!;
    private float hueStep = 30;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the cycle", () =>
        {
            Clear();

            tiles = Enumerable.Range(0, tile_count).Select(i => new Box { Size = new Vector2(64), X = i * 72 }).ToArray();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(new FlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 28),
                Children = new Drawable[]
                {
                    label("The heroes, as on the sheet: Yuuna, Fuu, Itsuki, Mimori, Sonoko, Karin"),
                    new FlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = HeroColors.CYCLE.Select(c => (Drawable)new Box { Size = new Vector2(136, 40), Color = c }).ToArray()
                    },
                    label("Moving, each tile further round"),
                    new Container { AutoSizeAxes = Axes.Both, Children = tiles },
                    new FlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(40, 0),
                        Children = new Drawable[]
                        {
                            disc = new Circle { Size = new Vector2(160) },
                            text = new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = "FULL BLOOM",
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 96, FontWeight.Bold)
                            }
                        }
                    }
                }
            });

            Add(cycle = new HeroColorCycle());
            addTargets();
        });
    }

    [Test]
    public void TestColorsMove()
    {
        AddSliderStep("speed", 0f, 4f, 1f, speed => cycle.Speed = speed);
        AddSliderStep("white", 0f, 1f, HeroColors.CYCLE_WHITE, white => cycle.White = white);
        AddSliderStep("tiles apart (degrees)", 0f, 120f, 30f, step =>
        {
            hueStep = step;
            addTargets();
        });
        AddStep("stop", () => cycle.Running = false);
        AddStep("go on", () => cycle.Running = true);
    }

    [Test]
    public void TestTheTilesRunThroughTheHeroes()
    {
        AddStep("hold still at the start", () =>
        {
            cycle.Speed = 0;
            cycle.White = 0;
        });
        AddUntilStep("the first tile is Yuuna's", () => tiles[0].Color == HeroColors.YUUNA.Main);
        AddAssert("two tiles on (60°) is Fuu's", () => tiles[2].Color == HeroColors.FUU.Main);
        AddAssert("the disc and text move with them", () => disc.Color == tiles[0].Color && text.Color == tiles[0].Color);
        AddStep("move again", () => cycle.Speed = 1);
        AddUntilStep("the first tile has moved on", () => tiles[0].Color != HeroColors.YUUNA.Main);
    }

    private void addTargets()
    {
        if (cycle.IsNull())
            return;

        foreach (Box tile in tiles)
            cycle.Remove(tile);

        cycle.Remove(disc);
        cycle.Remove(text);

        for (int i = 0; i < tiles.Length; i++)
            cycle.Add(tiles[i], i * hueStep);

        cycle.Add(disc);
        cycle.Add(text);
    }

    private static SpriteText label(string text) => new SpriteText
    {
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.Bold),
        Color = Color.White
    };
}
