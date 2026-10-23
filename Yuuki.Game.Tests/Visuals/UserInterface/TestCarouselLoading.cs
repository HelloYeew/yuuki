// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// How a song list fills in and moves: after a jump, the rows at its ends come in soon after it lands, in
/// place and with their covers; the rail's rows run on to its ends; and the window clicks over one song at
/// a time.
/// </summary>
public partial class TestCarouselLoading : YuukiTestScene
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
        AddUntilStep("at rest", () => songs.IsLoaded && songs.Motion.Skim == 0 && songs.CardOffset(0) != null);
    }

    /// <summary>
    /// While the list flies, a row far from the middle waits longer to be built (so a long jump doesn't
    /// build every card it passes); once the list lands, every row on screen is built promptly. Made
    /// stark here with a long per-slot wait: a row five out would otherwise wait two seconds.
    /// </summary>
    [Test]
    public void TestAJumpFillsInOnceItLands()
    {
        AddStep("a long wait per slot while flying", () => songs.LoadDelayPerSlot = 400);
        AddStep("jump to the 30th", () => songs.SetIndex(30));
        AddUntilStep("landed", () => Math.Abs(songs.ScrollPosition - 30) < 0.01);
        AddWaitStep("a moment", 400);
        AddAssert("the rows at the ends are built", () => songs.CardOffset(25) != null && songs.CardOffset(35) != null);
    }

    /// <summary>
    /// Scrolling fast (a held key, a spun wheel), the rows coming into view are already there: every row
    /// clearly on show (past the fade at the rail's ends, and not under the window) has its card in, every
    /// frame.
    /// </summary>
    [Test]
    public void TestAFastScrollShowsItsRows()
    {
        int gaps = 0;

        AddStep("the list's entrance", () => songs.AnimateEntrance());
        AddWaitStep("the entrance is over", 1000);
        AddStep("watch the rows every frame", () =>
        {
            gaps = 0;
            Add(new EveryFrame(() => gaps += rowsOnShowWithoutTheirCard()));
        });
        AddRepeatStep("hold the key down", () => songs.Next(), 20);
        AddUntilStep("landed", () => Math.Abs(songs.ScrollPosition - 20) < 0.001);
        AddAssert("no gaps on the way", () => gaps == 0);
    }

    /// <summary>
    /// However long a fast scroll goes on, the cards it leaves behind go back to the pool as they go out of
    /// sight, so it never needs more cards than the pool keeps (more are made new and thrown away).
    /// </summary>
    [Test]
    public void TestAFastScrollKeepsToThePool()
    {
        int mostBuilt = 0;

        AddStep("a long list", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(400);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(s => BeatmapTestUtility.CreateDummyBeatmaps(s, 2)).ToList();
            songs.SetItems(beatmapsCollection.BeatmapSets);
            songs.AnimateEntrance();
        });
        AddWaitStep("the entrance is over", 1000);
        AddStep("count the cards every frame", () => Add(new EveryFrame(() => mostBuilt = Math.Max(mostBuilt, songs.CardsBuilt))));
        AddRepeatStep("hold the key down", () => songs.Next(), 100);
        AddUntilStep("landed", () => Math.Abs(songs.ScrollPosition - 100) < 0.001);
        AddAssert("never more than the pool keeps", () => mostBuilt <= BeatmapSetCarousel.PoolSize);
    }

    /// <summary>
    /// A long jump (a click on the scrubber, say) is too fast to build the cards it passes, but builds
    /// where it's going as it sets off, so the list arrives with its rows in.
    /// </summary>
    [Test]
    public void TestAJumpLandsWithItsRows()
    {
        AddStep("a long list", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(400);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(s => BeatmapTestUtility.CreateDummyBeatmaps(s, 2)).ToList();
            songs.SetItems(beatmapsCollection.BeatmapSets);
            songs.AnimateEntrance();
        });
        AddWaitStep("the entrance is over", 1000);
        AddStep("jump to the 350th", () => songs.SetIndex(350));
        AddUntilStep("arriving", () => Math.Abs(songs.ScrollPosition - 350) < 2);
        AddAssert("its rows are in", () => rowsOnShowWithoutTheirCard(0.5f) == 0);
    }

    private int rowsOnShowWithoutTheirCard(float shown = 0.9f)
    {
        int missing = 0;
        int around = (int)Math.Round(songs.ScrollPosition);

        for (int i = Math.Max(0, around - 12); i <= Math.Min(songs.Count - 1, around + 12); i++)
        {
            float y = songs.SlotCentre(i).Y;
            bool onShow = y > 48 + 32 && y < songs.DrawHeight - 64 - 32 && Math.Abs(i - songs.ScrollPosition) >= 1;

            if (onShow && !(songs.CardAlpha(i) > shown))
                missing++;
        }

        return missing;
    }

    /// <summary>
    /// Once the list's entrance is over, a card built as it scrolls into view fades in about where it is
    /// (drifting a few pixels out from the window), rather than sliding in from the side as the entrance's
    /// cards do.
    /// </summary>
    [Test]
    public void TestARowBuiltWhileBrowsingDoesntSlideIn()
    {
        float furthest = 0;

        AddStep("the list's entrance", () => songs.AnimateEntrance());
        AddWaitStep("the entrance is over", 1000);
        AddStep("jump to the 30th", () =>
        {
            furthest = 0;
            songs.SetIndex(30);
        });
        AddUntilStep("the 32nd has come in", () =>
        {
            if (songs.CardOffset(32) is Vector2 offset)
                furthest = Math.Max(furthest, MathF.Sqrt(offset.X * offset.X + offset.Y * offset.Y));

            return songs.CardAlpha(32) > 0.99f;
        });
        AddAssert("about in its slot all the way", () => furthest <= songs.RevealDrift + 0.01f);
    }

    /// <summary>
    /// A card that has loaded waits, hidden, for its cover, so it comes in whole rather than as a
    /// placeholder its cover fades over a moment later.
    /// </summary>
    [Test]
    public void TestARowWaitsForItsCover()
    {
        SlowCoverCarousel list = null!;

        AddStep("a list whose covers are slow", () => list = addSlowList(revealWait: 60_000));
        AddUntilStep("a row built", () => list.CardAlpha(2) != null);
        AddWaitStep("a moment", 300);
        AddAssert("still hidden", () => list.CardAlpha(2) == 0);

        AddStep("its cover's in", () => SlowCoverCard.CoversIn = true);
        AddUntilStep("it comes in", () => list.CardAlpha(2) > 0.99f);
    }

    /// <summary>
    /// It doesn't wait for long, though: a cover that's slow to decode comes in over a row already there.
    /// </summary>
    [Test]
    public void TestARowComesInWithoutASlowCover()
    {
        SlowCoverCarousel list = null!;

        AddStep("a list whose covers are slow", () => list = addSlowList(revealWait: 150));
        AddUntilStep("the rows come in regardless", () => list.CardAlpha(2) > 0.99f);
    }

    /// <summary>
    /// The window clicks over from one song to the next: the one leaving is all but gone before the next
    /// one comes in, so no frame shows two songs (two titles) at once. Recorded every frame, for a step on
    /// its own and steps a few a second, as quick clicks are.
    /// </summary>
    [Test]
    public void TestTheWindowShowsOneSongAtATime()
    {
        float overlap = 0;

        AddStep("watch the window every frame", () =>
        {
            overlap = 0;
            Add(new EveryFrame(() => overlap = Math.Max(overlap, secondMostOpaqueFace())));
        });

        AddStep("one step on", () => songs.Next());
        AddUntilStep("landed", () => Math.Abs(songs.ScrollPosition - 1) < 0.001);
        AddWaitStep("the swap's over", 600);
        AddAssert("the next song shows", () => songs.FocusWindow!.ShownItem == songs.ItemAt(1) && mostOpaqueFace() > 0.999f);

        for (int i = 2; i <= 4; i++)
        {
            AddStep("another step on", () => songs.Next());
            AddWaitStep("a quarter of a second", 250);
        }

        AddUntilStep("landed", () => Math.Abs(songs.ScrollPosition - 4) < 0.001);
        AddWaitStep("the swap's over", 600);
        AddAssert("that song shows", () => songs.FocusWindow!.ShownItem == songs.ItemAt(4) && mostOpaqueFace() > 0.999f);
        AddAssert("never two at once", () => overlap < 0.2f);
    }

    /// <summary>
    /// Flying through the list (a long jump), the window shows the songs it passes, each in full, rather
    /// than faces that never get the time to come in.
    /// </summary>
    [Test]
    public void TestTheWindowShowsWhatAJumpPasses()
    {
        float faintest = 1;

        AddStep("watch the window every frame", () =>
        {
            faintest = 1;
            Add(new EveryFrame(() => faintest = Math.Min(faintest, mostOpaqueFace())));
        });
        AddStep("jump to the 30th", () => songs.SetIndex(30));
        AddUntilStep("landed", () => Math.Abs(songs.ScrollPosition - 30) < 0.001);
        AddAssert("never empty on the way", () => faintest > 0.5f);
    }

    private IEnumerable<float> faceAlphas() =>
        descendants(songs.FocusWindow!).OfType<CarouselFocusFace<BeatmapSet>>().Select(f => f.Alpha).OrderByDescending(a => a);

    private float mostOpaqueFace() => faceAlphas().FirstOrDefault();

    private float secondMostOpaqueFace() => faceAlphas().Skip(1).FirstOrDefault();

    private SlowCoverCarousel addSlowList(double revealWait)
    {
        SlowCoverCard.CoversIn = false;
        Clear();

        SlowCoverCarousel list;

        Add(new Container
        {
            Size = new Vector2(600, 700),
            Child = list = new SlowCoverCarousel
            {
                RevealWait = revealWait,
                PlayLoadEntrance = true
            }
        });
        list.Orientation.Value = CarouselOrientation.Vertical;
        list.SetItems(Enumerable.Range(0, 40).Select(i => $"song {i}").ToList());
        return list;
    }

    /// <summary>
    /// The rail's cards are masked, fading out at its top and bottom, so they run on to its ends.
    /// </summary>
    [Test]
    public void TestTheRailsEndsFade() => AddAssert("the cards fade at the ends", () =>
        descendants(songs).OfType<Container>().Any(c => c.Masking && c.MaskingFade.Top > 0 && c.MaskingFade.Bottom > 0));

    private partial class SlowCoverCarousel : Carousel<string, SlowCoverCard>
    {
    }

    /// <summary>
    /// A card whose cover is in only once a test says so.
    /// </summary>
    private partial class SlowCoverCard : CarouselCard<string>
    {
        public static bool CoversIn;

        public override bool IsReady => CoversIn;

        public override void Apply(string model, int index)
        {
        }
    }

    private partial class EveryFrame : Container
    {
        private readonly Action tick;

        public EveryFrame(Action tick)
        {
            this.tick = tick;
        }

        public override void Update()
        {
            base.Update();
            tick();
        }
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
}
