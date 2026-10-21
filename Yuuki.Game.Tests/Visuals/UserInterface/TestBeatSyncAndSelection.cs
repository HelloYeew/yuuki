// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestBeatSyncAndSelection : TestScene
{
    [Cached]
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    [Cached]
    private readonly BeatmapsCollection beatmapsCollection = new BeatmapsCollection();

    [Cached]
    private readonly CoverColorStore coverColors = new CoverColorStore();

    private BeatmapSet set = null!;
    private Beatmap four = null!;
    private Beatmap seven = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("a 180 BPM song with a 4- and a 7-lane difficulty", () =>
        {
            Clear();

            set = BeatmapTestUtility.CreateDummyBeatmapSets(1)[0];
            set.BPM = 180;

            four = new Beatmap { ID = 1, BeatmapSet = set, DifficultyName = "Four", DifficultyRating = 3, LaneCount = 4 };
            seven = new Beatmap { ID = 2, BeatmapSet = set, DifficultyName = "Seven", DifficultyRating = 6, LaneCount = 7 };

            beatmapsCollection.BeatmapSets = new List<BeatmapSet> { set };
            beatmapsCollection.Beatmaps = new List<Beatmap> { four, seven };

            workingBeatmap.BeatmapSet = set;
            workingBeatmap.Beatmap = four;
        });
    }

    [Test]
    public void TestStoppedBeatIsTheDefaultTempo()
    {
        BeatSyncContainer steady = null!, waiting = null!;
        int steadyBeats = 0, waitingBeats = 0;

        AddStep("beat helpers, nothing playing", () =>
        {
            Add(steady = new BeatSyncContainer { AllowBeatSyncWhenStopped = true, UseDefaultTempoWhenStopped = true });
            Add(waiting = new BeatSyncContainer());
            steady.NewBeat += (_, _, _) => steadyBeats++;
            waiting.NewBeat += (_, _, _) => waitingBeats++;
        });

        AddUntilStep("the steady one keeps time", () => steadyBeats > 0);
        AddAssert("at 60 BPM, not the song's 180", () => steady.BeatLength == 1000);
        AddAssert("one that doesn't keep time waits", () => waitingBeats == 0);
    }

    [Test]
    public void TestNextButtonsPulseUnhovered()
    {
        AddAssert("Next keeps the beat unhovered", () => !new RightBottomShearButton().FlashToBeatOnlyWhenHovered);
        AddAssert("Back still waits for a hover", () => new LeftBottomShearButton().FlashToBeatOnlyWhenHovered);
    }

    [Test]
    public void TestFocusWindowShowsTheFocusedDifficulty()
    {
        BeatmapCarousel carousel = null!;

        AddStep("a difficulty list", () =>
        {
            Add(new Container
            {
                Size = new Vector2(600, 500),
                Child = carousel = new BeatmapCarousel()
            });
            carousel.Orientation.Value = CarouselOrientation.Vertical;
            carousel.SetItems(new[] { four, seven });
        });

        AddUntilStep("the window shows the first", () => carousel.FocusWindow?.ShownItem == four);
        AddStep("move to the other", () => carousel.SetIndex(1, animate: false));
        AddUntilStep("the window follows", () => carousel.FocusWindow?.ShownItem == seven);
        AddStep("as a strip", () => carousel.Orientation.Value = CarouselOrientation.Horizontal);
        AddAssert("still the same one", () => carousel.FocusWindow?.ShownItem == seven);
    }

    /// <summary>
    /// The window flashes on the beat while it shows what's playing, and not while it shows anything else.
    /// </summary>
    [Test]
    public void TestFocusWindowFlashesOnTheBeatForWhatsPlaying()
    {
        BeatmapCarousel carousel = null!;
        int flashes = 0;

        AddStep("a difficulty list on the playing one", () =>
        {
            Add(new Container
            {
                Size = new Vector2(600, 500),
                Child = carousel = new BeatmapCarousel()
            });
            carousel.Orientation.Value = CarouselOrientation.Vertical;
            carousel.SetItems(new[] { four, seven });
        });
        AddUntilStep("it flashes on the beat", () => carousel.FocusWindow?.BeatFlashCount >= 2);

        AddStep("move to one that isn't playing", () => carousel.SetIndex(1, animate: false));
        AddUntilStep("it's shown", () => carousel.FocusWindow!.ShownItem == seven);
        AddStep("count", () => flashes = carousel.FocusWindow!.BeatFlashCount);
        AddWaitStep("a few beats", 1500);
        AddAssert("no flashes for it", () => carousel.FocusWindow!.BeatFlashCount == flashes);

        AddStep("it starts playing", () => workingBeatmap.Beatmap = seven);
        AddUntilStep("it flashes", () => carousel.FocusWindow!.BeatFlashCount > flashes);
    }

    /// <summary>
    /// A wink (the streak of light across the window) when the list comes to rest on an item, once on
    /// arriving, and no more while it rests there.
    /// </summary>
    [Test]
    public void TestFocusWindowWinksOnArriving()
    {
        BeatmapCarousel carousel = null!;
        int arrived = 0;

        AddStep("a difficulty list", () =>
        {
            Add(new Container
            {
                Size = new Vector2(600, 500),
                Child = carousel = new BeatmapCarousel()
            });
            carousel.Orientation.Value = CarouselOrientation.Vertical;
            carousel.SetItems(new[] { four, seven });
        });

        AddUntilStep("a wink on arriving", () => carousel.FocusWindow?.WinkCount == 1);
        AddStep("note it", () => arrived = carousel.FocusWindow!.WinkCount);
        AddWaitStep("rest a while", 10);
        AddAssert("no more while resting", () => carousel.FocusWindow!.WinkCount == arrived);
    }

    /// <summary>
    /// Only an item the list stops on gets a wink: one it passes on the way, even right through its
    /// position, doesn't. Driven by hand, since a headless scroll jumps past the middle item.
    /// </summary>
    [Test]
    public void TestFocusWindowWinksOnlyWhereTheListStops()
    {
        BeatmapFocusWindow window = null!;
        Beatmap[] items = null!;

        AddStep("a window", () =>
        {
            items = new[] { four, seven, new Beatmap { ID = 3, BeatmapSet = set, DifficultyName = "Nine", DifficultyRating = 9, LaneCount = 7 } };
            Add(new Container
            {
                Size = new Vector2(600, 160),
                Child = window = new BeatmapFocusWindow()
            });
        });
        AddUntilStep("loaded", () => window.IsLoaded);

        AddStep("rest on the first", () =>
        {
            window.Display(items, 0);
            window.Display(items, 0);
        });
        AddAssert("a wink", () => window.WinkCount == 1);

        AddStep("scroll through the second to the third", () =>
        {
            foreach (double position in new[] { 0.4, 0.8, 1.0, 1.3, 1.7, 2.0 })
                window.Display(items, position);
        });
        AddAssert("none on the way", () => window.WinkCount == 1);

        AddStep("rest on the third", () => window.Display(items, 2));
        AddAssert("a wink for it", () => window.WinkCount == 2);
    }

    /// <summary>
    /// Song select fills its list during its own load, so the window shows its first song before it has
    /// loaded. That face must still fill in properly (its cover used to stay a blank placeholder).
    /// </summary>
    [Test]
    public void TestFocusWindowFilledBeforeItLoads()
    {
        BeatmapSetCarousel carousel = null!;

        AddStep("a song list filled before it's on screen", () =>
        {
            carousel = new BeatmapSetCarousel();
            carousel.Orientation.Value = CarouselOrientation.Vertical;
            carousel.SetItems(new[] { set });

            Add(new Container
            {
                Size = new Vector2(600, 500),
                Child = carousel
            });
        });

        AddUntilStep("the window shows the song in full", () => texts(carousel.FocusWindow!).Contains("4-7K"));
    }

    /// <summary>
    /// The song's lane counts show once on its window, on the lane label: the details under the artist
    /// (level, length and BPM, each after its icon) don't repeat them.
    /// </summary>
    [Test]
    public void TestFocusWindowShowsTheLanesOnce()
    {
        BeatmapSetCarousel carousel = null!;

        AddStep("a song list", () =>
        {
            Add(new Container
            {
                Size = new Vector2(600, 500),
                Child = carousel = new BeatmapSetCarousel()
            });
            carousel.Orientation.Value = CarouselOrientation.Vertical;
            carousel.SetItems(new[] { set });
        });

        AddUntilStep("the details are up", () => texts(carousel.FocusWindow!).Contains("3.0–6.0"));
        AddAssert("the lanes once", () => texts(carousel.FocusWindow!).Count(t => t == "4-7K") == 1);
        AddAssert("no dots between the details", () => !texts(carousel.FocusWindow!).Any(t => t.Contains(" · ")));
    }

    /// <summary>
    /// A song's window wears its hardest difficulty's tier until its cover has been decoded somewhere, then
    /// changes to the cover's own colour without having to be shown again.
    /// </summary>
    [Test]
    public void TestFocusWindowTakesTheCoverColorWhenItArrives()
    {
        BeatmapSetCarousel carousel = null!;
        byte[] red = solid(4, 4, 220, 40, 50);
        var swatch = CoverColorExtractor.Extract(red, 4, 4);

        AddStep("a song whose cover hasn't been decoded yet", () =>
        {
            // Its own cover file, so no colour picked in an earlier test applies to it.
            set.CoverStoragePath = $"covers/{Guid.NewGuid()}.png";

            Add(new Container
            {
                Size = new Vector2(600, 500),
                Child = carousel = new BeatmapSetCarousel()
            });
            carousel.Orientation.Value = CarouselOrientation.Vertical;
            carousel.SetItems(new[] { set });
        });

        AddUntilStep("it wears its tier", () => carousel.FocusWindow?.ShownItem == set
                                                && carousel.FocusWindow.FillColor == SetlistColors.TierFill(SetlistColors.SongTier(new[] { four, seven })));
        AddStep("its cover is decoded", () => coverColors.Pick(BeatmapSetCoverSource.KeyFor(set), red, 4, 4));
        AddUntilStep("it takes the cover's colour", () => carousel.FocusWindow!.FillColor == swatch.Fill);
    }

    private static byte[] solid(int width, int height, byte r, byte g, byte b)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    [Test]
    public void TestLaneCountsInTheInfo()
    {
        BeatmapInfoPanel panel = null!;

        AddStep("the song", () =>
        {
            Add(panel = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play)));
            panel.ShowSet(set);
        });
        AddUntilStep("the song row has the range", () => texts(panel).Contains("4-7K"));

        AddStep("the 7-lane difficulty", () => panel.ShowBeatmap(seven));
        AddUntilStep("its row has its count", () => texts(panel).Contains("7K"));
    }

    [Test]
    public void TestLaneCountsOnTheCards()
    {
        Reactive<CarouselOrientation> orientation = null!;
        BeatmapSetCarouselCard setCard = null!;
        BeatmapCard fourCard = null!, sevenCard = null!;

        AddStep("the song's card and its difficulties', in a list", () =>
        {
            orientation = new Reactive<CarouselOrientation>(CarouselOrientation.Vertical);

            setCard = new BeatmapSetCarouselCard { Orientation = orientation };
            fourCard = new BeatmapCard { Orientation = orientation };
            fourCard.Apply(four, 0);
            sevenCard = new BeatmapCard { Orientation = orientation };
            sevenCard.Apply(seven, 1);

            Add(new Container
            {
                Size = new Vector2(400, 200),
                Children = new Drawable[] { setCard, fourCard, sevenCard }
            });
        });
        AddUntilStep("loaded", () => setCard.IsLoaded);
        AddStep("show the song", () => setCard.Apply(set, 0));

        AddAssert("the song's card has the range", () => setCard.LaneText == "4-7K");
        AddAssert("each difficulty its count", () => fourCard.LaneText == "4K" && sevenCard.LaneText == "7K");

        // A song's tile is only its cover, title and artist (the hero tile and info panel carry the lanes),
        // while a difficulty's tile puts its count in the caption under the level.
        AddStep("as a strip", () => orientation.Value = CarouselOrientation.Horizontal);
        AddStep("show the song again", () => setCard.Apply(set, 0));
        AddAssert("the song still knows its range", () => setCard.LaneText == "4-7K");
        AddAssert("each difficulty's caption has its count", () => texts(fourCard).Count(t => t.StartsWith("4K", StringComparison.Ordinal)) == 2);
    }

    /// <summary>
    /// A cover fills its parent unless told otherwise; a row's or tile's cover has its own size, and must
    /// not end up stretched across (or far past) the card.
    /// </summary>
    [Test]
    public void TestCoversKeepTheirOwnSize()
    {
        Reactive<CarouselOrientation> orientation = null!;
        BeatmapSetCarouselCard card = null!;

        AddStep("a song card in the rail", () =>
        {
            orientation = new Reactive<CarouselOrientation>(CarouselOrientation.Vertical);
            card = new BeatmapSetCarouselCard { Orientation = orientation };

            Add(new Container
            {
                Size = new Vector2(400, 60),
                Child = card
            });
        });
        AddUntilStep("loaded", () => card.IsLoaded);
        AddStep("show the song", () => card.Apply(set, 0));
        AddAssert("the row's cover is a small square", () => covers(card).All(c => c.DrawSize.X <= 60 && c.DrawSize.Y <= 60));

        AddStep("as a strip", () => orientation.Value = CarouselOrientation.Horizontal);
        AddStep("show the song again", () => card.Apply(set, 0));
        AddAssert("the tile's cover fits the tile", () => covers(card).All(c => c.DrawSize.X <= 130 && c.DrawSize.Y <= 130));
    }

    private static IEnumerable<DelayedLoadCover> covers(Drawable root)
    {
        if (root is DelayedLoadCover cover)
            yield return cover;

        if (root is Container container)
        {
            foreach (var c in container.Children.SelectMany(covers))
                yield return c;
        }
    }

    private static IEnumerable<string> texts(Drawable root)
    {
        if (root is SpriteText text)
            yield return text.Text;

        if (root is Container container)
        {
            foreach (string t in container.Children.SelectMany(texts))
                yield return t;
        }
    }
}
