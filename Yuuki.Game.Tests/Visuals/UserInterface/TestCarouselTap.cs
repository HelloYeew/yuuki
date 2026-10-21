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

/// <summary>
/// Clicking a carousel: another card selects it, the selected card again is reported apart from that, so
/// song select can treat it as moving on.
/// </summary>
public partial class TestCarouselTap : YuukiManualInputManagerTestScene
{
    private readonly HeadlessTextureManager textureManager = new HeadlessTextureManager();

    private BeatmapSetCarousel carousel = null!;
    private readonly List<bool> tapped = new List<bool>();
    private int selectedTaps;

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
    public void TestClickingTheSelectedCard()
    {
        addCarousel();

        AddStep("click the selected card", () => clickSlot(0));
        AddAssert("reported as the selected card", () => selectedTaps == 1 && tapped.Count == 0);
        AddAssert("selection unchanged", () => carousel.CurrentIndex == 0);
    }

    [Test]
    public void TestClickingAnotherCardThenAgain()
    {
        addCarousel();

        AddStep("click the next card", () => clickSlot(1));
        AddAssert("selected and reported as a move", () => carousel.CurrentIndex == 1 && tapped.Count == 1 && tapped[0] && selectedTaps == 0);

        AddStep("let it settle", () => carousel.SetIndex(carousel.CurrentIndex, animate: false));
        AddStep("click it again", () => clickSlot(0));
        AddAssert("now reported as the selected card", () => selectedTaps == 1 && tapped.Count == 1);
    }

    /// <summary>
    /// With the last card selected, a click past it lands on no card: the selection stays put, but it
    /// isn't a click on the selected card.
    /// </summary>
    [Test]
    public void TestClickingPastTheEnd()
    {
        addCarousel();

        AddStep("select the last card", () => carousel.SetIndex(4, animate: false));
        AddStep("click past it", () => clickSlot(1));
        AddAssert("not the selected card", () => selectedTaps == 0);
        AddAssert("an ordinary click that moved nothing", () => tapped.Count == 1 && !tapped[0] && carousel.CurrentIndex == 4);
    }

    private void addCarousel()
    {
        AddStep("add carousel", () =>
        {
            tapped.Clear();
            selectedTaps = 0;

            TestContent.Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                // The strip's height, with room above the tiles for the hero tile to rise into.
                Height = 350,
                Child = carousel = new BeatmapSetCarousel()
            });
            carousel.SetItems(BeatmapTestUtility.CreateDummyBeatmapSets(5));
            carousel.Tapped += moved => tapped.Add(moved);
            carousel.SelectedTapped += () => selectedTaps++;
        });
    }

    /// <summary>
    /// Clicks the slot <paramref name="offset"/> cards along from the selected one, where the carousel
    /// draws it (past either end, where a card would be).
    /// </summary>
    private void clickSlot(int offset)
    {
        InputManager.MoveMouseTo(carousel.ToScreenSpace(carousel.SlotCentre(carousel.CurrentIndex + offset)));
        InputManager.Click(MouseButton.Left);
    }
}
