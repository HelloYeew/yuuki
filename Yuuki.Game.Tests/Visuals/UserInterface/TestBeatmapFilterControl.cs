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
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestBeatmapFilterControl : YuukiManualInputManagerTestScene
{
    private BeatmapFilterControl filters = null!;
    private int clicksBehind;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [Test]
    public void TestSortListOpensOverTheSongList()
    {
        AddStep("add filters over a song list", () =>
        {
            clicksBehind = 0;
            TestContent.Add(new ClickableContainer
            {
                RelativeSizeAxes = Axes.Both,
                Action = () => clicksBehind++
            });
            TestContent.Add(filters = new BeatmapFilterControl
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight
            });
            filters.SetLayout(CarouselOrientation.Vertical, 600);
        });

        AddStep("open the sort list", () => clickOn(text("Added")!.Parent!));
        AddWaitStep("let it open", 400);
        AddStep("pick Artist", () => clickOn(texts().Last(t => t.Text == "Artist").Parent!));

        AddAssert("sorted by artist", () => filters.Filter.Sort == BeatmapSort.Artist);
        AddAssert("the list underneath wasn't clicked", () => clicksBehind == 0);
    }

    private void clickOn(Drawable target)
    {
        InputManager.MoveMouseTo(target);
        InputManager.PressButton(MouseButton.Left);
        InputManager.ReleaseButton(MouseButton.Left);
    }

    private SpriteText? text(string value) => texts().FirstOrDefault(t => t.Text == value);

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
