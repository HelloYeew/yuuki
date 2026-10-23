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
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// A song's tile in the strip shows, at the foot of its cover, your best mark on it and its lanes, as its row
/// in the rail does beside its title.
/// </summary>
public partial class TestSongTile : YuukiTestScene
{
    [Cached]
    private readonly BeatmapsCollection beatmapsCollection = new BeatmapsCollection();

    [Cached]
    private readonly CoverColorStore coverColors = new CoverColorStore();

    [Cached]
    private readonly ScoreStore scores = new ScoreStore(null);

    private BeatmapSet set = null!;
    private BeatmapSetCarouselCard tile = null!;

    // The score store lasts the whole scene: each test's song is a new one to it.
    private int lastSetId = 1000;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("a song with a 4- and a 7-lane difficulty, in the strip", () =>
        {
            Clear();

            set = BeatmapTestUtility.CreateDummyBeatmapSets(1)[0];
            set.ID = ++lastSetId;

            beatmapsCollection.BeatmapSets = new List<BeatmapSet> { set };
            beatmapsCollection.Beatmaps = new List<Beatmap>
            {
                new Beatmap { ID = 1, BeatmapSet = set, DifficultyName = "Four", DifficultyRating = 3, LaneCount = 4 },
                new Beatmap { ID = 2, BeatmapSet = set, DifficultyName = "Seven", DifficultyRating = 6, LaneCount = 7 }
            };

            Add(new Container
            {
                Size = new Vector2(200, 240),
                Child = tile = new BeatmapSetCarouselCard { Orientation = new Reactive<CarouselOrientation>(CarouselOrientation.Horizontal) }
            });
        });
        AddUntilStep("loaded", () => tile.IsLoaded);
    }

    [Test]
    public void TestAPlayedSongShowsYourBestAndItsLanes()
    {
        AddStep("a full combo on it", () => scores.Add(new ScoreInfo
        {
            BeatmapId = 1,
            BeatmapSetId = set.ID,
            Score = 950_000,
            Accuracy = 0.97,
            Rank = ScoreRank.S,
            IsFullCombo = true,
            Date = DateTimeOffset.Now
        }));
        AddStep("show the song", () => tile.Apply(set, 0));
        AddAssert("your best on the cover", () => shown("S") && shown("FC"));
        AddAssert("and its lanes", () => shown("4-7K"));
    }

    [Test]
    public void TestASongNotPlayedYetShowsItsLanes()
    {
        AddStep("show the song", () => tile.Apply(set, 0));
        AddAssert("no best", () => !shown("S") && !shown("FC"));
        AddAssert("its lanes", () => shown("4-7K"));
    }

    // Whether a text saying exactly this shows on the tile: its own fades, not the scene's.
    private bool shown(string text) => descendants(tile).OfType<SpriteText>().Any(t =>
    {
        if (t.Text != text)
            return false;

        float alpha = 1;

        for (Drawable? d = t; d != null && d != tile; d = d.Parent)
            alpha *= d.Alpha;

        return alpha > 0.01f;
    });

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
