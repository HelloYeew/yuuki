// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// A difficulty's name and the line under it, in the list and in its focus window, as the rail and the strip show them. They
/// scroll where they're too long for the card, rather than being cut short. Long names are here to watch that.
/// </summary>
public partial class TestDifficultyCardText : YuukiTestScene
{
    private const string long_name = "A difficulty with quite a remarkably long name that will never fit on one of these cards";
    private const string long_mapper = "A mapper with a very long name indeed, who collaborated with several others on it";

    [Cached]
    private readonly BeatmapsCollection beatmapsCollection = new BeatmapsCollection();

    [Cached]
    private readonly CoverColorStore coverColors = new CoverColorStore();

    [Cached]
    private readonly ScoreStore scores = new ScoreStore(null);

    private BeatmapCarousel carousel = null!;
    private List<Beatmap> beatmaps = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    private void showList(CarouselOrientation orientation)
    {
        AddStep($"a list of difficulties, {orientation}", () =>
        {
            Clear();

            BeatmapSet set = BeatmapTestUtility.CreateDummyBeatmapSets(1)[0];

            beatmaps = new List<Beatmap>
            {
                new Beatmap { ID = 1, BeatmapSet = set, DifficultyName = "Normal", Creator = "Miqu", DifficultyRating = 3, LaneCount = 4 },
                new Beatmap { ID = 2, BeatmapSet = set, DifficultyName = long_name, Creator = long_mapper, DifficultyRating = 5, LaneCount = 7 },
                new Beatmap { ID = 3, BeatmapSet = set, DifficultyName = "Hard", Creator = long_mapper, DifficultyRating = 6, LaneCount = 4 },
                new Beatmap { ID = 4, BeatmapSet = set, DifficultyName = long_name, Creator = "Miqu", DifficultyRating = 8, LaneCount = 7 }
            };

            beatmapsCollection.BeatmapSets = new List<BeatmapSet> { set };
            beatmapsCollection.Beatmaps = beatmaps;

            Add(new Container
            {
                Size = orientation == CarouselOrientation.Vertical ? new Vector2(700, 600) : new Vector2(1100, 500),
                Child = carousel = new BeatmapCarousel()
            });
            carousel.Orientation.Value = orientation;
            carousel.SetItems(beatmaps);
            carousel.SetIndex(1, false);
        });
        AddUntilStep("there", () => carousel.IsLoaded && carousel.CurrentIndex == 1 && carousel.FocusWindow != null);
        AddWaitStep("laid out", 200);
    }

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

    private static bool shown(Drawable drawable, Drawable upTo)
    {
        for (Drawable? d = drawable; d != null && d != upTo; d = d.Parent)
        {
            if (d.Alpha <= 0)
                return false;
        }

        return true;
    }

    private IEnumerable<BeatmapCard> cards => descendants(carousel).OfType<BeatmapCard>();

    // What says the name or the line under it, on what's showing of this.
    private static IEnumerable<ScrollingText> scrolling(Drawable within, string saying) =>
        descendants(within).OfType<ScrollingText>().Where(t => shown(t, within) && t.Text.Contains(saying));

    // Whether a plain, cut-off text says it instead.
    private static bool cutOff(Drawable within, string saying) =>
        descendants(within).OfType<SpriteText>().Any(t => t.Parent is not Container { Masking: true } && shown(t, within) && t.Text.Contains(saying));

    [Test]
    public void TestRowsAndTheirWindow()
    {
        showList(CarouselOrientation.Vertical);

        AddAssert("the window's name scrolls", () => scrolling(carousel.FocusWindow!, long_name).Any());
        AddAssert("and its mapper", () => scrolling(carousel.FocusWindow!, $"mapped by {long_mapper}").Any());
        AddAssert("a row's name scrolls", () => cards.Any(c => scrolling(c, "Normal").Any()));
        AddAssert("and its mapper", () => cards.Any(c => scrolling(c, "mapped by Miqu").Any()));
        AddAssert("neither's plain text any more", () => cards.All(c => !cutOff(c, "mapped by")) && !cutOff(carousel.FocusWindow!, "mapped by"));
        AddAssert("the rows' text stops short of what's at their right", () => cards.All(textClearsExtras));
        AddAssert("and the window's", () => textClearsExtras(carousel.FocusWindow!));
    }

    [Test]
    public void TestTiles()
    {
        showList(CarouselOrientation.Horizontal);

        AddAssert("a tile's name scrolls", () => cards.Any(c => scrolling(c, "Normal").Any()));
        AddAssert("and its line under it", () => cards.Any(c => scrolling(c, "Miqu").Any()));
        AddAssert("as wide as its square", () => cards.SelectMany(c => scrolling(c, "Normal")).All(t => t.DrawWidth > 0 && t.DrawWidth <= 130));
        AddAssert("neither's plain text any more", () => cards.All(c => !cutOff(c, "Normal") && !cutOff(c, "Miqu")));
    }

    // The line of text ends before the lanes begin.
    private static bool textClearsExtras(Drawable card)
    {
        ScrollingText? name = descendants(card).OfType<ScrollingText>().FirstOrDefault(t => shown(t, card));
        LaneLabel? lanes = descendants(card).OfType<LaneLabel>().FirstOrDefault(l => shown(l, card));

        if (name == null || lanes == null)
            return true;

        float nameEnd = card.ToLocalSpace(name.ScreenSpaceDrawQuad.TopRight).X;
        float lanesStart = card.ToLocalSpace(lanes.ScreenSpaceDrawQuad.TopLeft).X;
        return name.DrawWidth > 0 && nameEnd <= lanesStart;
    }
}
