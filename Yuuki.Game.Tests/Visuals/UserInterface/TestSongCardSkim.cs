// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// A song's card as the list skims: its title and artist stay, and so does your best mark, moving over to the edge of the
/// row as the skyline and lanes step aside. The slider sets how far into skimming it is.
/// </summary>
public partial class TestSongCardSkim : YuukiTestScene
{
    private const float row_width = 800;

    [Cached]
    private readonly BeatmapsCollection beatmapsCollection = new BeatmapsCollection();

    [Cached]
    private readonly CoverColorStore coverColors = new CoverColorStore();

    [Cached]
    private readonly ScoreStore scores = new ScoreStore(null);

    private readonly CarouselMotion motion = new CarouselMotion();

    private BeatmapSet played = null!;
    private BeatmapSet notPlayed = null!;
    private BeatmapSetCarouselCard playedRow = null!;
    private BeatmapSetCarouselCard notPlayedRow = null!;
    private BeatmapSetCarouselCard tile = null!;

    // The score store lasts the whole scene: each test's songs are new ones to it.
    private int lastSetId = 2000;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    private void setSkim(float amount) =>
        typeof(CarouselMotion).GetProperty(nameof(CarouselMotion.Skim))!.GetSetMethod(true)!.Invoke(motion, new object[] { amount });

    private BeatmapSet song(string title)
    {
        BeatmapSet set = BeatmapTestUtility.CreateDummyBeatmapSets(1)[0];
        set.ID = ++lastSetId;
        set.Title = title;
        return set;
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("a song played and a song not, as rows and a tile", () =>
        {
            Clear();
            setSkim(0);

            played = song("A song you have played");
            notPlayed = song("A song you have not played");

            beatmapsCollection.BeatmapSets = new List<BeatmapSet> { played, notPlayed };
            beatmapsCollection.Beatmaps = new List<Beatmap>
            {
                new Beatmap { ID = 1, BeatmapSet = played, DifficultyName = "Four", DifficultyRating = 3, LaneCount = 4 },
                new Beatmap { ID = 2, BeatmapSet = played, DifficultyName = "Seven", DifficultyRating = 6, LaneCount = 7 },
                new Beatmap { ID = 3, BeatmapSet = notPlayed, DifficultyName = "Four", DifficultyRating = 3, LaneCount = 4 }
            };

            scores.Add(new ScoreInfo
            {
                BeatmapId = 1,
                BeatmapSetId = played.ID,
                Score = 950_000,
                Accuracy = 0.97,
                Rank = ScoreRank.S,
                IsFullCombo = true,
                Date = DateTimeOffset.Now
            });

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(new Container
            {
                Position = new Vector2(20, 20),
                Size = new Vector2(row_width, 80),
                Child = playedRow = card(CarouselOrientation.Vertical)
            });
            Add(new Container
            {
                Position = new Vector2(20, 110),
                Size = new Vector2(row_width, 80),
                Child = notPlayedRow = card(CarouselOrientation.Vertical)
            });
            Add(new Container
            {
                Position = new Vector2(20, 200),
                Size = new Vector2(200, 240),
                Child = tile = card(CarouselOrientation.Horizontal)
            });
        });
        AddUntilStep("loaded", () => playedRow.IsLoaded && notPlayedRow.IsLoaded && tile.IsLoaded);
        AddStep("show the songs", () =>
        {
            playedRow.Apply(played, 0);
            notPlayedRow.Apply(notPlayed, 1);
            tile.Apply(played, 0);
        });
        AddWaitStep("laid out", 2);
    }

    private BeatmapSetCarouselCard card(CarouselOrientation orientation) => new BeatmapSetCarouselCard
    {
        Orientation = new Reactive<CarouselOrientation>(orientation),
        Motion = motion
    };

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

    private static float alphaOf(Drawable drawable, Drawable upTo)
    {
        float alpha = 1;

        for (Drawable? d = drawable; d != null && d != upTo; d = d.Parent)
            alpha *= d.Alpha;

        return alpha;
    }

    private static bool shows(BeatmapSetCarouselCard card, string text) =>
        descendants(card).OfType<SpriteText>().Any(t => t.Text == text && alphaOf(t, card) > 0);

    // Where the best mark's right edge is, in from the card's.
    private static float bestInFromTheRight(BeatmapSetCarouselCard card)
    {
        BestMark mark = descendants(card).OfType<BestMark>().Single(m => m.Alpha > 0);
        return card.DrawWidth - card.ToLocalSpace(mark.ScreenSpaceDrawQuad.TopRight).X;
    }

    [Test]
    public void TestSkimming()
    {
        AddSliderStep("how far into skimming", 0f, 1f, 0f, setSkim);

        float atRest = 0;

        AddAssert("at rest: its best", () => shows(playedRow, "S") && shows(playedRow, "FC"));
        AddAssert("and its lanes", () => shows(playedRow, "4-7K"));
        AddStep("note where the best is", () => atRest = bestInFromTheRight(playedRow));
        AddAssert("left of the lanes", () => atRest > 60);

        AddStep("half way", () => setSkim(0.5f));
        AddWaitStep("laid out", 2);
        AddAssert("the best's on its way over", () => bestInFromTheRight(playedRow) < atRest && bestInFromTheRight(playedRow) > 30);

        AddStep("skimming", () => setSkim(1));
        AddWaitStep("laid out", 2);
        AddAssert("its best stays", () => shows(playedRow, "S") && shows(playedRow, "FC"));
        AddAssert("not fading with the rest", () => descendants(playedRow).OfType<BestMark>().Single(m => m.Alpha > 0) is var mark && alphaOf(mark, playedRow) == 1);
        AddAssert("the lanes have stepped aside", () => !shows(playedRow, "4-7K"));
        AddAssert("the best's at the edge", () => bestInFromTheRight(playedRow) < 40);
        AddAssert("the title stops short of it", () =>
        {
            BestMark mark = descendants(playedRow).OfType<BestMark>().Single(m => m.Alpha > 0);
            ScrollingText title = descendants(playedRow).OfType<ScrollingText>().First(t => t.Text == played.Title);

            float titleEnd = playedRow.ToLocalSpace(title.ScreenSpaceDrawQuad.TopRight).X;
            float markStart = playedRow.ToLocalSpace(mark.ScreenSpaceDrawQuad.TopLeft).X;
            return titleEnd <= markStart;
        });

        AddStep("back to rest", () => setSkim(0));
        AddWaitStep("laid out", 2);
        AddAssert("the best's back where it was", () => Math.Abs(bestInFromTheRight(playedRow) - atRest) < 1);
        AddAssert("and the lanes", () => shows(playedRow, "4-7K"));
    }

    /// <summary>
    /// A song you haven't played has nothing to keep: the title runs on across the row.
    /// </summary>
    [Test]
    public void TestASongNotPlayedHasNoMark()
    {
        float widthPlayed() => descendants(playedRow).OfType<ScrollingText>().First(t => t.Text == played.Title).DrawWidth;
        float widthNotPlayed() => descendants(notPlayedRow).OfType<ScrollingText>().First(t => t.Text == notPlayed.Title).DrawWidth;

        AddStep("skimming", () => setSkim(1));
        AddWaitStep("laid out", 2);
        AddAssert("no mark", () => descendants(notPlayedRow).OfType<BestMark>().All(m => m.Alpha == 0));
        AddAssert("more room for its title than beside a mark", () => widthNotPlayed() > widthPlayed());
    }

    /// <summary>
    /// A tile keeps its mark on its cover too, and its lanes step aside.
    /// </summary>
    [Test]
    public void TestATileKeepsItsMark()
    {
        AddAssert("at rest: both", () => shows(tile, "S") && shows(tile, "4-7K"));
        AddStep("skimming", () => setSkim(1));
        AddWaitStep("laid out", 2);
        AddAssert("its best stays", () => shows(tile, "S") && shows(tile, "FC"));
        AddAssert("the lanes have stepped aside", () => !shows(tile, "4-7K"));
        AddStep("back to rest", () => setSkim(0));
        AddWaitStep("laid out", 2);
        AddAssert("the lanes are back", () => shows(tile, "4-7K"));
    }
}
