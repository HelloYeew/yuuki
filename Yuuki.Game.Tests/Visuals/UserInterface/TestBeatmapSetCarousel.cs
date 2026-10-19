// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestBeatmapSetCarousel : TestScene
{
    private readonly HeadlessTextureManager textureManager = new HeadlessTextureManager();

    private BeatmapSetCarousel carousel = null!;
    private List<BeatmapSet> sets = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        // The cards' cover art uses DelayedLoadCover, which decodes through a texture manager.
        Cache<ITextureManager>(textureManager);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        textureManager.Dispose();
    }

    [Test]
    public void TestSetItems()
    {
        addCarousel(5);

        AddAssert("count is 5", () => carousel.Count == 5);
        AddAssert("first item selected", () => carousel.CurrentIndex == 0 && ReferenceEquals(carousel.CurrentItem.Value, sets[0]));
    }

    [Test]
    public void TestNextPreviousClamps()
    {
        addCarousel(5);

        AddStep("next", () => carousel.Next());
        AddAssert("index 1", () => carousel.CurrentIndex == 1);

        AddStep("previous", () => carousel.Previous());
        AddAssert("index 0", () => carousel.CurrentIndex == 0);

        AddStep("previous at start", () => carousel.Previous());
        AddAssert("still index 0 (clamped)", () => carousel.CurrentIndex == 0);

        AddStep("jump past end", () => carousel.SetIndex(100));
        AddAssert("clamped to last", () => carousel.CurrentIndex == 4);
    }

    [Test]
    public void TestSelectItem()
    {
        addCarousel(5);

        AddStep("select 4th set", () => carousel.SelectItem(sets[3]));
        AddAssert("selected 4th set", () => carousel.CurrentIndex == 3 && ReferenceEquals(carousel.CurrentItem.Value, sets[3]));

        AddStep("select unknown set", () => carousel.SelectItem(new BeatmapSet { ID = 999, Title = "Ghost" }));
        AddAssert("selection unchanged", () => carousel.CurrentIndex == 3);
    }

    [Test]
    public void TestAddItem()
    {
        AddStep("add empty carousel", () =>
        {
            sets = new List<BeatmapSet>();
            Clear();
            Add(wrap(carousel = new BeatmapSetCarousel()));
            carousel.SetItems(sets);
        });

        AddAssert("empty, nothing selected", () => carousel.Count == 0 && carousel.CurrentItem.Value == null);

        AddStep("add first", () => carousel.AddItem(BeatmapTestUtility.MakeBeatmapSet(1)));
        AddAssert("count 1, first auto-selected", () => carousel.Count == 1 && carousel.CurrentIndex == 0 && carousel.CurrentItem.Value != null);

        BeatmapSet firstSelected = null!;
        AddStep("remember selection", () => firstSelected = carousel.CurrentItem.Value!);

        AddStep("add second", () => carousel.AddItem(BeatmapTestUtility.MakeBeatmapSet(2)));
        AddAssert("count 2, selection preserved", () => carousel.Count == 2 && ReferenceEquals(carousel.CurrentItem.Value, firstSelected));
    }

    [Test]
    public void TestAddItemAfterSetItemsKeepsSelection()
    {
        addCarousel(3);

        AddStep("select middle", () => carousel.SelectItem(sets[1]));
        AddAssert("index 1", () => carousel.CurrentIndex == 1);

        AddStep("append one", () => carousel.AddItem(BeatmapTestUtility.MakeBeatmapSet(99)));
        AddAssert("count 4, selection kept", () => carousel.Count == 4 && ReferenceEquals(carousel.CurrentItem.Value, sets[1]));
    }

    [Test]
    public void TestRemoveItem()
    {
        addCarousel(5);

        AddStep("select 4th set", () => carousel.SelectItem(sets[3]));
        AddAssert("index 3", () => carousel.CurrentIndex == 3);

        // Removing an earlier item shifts the selection down but keeps the same logical set selected.
        AddStep("remove 2nd set", () => carousel.RemoveItem(sets[1]));
        AddAssert("count 4, same set now at index 2", () => carousel.Count == 4 && carousel.CurrentIndex == 2 && ReferenceEquals(carousel.CurrentItem.Value, sets[3]));

        // Removing the selected item lands on the item that shifts into its place.
        AddStep("remove selected set", () => carousel.RemoveItem(sets[3]));
        AddAssert("count 3", () => carousel.Count == 3);
        AddAssert("selection still valid", () => carousel.CurrentItem.Value != null && carousel.CurrentIndex >= 0 && carousel.CurrentIndex < 3);

        // Removing something not present is a no-op.
        bool removed = true;
        AddStep("remove unknown set", () => removed = carousel.RemoveItem(new BeatmapSet { ID = 999 }));
        AddAssert("returns false", () => !removed);
        AddAssert("count still 3", () => carousel.Count == 3);
    }

    [Test]
    public void TestAddRemoveAreAnimatedAndSettle()
    {
        addCarousel(4);

        AddStep("append item", () => carousel.AddItem(BeatmapTestUtility.MakeBeatmapSet(50)));
        AddAssert("count 5 immediately", () => carousel.Count == 5);
        AddUntilStep("add animation settles", () => !carousel.IsAnimating);

        AddStep("remove selected", () => carousel.RemoveItem(carousel.CurrentItem.Value!));
        AddAssert("count 4 immediately", () => carousel.Count == 4);
        AddUntilStep("remove animation settles + card cleaned up", () => !carousel.IsAnimating);
    }

    [Test]
    public void TestOrientationSwapKeepsSelection()
    {
        addCarousel(6);

        AddStep("select 4th set", () => carousel.SelectItem(sets[3], animate: false));
        AddAssert("index 3", () => carousel.CurrentIndex == 3);

        AddStep("switch to vertical", () => carousel.Orientation.Value = CarouselOrientation.Vertical);
        AddAssert("selection preserved", () => carousel.CurrentIndex == 3 && ReferenceEquals(carousel.CurrentItem.Value, sets[3]));

        AddStep("navigate while vertical", () => carousel.Next());
        AddAssert("index 4", () => carousel.CurrentIndex == 4);

        AddStep("switch back to horizontal", () => carousel.Orientation.Value = CarouselOrientation.Horizontal);
        AddAssert("selection still index 4", () => carousel.CurrentIndex == 4 && ReferenceEquals(carousel.CurrentItem.Value, sets[4]));
    }

    private void addCarousel(int count)
    {
        AddStep($"add carousel with {count} sets", () =>
        {
            sets = BeatmapTestUtility.CreateDummyBeatmapSets(count);
            Clear();
            Add(wrap(carousel = new BeatmapSetCarousel()));
            carousel.SetItems(sets);
        });
    }

    /// <summary>
    /// Wraps the carousel in a fixed-height, full-width strip so it lays out like it does in song select.
    /// </summary>
    private static Container wrap(BeatmapSetCarousel carousel) => new Container
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.X,
        Height = 150,
        Child = carousel
    };
}
