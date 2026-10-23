// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Input;
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

    [Test]
    public void TestTrackpadNotchesAddUp()
    {
        addEvenCarousel(1);
        AddStep("hover the carousel", () => InputManager.MoveMouseTo(carousel));

        AddStep("three quarters of a notch", () =>
        {
            for (int i = 0; i < 3; i++)
                InputManager.ScrollBy(new Vector2(0, -0.25f));
        });
        AddAssert("not a card yet", () => carousel.CurrentIndex == 0 && scrolled.Count == 0);

        AddStep("the last quarter", () => InputManager.ScrollBy(new Vector2(0, -0.25f)));
        AddAssert("one card on", () => carousel.CurrentIndex == 1 && scrolled.Count == 1 && scrolled[0] == (1, true));

        AddStep("half back, then on", () =>
        {
            InputManager.ScrollBy(new Vector2(0, 0.5f));
            InputManager.ScrollBy(new Vector2(0, -0.75f));
        });
        AddAssert("back is forgotten going on", () => carousel.CurrentIndex == 1 && scrolled.Count == 1);
    }

    [Test]
    public void TestTrackpadMovesACardAPitch()
    {
        // Scaled to half, as the game is on a phone: a card is half as far on screen.
        addEvenCarousel(0.5f);

        AddStep("fingers up 80% of a card, in fifths", () =>
        {
            for (int i = 0; i < 4; i++)
                swipe(-even_pitch / 10);
        });
        AddAssert("not a card yet", () => carousel.CurrentIndex == 0 && scrolled.Count == 0);

        AddStep("the last fifth", () => swipe(-even_pitch / 10));
        AddAssert("one card on", () => carousel.CurrentIndex == 1 && scrolled.Count == 1 && scrolled[0] == (1, true));

        AddStep("two cards in one go", () => swipe(-even_pitch));
        AddAssert("two on, reported once", () => carousel.CurrentIndex == 3 && scrolled.Count == 2 && scrolled[1] == (1, true));

        AddStep("down a card and a half", () => swipe(even_pitch * 0.75f));
        AddAssert("one back", () => carousel.CurrentIndex == 2 && scrolled.Count == 3 && scrolled[2] == (-1, true));
    }

    private const float even_pitch = 100;

    private void addEvenCarousel(float scale) => AddStep("add carousel", () =>
    {
        scrolled.Clear();

        TestContent.Add(new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.X,
            Height = 150,
            Scale = new Vector2(scale),
            Child = carousel = new EvenCarousel()
        });
        carousel.SetItems(BeatmapTestUtility.CreateDummyBeatmapSets(5));
        carousel.Scrolled += (direction, moved) => scrolled.Add((direction, moved));
    });

    // A trackpad swipe as macOS gives it, in notches and in how far the fingers moved on screen.
    private void swipe(float screenY) => carousel.OnScroll(new ScrollEvent(new MouseState { Position = carousel.ToScreenSpace(carousel.DrawSize / 2) },
        new Vector2(0, screenY / 10), new Vector2(0, screenY)));

    // A strip a set pitch apart, which skimming doesn't shorten.
    private partial class EvenCarousel : BeatmapSetCarousel
    {
        public EvenCarousel()
        {
            AllowSkim = false;
        }

        protected override CarouselLayoutStrategy CreateLayout(CarouselOrientation orientation) => new StripCarouselLayout { Pitch = even_pitch };
    }
}
