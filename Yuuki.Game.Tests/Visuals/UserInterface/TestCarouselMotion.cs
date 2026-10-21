// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// How a song list moves: skim mode when it moves fast, and the look-ahead while it moves at all.
/// </summary>
public partial class TestCarouselMotion : YuukiTestScene
{
    [Cached]
    private readonly BeatmapsCollection beatmapsCollection = new BeatmapsCollection();

    [Cached]
    private readonly CoverColorStore coverColors = new CoverColorStore();

    private BeatmapSetCarousel songs = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("a rail of forty songs", () =>
        {
            Clear();

            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(40);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(s => BeatmapTestUtility.CreateDummyBeatmaps(s, 2)).ToList();

            Add(new Container
            {
                Size = new Vector2(600, 700),
                Child = songs = new BeatmapSetCarousel()
            });
            songs.Orientation.Value = CarouselOrientation.Vertical;
            songs.SetItems(beatmapsCollection.BeatmapSets);
        });
        AddUntilStep("at rest", () => songs.IsLoaded && songs.Motion.Skim == 0 && songs.FocusWindowRect.Height == 140);
    }

    /// <summary>
    /// Stepping faster than about seven songs a second compacts the list (the window too), and it eases
    /// back out once the stepping stops.
    /// </summary>
    [Test]
    public void TestFastSteppingSkims()
    {
        AddStep("ten steps at once", () =>
        {
            for (int i = 0; i < 10; i++)
                songs.Next();
        });
        AddUntilStep("it skims", () => songs.Motion.Skim > 0.9f);
        AddAssert("the window compacts", () => songs.FocusWindowRect.Height < 100);
        AddUntilStep("back to rest once it stops", () => songs.Motion.Skim == 0 && songs.FocusWindowRect.Height == 140);
    }

    [Test]
    public void TestASlowStepDoesntSkim()
    {
        AddStep("one step", () => songs.Next());
        AddWaitStep("let it settle", 600);
        AddAssert("no skim", () => songs.Motion.Skim == 0);
    }

    /// <summary>
    /// Putting the list somewhere (a search, a song picked elsewhere) isn't moving it: no skim, no lead.
    /// </summary>
    [Test]
    public void TestPuttingTheListSomewhereIsntMoving()
    {
        AddStep("put it at the 30th", () => songs.SetIndex(30, animate: false));
        AddWaitStep("a moment", 300);
        AddAssert("no skim, no lead", () => songs.Motion.Skim == 0 && songs.Motion.Lead == 0);
    }

    /// <summary>
    /// Moving on down, the window leads upward, so more of what's coming shows below it; at rest it's back
    /// in the middle.
    /// </summary>
    [Test]
    public void TestTheWindowLeadsWhileMoving()
    {
        AddStep("head for the 30th", () => songs.SetIndex(30));
        AddUntilStep("it leads", () => songs.Motion.Lead > 0.2f && songs.FocusWindowRect.Center.Y < songs.DrawHeight / 2 - 5);
        AddUntilStep("back in the middle at rest", () => songs.Motion.Lead == 0 && songs.CurrentIndex == 30
                                                         && System.Math.Abs(songs.FocusWindowRect.Center.Y - songs.DrawHeight / 2) < 0.5f);
    }

    [Test]
    public void TestADifficultyListDoesntSkim()
    {
        BeatmapCarousel difficulties = null!;

        AddStep("a difficulty list of twenty", () =>
        {
            var set = beatmapsCollection.BeatmapSets[0];

            Add(new Container
            {
                Size = new Vector2(600, 700),
                Child = difficulties = new BeatmapCarousel()
            });
            difficulties.Orientation.Value = CarouselOrientation.Vertical;
            difficulties.SetItems(BeatmapTestUtility.CreateDummyBeatmaps(set, 20));
        });
        AddUntilStep("loaded", () => difficulties.IsLoaded);
        AddStep("ten steps at once", () =>
        {
            for (int i = 0; i < 10; i++)
                difficulties.Next();
        });
        AddWaitStep("let it move", 300);
        AddAssert("no skim", () => difficulties.Motion.Skim == 0);
    }
}
