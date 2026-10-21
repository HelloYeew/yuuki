// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// Song select's header: the headline search, the level histogram, lane chips and sort words.
/// </summary>
public partial class TestBeatmapFilterControl : YuukiManualInputManagerTestScene
{
    private BeatmapFilterControl filters = null!;
    private int changes;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("add the header", () =>
        {
            changes = 0;
            TestContent.Clear();
            TestContent.Add(filters = new BeatmapFilterControl
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight
            });
            filters.SetLayout(CarouselOrientation.Vertical, 640);
            filters.SetLanes(new[] { 7, 4, 4, 6 });
            filters.SetLevelCounts(Enumerable.Range(0, BeatmapFilter.MAX_LEVEL + 1).ToArray());
            filters.FilterChanged += _ => changes++;
        });
        AddUntilStep("laid out", () => filters.IsLoaded && filters.LevelBarsArea.DrawRectangle.Width > 0);
        AddWaitStep("settle", 2);
    }

    [Test]
    public void TestLaneChipsToggle()
    {
        AddAssert("a chip for each lane count, in order", () => texts().Where(t => t.Text.EndsWith('K')).Select(t => t.Text).SequenceEqual(new[] { "4K", "6K", "7K" }));

        AddStep("pick 7K", () => clickOn(control("7K")));
        AddAssert("only 7K", () => filters.Filter.Lanes.SetEquals(new[] { 7 }));

        AddStep("pick 4K too", () => clickOn(control("4K")));
        AddAssert("4K and 7K", () => filters.Filter.Lanes.SetEquals(new[] { 4, 7 }));

        AddStep("unpick both", () =>
        {
            clickOn(control("7K"));
            clickOn(control("4K"));
        });
        AddAssert("every lane count again", () => filters.Filter.Lanes.Count == 0);
        AddAssert("each click changed the filter", () => changes == 4);
    }

    [Test]
    public void TestOneLaneCountHasNoChips()
    {
        AddStep("a 4K-only library", () => filters.SetLanes(new[] { 4, 4 }));
        AddAssert("no chips to pick between", () => text("4K") == null || hidden(text("4K")!));
    }

    [Test]
    public void TestSortWords()
    {
        AddStep("sort by artist", () => clickOn(control("artist")));

        AddAssert("sorted by artist", () => filters.Filter.Sort == BeatmapSort.Artist);
        AddAssert("the filter changed", () => changes == 1);

        AddStep("artist again", () => clickOn(control("artist")));
        AddAssert("nothing more to change", () => changes == 1);
    }

    /// <summary>
    /// A press moves whichever end of the range is nearer, and dragging carries it on.
    /// </summary>
    [Test]
    public void TestDraggingTheLevelRange()
    {
        AddStep("press on level 4", () =>
        {
            InputManager.MoveMouseTo(levelPosition(4));
            InputManager.PressButton(MouseButton.Left);
        });
        AddAssert("the low end came to it", () => range() == (4, BeatmapFilter.MAX_LEVEL));

        AddStep("drag down to level 2", () =>
        {
            InputManager.MoveMouseTo(levelPosition(3));
            InputManager.MoveMouseTo(levelPosition(2));
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddAssert("it followed", () => filters.Filter.MinLevel == 2);

        AddStep("press on level 12", () => clickAt(levelPosition(12)));
        AddAssert("the high end came to it", () => range() == (2, 12));
    }

    [Test]
    public void TestTierTakesItsLevels()
    {
        AddStep("take Expert", () => clickOn(control("EXPERT")));
        AddAssert("levels 8 to 11", () => range() == (8, 11));

        AddStep("Expert again", () => clickOn(control("EXPERT")));
        AddAssert("every level again", () => range() == (0, BeatmapFilter.MAX_LEVEL));
    }

    [Test]
    public void TestCountReadsAsSongsOrAsAShare()
    {
        AddStep("nothing filtered", () => filters.SetCounts(360, 360));
        AddAssert("360 songs", () => countText() == "360 songs");

        AddStep("a lane picked", () => clickOn(control("7K")));
        AddStep("37 of them pass", () => filters.SetCounts(37, 360));
        AddAssert("37 of 360", () => countText() == "37 of 360");
    }

    private (int, int) range() => (filters.Filter.MinLevel, filters.Filter.MaxLevel);

    private string countText()
    {
        var all = texts().ToList();
        int number = all.FindIndex(t => t.Text is "360" or "37");
        return number < 0 ? "" : all[number].Text + all[number + 1].Text;
    }

    private Vector2 levelPosition(int level)
    {
        var area = filters.LevelBarsArea.DrawRectangle;
        return new Vector2(area.X + (level + 0.5f) / (BeatmapFilter.MAX_LEVEL + 1) * area.Width, area.Y + area.Height / 2);
    }

    private void clickOn(Drawable target)
    {
        InputManager.MoveMouseTo(target);
        InputManager.PressButton(MouseButton.Left);
        InputManager.ReleaseButton(MouseButton.Left);
    }

    private void clickAt(Vector2 position)
    {
        InputManager.MoveMouseTo(position);
        InputManager.PressButton(MouseButton.Left);
        InputManager.ReleaseButton(MouseButton.Left);
    }

    private bool hidden(Drawable drawable)
    {
        for (Drawable? d = drawable; d != null && d != filters; d = d.Parent)
        {
            if (d.Alpha <= 0)
                return true;
        }

        return false;
    }

    private SpriteText? text(string value) => texts().FirstOrDefault(t => t.Text == value);

    /// <summary>
    /// The chip, word or tab labeled <paramref name="label"/>: what to click, since text has no size in a
    /// headless run.
    /// </summary>
    private Drawable control(string label) => text(label)!.Parent!;

    private IEnumerable<SpriteText> texts() => descendants(filters).OfType<SpriteText>();

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
