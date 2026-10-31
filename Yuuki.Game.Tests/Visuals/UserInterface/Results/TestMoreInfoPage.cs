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
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

/// <summary>
/// The whole More info page: a play just finished (its judgements in memory), a saved one judged again from its replay
/// (its graphs drawing in behind the playhead, its cards waiting under the shimmer), and one that can't be.
/// </summary>
public partial class TestMoreInfoPage : YuukiTestScene
{
    private MoreInfoPage page = null!;
    private double? watchedFrom;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("show the page", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(new YuukiScrollableContainerTestHost(page = new MoreInfoPage(ResultsTestRuns.Palette) { WatchFrom = from => watchedFrom = from }));
        });
    }

    [Test]
    public void TestAPlayJustFinished()
    {
        AddStep("a run", () =>
        {
            page.SetRun(ResultsTestRuns.Rich());
            page.SetBest(new PlayAnalysis(ResultsTestRuns.Rich(9, rough: false)));
            page.Open();
        });
        AddUntilStep("worked out", () => !page.Judging && page.Analysis != null);
        AddStep("watch from 1:00", () => page.WatchFrom!(60_000));
        AddAssert("asked for", () => watchedFrom == 60_000);
    }

    /// <summary>
    /// The graphs are worked out off the update thread: giving it a run doesn't wait for them.
    /// </summary>
    [Test]
    public void TestWorkedOutOffTheUpdateThread()
    {
        AddStep("a run", () => page.SetRun(ResultsTestRuns.Rich()));
        AddAssert("not worked out yet", () => page.Analysis == null);
        AddUntilStep("worked out", () => page.Analysis != null);
    }

    /// <summary>
    /// A saved play: its graphs draw in as its replay is judged again (slowly here, to see it), then everything's in.
    /// </summary>
    [Test]
    public void TestASavedPlayJudgedAgain()
    {
        ReplayJudge judge = null!;

        AddStep("a saved run, judged again slowly", () => judge = startJudging(60));
        AddAssert("judging", () => page.Judging);
        AddUntilStep("judged", () => !page.Judging && page.Analysis != null, 120_000);
        AddAssert("the judge is done", () => judge.IsDone);
        AddUntilStep("nothing waits now", () => descendants(page).OfType<ResultsCard>().All(card => !card.Waiting));
        AddAssert("the question's back", () => says("Where did I drift?"));
        AddAssert("no playhead", () => !descendants(page).OfType<SpriteText>().Any(t => t.Alpha > 0 && t.Parent!.Alpha > 0 && t.Text.StartsWith("re-judging", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The page while a saved play's replay is read, stopped part way to look at. The graphs have drawn in behind the
    /// playhead, which says how far it's got; the cards that need every judgement wait under their shimmer. It stays held
    /// when the steps are done: the slider sets how fast it reads on from there (0 holds it).
    /// </summary>
    [Test]
    public void TestTheLoadingState()
    {
        ReplayJudge judge = null!;

        holdPartWay(j => judge = j);

        AddAssert("part way, not done", () => judge.Progress > 0.4f && judge.Progress < 1 && !judge.IsDone && judge.JudgedNotes < judge.TotalNotes);
        AddAssert("the page is still judging", () => page.Judging);
        AddAssert("the timing card says so", () => says("Reading your replay…"));
        AddAssert("the playhead says how far", () => descendants(page).OfType<SpriteText>().Any(t => t.Alpha > 0 && t.Parent!.Alpha > 0 && t.Text.StartsWith("re-judging", StringComparison.Ordinal) && t.Text.EndsWith($"{judge.TotalNotes:N0} notes", StringComparison.Ordinal)));
        AddAssert("the whole run isn't in yet", () => page.Analysis == null);
        AddAssert("the small cards wait", () => descendants(page).OfType<ResultsCard>().Count(card => card.Waiting) == 4);
        AddAssert("the two graphs don't", () => descendants(page).OfType<ResultsCard>().Count(card => !card.Waiting) == 2);

        AddSliderStep("how fast it reads on (times the song; 0 holds it)", 0f, 60f, 0f, speed =>
        {
            if (judge != null)
                judge.SongSpeed = speed;
        });
    }

    /// <summary>
    /// Let go of, it reads on to the end, and everything comes in.
    /// </summary>
    [Test]
    public void TestLoadingFinishes()
    {
        ReplayJudge judge = null!;

        holdPartWay(j => judge = j);

        AddStep("go on", () => judge.SongSpeed = 60);
        AddUntilStep("all in", () => !page.Judging && page.Analysis != null, 120_000);
        AddAssert("the judge is done", () => judge.IsDone);
        AddUntilStep("nothing waits now", () => descendants(page).OfType<ResultsCard>().All(card => !card.Waiting));
        AddAssert("the question's back", () => says("Where did I drift?"));
    }

    // A saved run read part way, then held there: it stays where it stopped, and the graphs catch up with it.
    private void holdPartWay(Action<ReplayJudge> started)
    {
        ReplayJudge judge = null!;
        double heldAt = 0;
        int notesAtHold = 0;

        AddStep("a saved run, being read", () =>
        {
            judge = startJudging(25);
            started(judge);
        });
        AddUntilStep("part way through", () => judge.Progress > 0.4f, 120_000);
        AddStep("hold it there", () => judge.SongSpeed = 0);
        AddWaitStep("it stops, and the graphs catch up", 400);
        AddStep("note where it stopped", () =>
        {
            heldAt = judge.JudgedTime;
            notesAtHold = judge.JudgedNotes;
        });
        AddWaitStep("look at it", 200);
        AddAssert("it's held", () => judge.JudgedTime == heldAt && judge.JudgedNotes == notesAtHold && !judge.IsDone);
    }

    // A saved run, its totals as saved, having its replay read at this speed.
    private ReplayJudge startJudging(double speed)
    {
        PlayableBeatmap chart = ChartGenerator.Generate(160, 90_000, seed: 4);
        var played = new ReplayData { ChartHash = ReplayData.ChartHashOf(chart), Inputs = perfect(chart) };
        var saved = new GameplayResult { Score = 1_000_000, CriticalCount = chart.Notes.Length, MaxCombo = chart.Notes.Length, ChartLength = chart.LastNoteTime };

        page.ShowSong(null, new BeatmapSet { Title = "A song being read", Artist = "Someone" });
        page.SetRun(saved);

        var judge = new ReplayJudge(chart, played) { StartTime = 0, SongSpeed = speed };
        Add(judge);
        judge.Completed += () => page.SetRun(saved.WithJudgements(judge.Judgements, judge.HoldBreakTimes, chart.LaneCount, chart.LastNoteTime));
        page.JudgeFrom(judge);
        page.Open();
        return judge;
    }

    private bool says(string text) => descendants(page).OfType<SpriteText>().Any(t => t.Text == text && t.Alpha > 0);

    [Test]
    public void TestOneThatCantBe()
    {
        AddStep("a saved run without a replay", () =>
        {
            page.SetRun(new GameplayResult { Score = 900_000, CriticalCount = 300, BreakCount = 20 });
            page.ShowUnavailable("This play was saved without a replay, so only its totals are kept.");
            page.Open();
        });
        AddAssert("nothing to show", () => page.Analysis == null && !page.Judging);
        AddAssert("and nothing to zoom", () => descendants(page).OfType<ResultsChip>().Where(c => c.Text == "+").All(c => c.Parent!.Parent!.Alpha == 0));
    }

    // Every note pressed on time.
    private static List<ReplayInput> perfect(PlayableBeatmap chart)
    {
        var inputs = new List<ReplayInput>();

        foreach (BeatmapNote note in chart.Notes)
        {
            int lane = (int)note.Lane;

            switch (note.Type)
            {
                case NoteType.Hold:
                    inputs.Add(new ReplayInput(note.StartTime, ReplayInputKind.LanePress, lane));
                    inputs.Add(new ReplayInput(note.EndTime, ReplayInputKind.LaneRelease, lane));
                    break;

                case NoteType.Swipe:
                    inputs.Add(new ReplayInput(note.StartTime, note.Direction == SwipeDirection.Right ? ReplayInputKind.SwipeRight : ReplayInputKind.SwipeLeft));
                    break;

                default:
                    inputs.Add(new ReplayInput(note.StartTime, ReplayInputKind.LanePress, lane));
                    inputs.Add(new ReplayInput(note.StartTime + 30, ReplayInputKind.LaneRelease, lane));
                    break;
            }
        }

        return inputs.OrderBy(i => i.Time).ToList();
    }

    /// <summary>
    /// The timing graph takes the height the rest of the page leaves, so the page just fills the room it's in, down to a
    /// height it stays readable at (and the page scrolls), and up to one past which it's no more use. What's plotted moves
    /// with it: the timing gets it all, and the labels stay on what they label.
    /// </summary>
    [Test]
    public void TestTheTimingGraphTakesTheRoom()
    {
        AddStep("a run", () =>
        {
            page.SetRun(ResultsTestRuns.Rich());
            page.Open();
        });
        AddUntilStep("worked out", () => page.Analysis != null);
        AddSliderStep("room", 400f, 1600f, 946f, room => page.ViewHeight = room);

        AddStep("the room on a 1080p screen", () => page.ViewHeight = 946);
        AddWaitStep("laid out", 2);
        AddAssert("the page just fills it", () => near(page.DrawHeight, 946));
        AddAssert("the graph's what's grown", () => page.Scatter.Height > TimingScatter.HEIGHT);
        AddAssert("the card holds it", () => near(page.Scatter.DrawHeight, 946 - 526 - 64));
        AddAssert("the labels stay on the timing's foot", () => near(labelAt("early ↓"), page.Scatter.DrawHeight - 64));
        AddAssert("on time stays in the middle of the timing", () => near(labelAt("0"), (34 + page.Scatter.DrawHeight - 54) / 2));
        AddAssert("the notes a second stay under it", () => near(labelAt("notes/s"), page.Scatter.DrawHeight - 35));
        AddAssert("and the time under it all", () => near(labelAt("0:00"), page.Scatter.DrawHeight - 14));

        AddStep("the room, part way through a pixel", () => page.ViewHeight = 946.5f);
        AddWaitStep("laid out", 2);
        AddAssert("nothing left to scroll", () => page.DrawHeight <= 946.5f);

        AddStep("the room in a short window", () => page.ViewHeight = 588);
        AddWaitStep("laid out", 2);
        AddAssert("the graph stays readable", () => near(page.Scatter.DrawHeight, TimingScatter.HEIGHT));
        AddAssert("and the page scrolls", () => page.DrawHeight > 588);
        AddAssert("the labels come back with it", () => near(labelAt("early ↓"), TimingScatter.HEIGHT - 64));

        AddStep("no end of room", () => page.ViewHeight = 5000);
        AddWaitStep("laid out", 2);
        AddAssert("it stops growing", () => near(page.Scatter.DrawHeight, 800 - 64));
        AddAssert("and leaves the rest empty", () => page.DrawHeight < 5000);
    }

    /// <summary>
    /// What's offered for the offsets has a strip of its own, there only while there's something in it. The timing graph
    /// gives up the height to keep the whole page on the screen; past what it can give up, the page scrolls, and it's as
    /// easy to read as ever.
    /// </summary>
    [Test]
    public void TestWhatsOfferedHasAStrip()
    {
        Box offered = null!;
        float graphWithout = 0, heightWithout = 0;

        AddStep("a run in the room on a 1080p screen", () =>
        {
            page.SetRun(ResultsTestRuns.Rich());
            page.ViewHeight = 946;
            page.Open();
        });
        AddUntilStep("worked out", () => page.Analysis != null);
        AddWaitStep("laid out", 2);
        AddAssert("no strip", () => page.AdviceStrip == null);
        AddStep("note the heights", () =>
        {
            graphWithout = page.Scatter.DrawHeight;
            heightWithout = page.DrawHeight;
        });
        AddAssert("the row and the rest fill the room", () => near(heightWithout, 946));

        AddStep("offer a line and a button's worth", () => page.SetAdvice(new Drawable[] { offered = new Box { RelativeSizeAxes = Axes.X, Height = 60 } }));
        AddWaitStep("laid out", 2);
        AddAssert("the strip's in the page", () => page.AdviceStrip?.Parent == page);
        AddAssert("what's offered is in it", () => isIn(offered, page.AdviceStrip!));
        AddAssert("it holds it, with room round it", () => near(page.AdviceStrip!.DrawHeight, 60 + 24));
        AddAssert("the graph gave up the height", () => near(page.Scatter.DrawHeight, graphWithout - 60 - 24 - 8));
        AddAssert("and it all fits", () => near(page.DrawHeight, 946));
        AddAssert("the strip's at the foot of the page", () => page.Children[^1] == page.AdviceStrip);
        AddStep("open the page again", () => page.Open());
        AddAssert("it comes in with the rest", () => page.AdviceStrip!.Alpha < 1);
        AddUntilStep("and it's in", () => page.AdviceStrip!.Alpha == 1);

        AddStep("offer a lot", () => page.SetAdvice(new Drawable[] { offered = new Box { RelativeSizeAxes = Axes.X, Height = 400 } }));
        AddWaitStep("laid out", 2);
        AddAssert("still all in it", () => isIn(offered, page.AdviceStrip!));
        AddAssert("the graph's easy to read", () => near(page.Scatter.DrawHeight, TimingScatter.HEIGHT));
        AddAssert("and the page scrolls", () => page.DrawHeight > 946);

        AddStep("offer nothing", () => page.SetAdvice(Array.Empty<Drawable>()));
        AddWaitStep("laid out", 2);
        AddAssert("no strip again", () => page.AdviceStrip == null);
        AddAssert("and the graph's back", () => near(page.Scatter.DrawHeight, graphWithout));
        AddAssert("and the page", () => near(page.DrawHeight, heightWithout));

        AddStep("offer it again", () => page.SetAdvice(new Drawable[] { offered = new Box { RelativeSizeAxes = Axes.X, Height = 60 } }));
        AddWaitStep("laid out", 2);
        AddAssert("back in the page", () => page.AdviceStrip?.Parent == page);
    }

    /// <summary>
    /// The lanes' chips: all on with none picked, as all are showing. Pick one and the rest fade, as a filter turned off does.
    /// </summary>
    [Test]
    public void TestPickingLanes()
    {
        ResultsChip chip(string lane) => descendants(page).OfType<ResultsChip>().Single(c => c.Text == lane);

        AddStep("a run", () =>
        {
            page.SetRun(ResultsTestRuns.Rich());
            page.Open();
        });
        AddUntilStep("worked out", () => page.Analysis != null);
        AddWaitStep("its chips", 2);
        AddAssert("none picked, all on", () => new[] { "1", "2", "3", "4" }.All(lane => chip(lane).LooksOn) && page.Scatter.Lanes.Count == 0);

        AddStep("pick lane 1", () => chip("1").Action!());
        AddAssert("it shows", () => page.Scatter.Lanes.SetEquals(new[] { 0 }));
        AddAssert("it stays on", () => chip("1").LooksOn);
        AddAssert("the rest fade", () => new[] { "2", "3", "4" }.All(lane => !chip(lane).LooksOn));

        AddStep("pick lane 3 too", () => chip("3").Action!());
        AddAssert("both on", () => chip("1").LooksOn && chip("3").LooksOn && !chip("2").LooksOn && !chip("4").LooksOn);

        AddStep("unpick lane 1", () => chip("1").Action!());
        AddAssert("it fades, with lane 3 still picked", () => !chip("1").LooksOn && chip("3").LooksOn);

        AddStep("unpick lane 3", () => chip("3").Action!());
        AddAssert("none picked, all on again", () => new[] { "1", "2", "3", "4" }.All(lane => chip(lane).LooksOn) && page.Scatter.Lanes.Count == 0);
    }

    /// <summary>
    /// The zoom's buttons: the two graphs stay lined up, what can't be done looks off, and a bare page says how to move along.
    /// </summary>
    [Test]
    public void TestZoomingTheGraphs()
    {
        ResultsChip chip(string text) => descendants(page).OfType<ResultsChip>().Single(c => c.Text == text);
        AddStep("a run", () =>
        {
            page.SetRun(ResultsTestRuns.Rich());
            page.Open();
        });
        AddUntilStep("worked out", () => page.Analysis != null && !page.Judging);
        AddWaitStep("laid out", 2);

        AddAssert("the two graphs share a view", () => page.Scatter.View == page.View && page.Health.View == page.View);
        AddAssert("all of the song", () => page.View.Zoom == 1 && says("1×"));
        AddAssert("nothing to zoom out from", () => !chip("−").LooksOn && chip("+").LooksOn);
        AddAssert("nowhere to move to, but the buttons stay where they are", () => chip("◀").Alpha == 1 && chip("▶").Alpha == 1 && !chip("◀").LooksOn && !chip("▶").LooksOn);
        AddAssert("the question", () => says("Where did I drift?"));

        AddStep("zoom in", () => chip("+").Action!());
        AddWaitStep("laid out", 2);
        AddAssert("twice over", () => page.View.Zoom == 2 && says("2×"));
        AddAssert("can go either way", () => chip("−").LooksOn && chip("+").LooksOn);
        AddAssert("and move along", () => chip("◀").LooksOn && chip("▶").LooksOn);
        AddAssert("says how", () => says("Drag to move along the song"));

        AddStep("zoom in again", () => chip("+").Action!());
        AddWaitStep("laid out", 2);
        AddAssert("four times over, and no further", () => page.View.Zoom == 4 && says("4×") && !chip("+").LooksOn);
        AddStep("try anyway", () => chip("+").Action!());
        AddAssert("no further", () => page.View.Zoom == 4);

        AddStep("move on to the end", () =>
        {
            for (int i = 0; i < 10; i++)
                chip("▶").Action!();
        });
        AddWaitStep("laid out", 2);
        AddAssert("at the end", () => !page.View.CanMoveOn && !chip("▶").LooksOn && chip("◀").LooksOn);

        AddStep("zoom right out", () =>
        {
            chip("−").Action!();
            chip("−").Action!();
        });
        AddWaitStep("laid out", 2);
        AddAssert("all of the song again", () => page.View.Zoom == 1 && page.View.Start == 0 && !chip("−").LooksOn);
        AddAssert("nowhere to move to, again, and still there", () => chip("◀").Alpha == 1 && !chip("◀").LooksOn && !chip("▶").LooksOn);
        AddAssert("the question again", () => says("Where did I drift?"));
    }

    /// <summary>
    /// The card's question gives way where its controls need the room, and comes back where they don't.
    /// </summary>
    [Test]
    public void TestTheQuestionGivesWayToTheControls()
    {
        Container holder = null!;
        ResultsCard card = null!;
        SpriteText question() => descendants(card).OfType<SpriteText>().Single(t => t.Text == "What's the question?");

        AddStep("a card with controls", () =>
        {
            Add(holder = new Container
            {
                Width = 1200,
                AutoSizeAxes = Axes.Y,
                Child = card = new ResultsCard("A title", "What's the question?", ResultsTestRuns.Palette)
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 100
                }
            });
            card.Controls.Add(new Container { Size = new Vector2(600, 30) });
        });
        AddWaitStep("laid out", 2);
        AddAssert("room for it", () => question().Alpha == 1);
        AddStep("narrow it", () => holder.Width = 640);
        AddWaitStep("laid out", 2);
        AddAssert("no room", () => question().Alpha == 0);
        AddWaitStep("settled", 3);
        AddAssert("and it doesn't flicker back", () => question().Alpha == 0);
        AddStep("widen it", () => holder.Width = 1200);
        AddWaitStep("laid out", 2);
        AddAssert("back", () => question().Alpha == 1);
    }

    /// <summary>
    /// Comparing with the best is off to begin with, and looks it, as a filter turned off does.
    /// </summary>
    [Test]
    public void TestComparingLooksOffUntilOn()
    {
        ResultsChip compare() => descendants(page).OfType<ResultsChip>().Single(c => c.Text == "compare with your best");

        AddStep("a run and a best", () =>
        {
            page.SetRun(ResultsTestRuns.Rich());
            page.SetBest(new PlayAnalysis(ResultsTestRuns.Rich(9, rough: false)));
            page.Open();
        });
        AddUntilStep("worked out", () => page.Analysis != null);
        AddAssert("looks off", () => !compare().LooksOn && !page.Scatter.Compare);
        AddStep("turn it on", () => compare().Action!());
        AddAssert("looks on", () => compare().LooksOn && page.Scatter.Compare);
    }

    /// <summary>
    /// Every card fits what it holds in the row, a run with something in each.
    /// </summary>
    [Test]
    public void TestTheRowHoldsWhatItsCardsDo()
    {
        AddStep("a run", () =>
        {
            page.SetRun(ResultsTestRuns.Rich());
            page.Open();
        });
        AddUntilStep("worked out", () => page.Analysis != null);
        AddWaitStep("drawn in", 5);
        AddAssert("nothing past the foot of its card", () =>
        {
            foreach (ResultsCard card in descendants(page).OfType<ResultsCard>())
            {
                foreach (Drawable held in card.Body.Children)
                {
                    if (!isIn(held, card))
                        return false;
                }
            }

            return true;
        });
    }

    private static bool isIn(Drawable held, Drawable holder)
    {
        Vector2 bottom = holder.ToLocalSpace(held.ToScreenSpace(new Vector2(0, held.DrawHeight)));
        return bottom.Y <= holder.DrawHeight;
    }

    private static bool near(float a, float b) => Math.Abs(a - b) <= 1;

    // Where a label of the timing graph is, down it.
    private float labelAt(string text) => descendants(page.Scatter).OfType<SpriteText>().First(t => t.Text == text).Y;

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

    /// <summary>
    /// The page in a scroll, as the score screen holds it.
    /// </summary>
    private partial class YuukiScrollableContainerTestHost : Container
    {
        public YuukiScrollableContainerTestHost(MoreInfoPage page)
        {
            RelativeSizeAxes = Axes.Both;
            Padding = new MarginPadding { Left = 48, Right = 48, Top = 34 };
            Child = new Yuuki.Game.Graphics.Containers.YuukiScrollableContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = page
            };
        }
    }
}
