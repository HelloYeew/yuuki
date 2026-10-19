// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestCarouselWheel : YuukiManualInputManagerTestScene
{
    private readonly HeadlessTextureManager textureManager = new HeadlessTextureManager();

    private BeatmapSetCarousel carousel = null!;
    private readonly List<(int Direction, bool Moved)> scrolled = new List<(int Direction, bool Moved)>();

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<ITextureManager>(textureManager);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        textureManager.Dispose();
    }

    [Test]
    public void TestWheelOverCarouselIsReported()
    {
        AddStep("add carousel", () =>
        {
            scrolled.Clear();

            TestContent.Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                Height = 150,
                Child = carousel = new BeatmapSetCarousel()
            });
            carousel.SetItems(BeatmapTestUtility.CreateDummyBeatmapSets(5));
            carousel.Scrolled += (direction, moved) => scrolled.Add((direction, moved));
        });
        AddStep("hover the carousel", () => InputManager.MoveMouseTo(carousel));

        AddStep("wheel down", () => InputManager.ScrollBy(new Vector2(0, -1)));
        AddAssert("moved on and reported", () => carousel.CurrentIndex == 1 && scrolled.Count == 1 && scrolled[0] == (1, true));

        AddStep("wheel up", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("moved back and reported", () => carousel.CurrentIndex == 0 && scrolled.Count == 2 && scrolled[1] == (-1, true));

        // At the first item there's nowhere to go: still reported, so the owner can play the end-stop tick.
        AddStep("wheel up at the start", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("reported as not moving", () => carousel.CurrentIndex == 0 && scrolled.Count == 3 && scrolled[2] == (-1, false));
    }
}
