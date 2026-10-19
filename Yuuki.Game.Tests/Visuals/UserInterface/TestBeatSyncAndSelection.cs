// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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
    public void TestPickedDifficultyWearsTheRing()
    {
        Reactive<CarouselOrientation> orientation = null!;
        BeatmapCard fourCard = null!, sevenCard = null!;

        AddStep("two difficulty cards in a list", () =>
        {
            orientation = new Reactive<CarouselOrientation>(CarouselOrientation.Vertical);

            fourCard = new BeatmapCard { Orientation = orientation };
            fourCard.Apply(four, 0);
            sevenCard = new BeatmapCard { Orientation = orientation };
            sevenCard.Apply(seven, 1);

            Add(new Container
            {
                Size = new Vector2(400, 200),
                Children = new Drawable[] { fourCard, sevenCard }
            });
        });

        AddAssert("the picked one has it", () => fourCard.IsSelectedDifficulty && !sevenCard.IsSelectedDifficulty);
        AddStep("pick the other", () => workingBeatmap.Beatmap = seven);
        AddAssert("the ring moves", () => !fourCard.IsSelectedDifficulty && sevenCard.IsSelectedDifficulty);
        AddStep("as a cover flow", () => orientation.Value = CarouselOrientation.Horizontal);
        AddAssert("none there, as for songs", () => !fourCard.IsSelectedDifficulty && !sevenCard.IsSelectedDifficulty);
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

        AddStep("as a cover flow", () => orientation.Value = CarouselOrientation.Horizontal);
        AddStep("show the song again", () => setCard.Apply(set, 0));
        AddAssert("the cover's corner has it too", () => texts(setCard).Count(t => t == "4-7K") == 2);
        AddAssert("and the difficulties' corners", () => texts(fourCard).Count(t => t == "4K") == 2);
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
