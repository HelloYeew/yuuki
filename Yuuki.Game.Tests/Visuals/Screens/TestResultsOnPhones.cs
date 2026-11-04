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
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Results;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The results screen on phones' canvases, as their screens give them (inside the safe area, less the phone footer's 68),
/// and on desktop's: where the column's too narrow for the score's statistics in a line, they go beside it as rows, and
/// under it where it's narrower still, never over it. On an iPhone Pro Max the whole column fits without scrolling.
/// </summary>
public partial class TestResultsOnPhones : TestScene
{
    private const float phone_footer = 68;
    private const float desktop_footer = 100;

    // An iPhone Pro Max's, as one reported the results on it, and at UI scale 1.05; an iPhone's (not Max); a 16:9 phone's;
    // desktop's, and at UI scale 1.4.
    private static readonly Vector2 pro_max = new Vector2(1430, 720);
    private static readonly Vector2 pro_max_larger = new Vector2(1362, 686);
    private static readonly Vector2 desktop_larger = new Vector2(1371, 771);
    private static readonly Vector2 iphone = new Vector2(1417, 720);
    private static readonly Vector2 sixteen_nine = new Vector2(1280, 720);
    private static readonly Vector2 desktop = new Vector2(1920, 1080);

    [Cached]
    private readonly GameLayout gameLayout = new GameLayout();

    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();
    private readonly ScoreStore scoreStore = new ScoreStore(null);

    private readonly Beatmap beatmap = new Beatmap
    {
        ID = 71,
        BeatmapSet = new BeatmapTestUtility().ThemeSet,
        DifficultyName = "Easy",
        Creator = "danar",
        DifficultyRating = 1.8,
        LaneCount = 4
    };

    private Container canvas = null!;
    private YuukiScreenStack stack = null!;
    private Box footer = null!;
    private ScoreScreen? shown;

    [BackgroundDependencyLoader]
    private void load()
    {
        gameLayout.Current.Value = DisplayLayout.Phone;

        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(config);
        workingBeatmap.BeatmapSet = beatmap.BeatmapSet;
        workingBeatmap.Beatmap = beatmap;
        Cache(workingBeatmap);
        Cache(scoreStore);

        // Built once for the fixture and re-added by each test's SetUp, so removal mustn't dispose it.
        canvas = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = pro_max,
            DisposeOnRemoval = false,
            Children = new Drawable[]
            {
                stack = new YuukiScreenStack
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Bottom = phone_footer }
                },
                footer = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = phone_footer,
                    Color = ColorExtensions.FromHex("#3A3F55")
                }
            }
        };
        Cache(stack);
    }

    [SetUp]
    public void SetUp() => AddStep("add the canvas", () => Add(canvas));

    public override void Update()
    {
        base.Update();

        // Whole in the window, whatever its size.
        if (canvas.Parent != null && DrawWidth > 0)
            canvas.Scale = new Vector2(Math.Min(1, Math.Min(DrawWidth / canvas.Width, DrawHeight / canvas.Height)));
    }

    /// <summary>
    /// Every canvas and run, to look at: the sliders size the canvas, the steps pick a phone and a run.
    /// </summary>
    [Test]
    public void TestTheColumnOnEachCanvas()
    {
        AddSliderStep("canvas width", 1100f, 1920f, pro_max.X, width => resize(new Vector2(width, canvas.Height), footer.Height));
        AddSliderStep("canvas height", 600f, 1080f, pro_max.Y, height => resize(new Vector2(canvas.Width, height), footer.Height));

        foreach ((string name, Vector2 size, float footerHeight) in new[]
                 {
                     ("an iPhone Pro Max", pro_max, phone_footer),
                     ("an iPhone Pro Max at UI scale 1.05", pro_max_larger, phone_footer),
                     ("an iPhone", iphone, phone_footer),
                     ("a 16:9 phone", sixteen_nine, phone_footer),
                     ("desktop", desktop, desktop_footer),
                     ("desktop at UI scale 1.4", desktop_larger, phone_footer)
                 })
        {
            AddStep(name, () => resize(size, footerHeight));
            show("a new best", Run.NewBest);
            statisticsClearTheScore();
            show("a long chart, behind the best", Run.LongChart);
            statisticsClearTheScore();
            show("a first play", Run.FirstPlay);
            statisticsClearTheScore();
        }
    }

    /// <summary>
    /// On the canvas it was reported on: the statistics beside the score as rows, and the plays strip in view. A step
    /// larger in UI scale on the phone and on desktop, the song header gives up a little width for the rows, and the
    /// judgement rows close up on the phone for the height.
    /// </summary>
    [Test]
    [VisualTestOnly("headless text is 0 wide, so every arrangement fits")]
    public void TestAProMaxFitsWithoutScrolling()
    {
        foreach ((string name, Vector2 size, bool larger) in new[]
                 {
                     ("an iPhone Pro Max", pro_max, false),
                     ("an iPhone Pro Max at UI scale 1.05", pro_max_larger, true),
                     ("desktop at UI scale 1.4", desktop_larger, true)
                 })
        {
            AddStep(name, () => resize(size, phone_footer));

            foreach (Run run in new[] { Run.NewBest, Run.LongChart })
            {
                show(run.ToString(), run);
                AddAssert("the statistics are rows beside the score", () => shown!.SummaryArrangement == ScoreSummaryArrangement.Rows);
                AddAssert("all of the column without scrolling", () => shown!.DetailsFit);
                AddAssert(larger ? "the header gave up a little width" : "the header as it is", () =>
                {
                    float scale = descendants(shown!).OfType<BeatmapInfoPanel>().Single().Scale.X;
                    return larger ? scale is >= 0.85f and < 1 : scale == 1;
                });
                statisticsClearTheScore();
            }
        }

        AddStep("a 16:9 phone", () => resize(sixteen_nine, phone_footer));
        show("a new best", Run.NewBest);
        AddAssert("the statistics go under the score", () => shown!.SummaryArrangement == ScoreSummaryArrangement.Stacked);
        statisticsClearTheScore();

        AddStep("desktop", () => resize(desktop, desktop_footer));
        show("a new best", Run.NewBest);
        AddAssert("the statistics in a line", () => shown!.SummaryArrangement == ScoreSummaryArrangement.OneLine);
    }

    private enum Run
    {
        NewBest,
        LongChart,
        FirstPlay
    }

    private void resize(Vector2 size, float footerHeight)
    {
        canvas.Size = size;
        footer.Height = footerHeight;
        stack.Padding = new MarginPadding { Bottom = footerHeight };
    }

    private void show(string name, Run run) => AddStep($"show {name}", () =>
    {
        if (stack.CurrentScreen is ScoreScreen previous)
            previous.Exit();

        // A difficulty of its own each time, so the plays shown before aren't among this one's.
        beatmap.ID++;

        // 742 notes, a full combo; or 1999 with a combo of 765 against a best's full combo.
        GameplayResult result = run == Run.LongChart
            ? new GameplayResult { CriticalCount = 1800, BreakCount = 150, HitCount = 40, MissCount = 9, MaxCombo = 765, Score = 912_345, ChartLength = 150_000 }
            : new GameplayResult { CriticalCount = 700, BreakCount = 39, HitCount = 3, MaxCombo = 742, Score = 981_805, ChartLength = 120_000 };

        if (run != Run.FirstPlay)
        {
            for (int i = 0; i < 4; i++)
            {
                bool behind = run == Run.LongChart;
                scoreStore.Add(new ScoreInfo
                {
                    BeatmapId = beatmap.ID,
                    BeatmapSetId = beatmap.BeatmapSet.ID,
                    Score = behind ? 998_000 - i * 9_000 : 933_133 - i * 9_000,
                    Accuracy = behind ? 0.9978 : 0.8829,
                    MaxCombo = behind ? result.TotalNotes : 131,
                    Date = DateTimeOffset.Now.AddDays(i - 4)
                });
            }
        }

        ScoreInfo saved = ScoreInfo.From(result, beatmap.ID, beatmap.BeatmapSet.ID);
        saved.Date = DateTimeOffset.Now;
        scoreStore.Add(saved);

        stack.Push(shown = new ScoreScreen(result, run == Run.NewBest, () => { }, saved) { RelativeSizeAxes = Axes.Both });
    });

    private void statisticsClearTheScore()
    {
        AddUntilStep("results are in", () => stack.CurrentScreen == shown && shown!.IsLoaded);
        AddStep("skip the reveal", () =>
        {
            shown!.Skip();
            shown.Skip();
        });
        AddUntilStep("the statistics clear the score", () =>
        {
            (Drawable score, Drawable statistics) = shown!.SummaryBlocks;
            var a = score.ScreenSpaceDrawQuad;
            var b = statistics.ScreenSpaceDrawQuad;

            return shown.SummaryArrangement == ScoreSummaryArrangement.Stacked
                ? a.BottomRight.Y <= b.TopLeft.Y + 0.5f
                : a.BottomRight.X <= b.TopLeft.X + 0.5f;
        });
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
