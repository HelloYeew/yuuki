// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// The playfield keeps one shape on every window: its canvas is scaled uniformly to the largest that
/// fits, so a wider window only adds room at the sides and a narrower one shrinks it whole. The size
/// and position settings then shrink it and place it in the room left. Each case puts the field in a
/// "window" of a given size and measures where it lands.
/// </summary>
public partial class TestPlayfieldLayout : TestScene
{
    private static readonly float base_aspect = PlayfieldAdjustmentContainer.BASE_SIZE.X / PlayfieldAdjustmentContainer.BASE_SIZE.Y;

    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private Container window = null!;
    private Box area = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
    }

    private void build(float width, float height, double size = 1, double x = 0.5, double y = 0.5) => AddStep($"{width}×{height} window, size {size}, at ({x}, {y})", () =>
    {
        config.Get<double>(YuukiSetting.PlayfieldSize).Value = size;
        config.Get<double>(YuukiSetting.PlayfieldPositionX).Value = x;
        config.Get<double>(YuukiSetting.PlayfieldPositionY).Value = y;

        Clear();
        Add(window = new Container
        {
            Size = new Vector2(width, height),
            Child = new PlayfieldAdjustmentContainer
            {
                // Stands in for the playfield: it fills the canvas exactly as the playfield does.
                Child = area = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.White,
                    Alpha = 0.5f
                }
            }
        });
    });

    private void laidOut() => AddUntilStep("laid out", () => area.DrawRectangle.Width > 0 && window.DrawRectangle.Width > 0);

    private float areaAspect => area.DrawRectangle.Width / area.DrawRectangle.Height;

    private static float right(RectangleF r) => r.X + r.Width;

    private static float bottom(RectangleF r) => r.Y + r.Height;

    private static bool near(float a, float b) => Math.Abs(a - b) <= Math.Max(Math.Abs(b), 1) * 0.005f;

    [TestCase(1440, 1080)] // 4:3
    [TestCase(1920, 1080)] // 16:9
    [TestCase(2560, 1080)] // 21:9
    [TestCase(3840, 1080)] // 32:9
    [TestCase(1080, 1920)] // portrait
    public void TestSameShapeOnEveryWindow(int width, int height)
    {
        build(width, height);
        laidOut();
        AddAssert("the field keeps its shape", () => near(areaAspect, base_aspect));
    }

    [TestCase(1440, 1080)]
    [TestCase(1920, 1080)]
    [TestCase(3840, 1080)]
    public void TestLandscapeFillsTheHeight(int width, int height)
    {
        build(width, height);
        laidOut();
        AddAssert("as tall as the window", () => near(area.DrawRectangle.Height, window.DrawRectangle.Height));
        AddAssert("centred across it", () => near(area.DrawRectangle.Center.X, window.DrawRectangle.Center.X));
    }

    [Test]
    public void TestPortraitFitsTheWidth()
    {
        build(1080, 1920);
        laidOut();
        AddAssert("as wide as the window", () => near(area.DrawRectangle.Width, window.DrawRectangle.Width));
        AddAssert("not past its edges", () => area.DrawRectangle.Height < window.DrawRectangle.Height);
    }

    [Test]
    public void TestWiderWindowOnlyAddsRoom()
    {
        float at169 = 0;

        build(1920, 1080);
        laidOut();
        AddStep("measure at 16:9", () => at169 = area.DrawRectangle.Width / window.DrawRectangle.Height);

        build(3840, 1080);
        laidOut();
        AddAssert("the same field at 32:9", () => near(area.DrawRectangle.Width / window.DrawRectangle.Height, at169));
    }

    [Test]
    public void TestSizeShrinksTheWholeField()
    {
        build(1920, 1080, size: 0.5);
        laidOut();
        AddAssert("half as tall", () => near(area.DrawRectangle.Height, window.DrawRectangle.Height / 2));
        AddAssert("still the same shape", () => near(areaAspect, base_aspect));
    }

    [Test]
    public void TestPositionPlacesItInTheRoomLeft()
    {
        build(1920, 1080, size: 0.5, x: 0, y: 1);
        laidOut();
        AddAssert("against the left edge", () => near(area.DrawRectangle.X, window.DrawRectangle.X));
        AddAssert("against the bottom edge", () => near(bottom(area.DrawRectangle), bottom(window.DrawRectangle)));

        build(1920, 1080, size: 0.5, x: 1, y: 0);
        laidOut();
        AddAssert("against the right edge", () => near(right(area.DrawRectangle), right(window.DrawRectangle)));
        AddAssert("against the top edge", () => near(area.DrawRectangle.Y, window.DrawRectangle.Y));
    }

    [Test]
    public void TestFollowsSettingsLive()
    {
        build(1920, 1080);
        laidOut();
        AddStep("shrink to 60%", () => config.Get<double>(YuukiSetting.PlayfieldSize).Value = 0.6);
        AddUntilStep("the field follows", () => near(area.DrawRectangle.Height, window.DrawRectangle.Height * 0.6f));
    }

    [Test]
    public void TestOutOfRangeSettingsAreClamped()
    {
        build(1920, 1080, size: 3, x: 5, y: -2);
        laidOut();
        AddAssert("no bigger than fits", () => near(area.DrawRectangle.Height, window.DrawRectangle.Height));
        AddAssert("not past the right edge", () => right(area.DrawRectangle) <= right(window.DrawRectangle) + 0.5f);
    }
}
