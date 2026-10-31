// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

/// <summary>
/// More info's small cards, one each: a <see cref="ResultsCard"/> holding the timing spread, the lanes, the note types and
/// the timing figures, and waiting under its shimmer.
/// </summary>
public partial class TestMoreInfoCards : YuukiTestScene
{
    private ResultsCard spreadCard = null!, lanesCard = null!, typesCard = null!, figuresCard = null!;
    private TimingSpread spread = null!;
    private LaneColumns lanes = null!;
    private NoteTypeBars types = null!;
    private TimingFigures figures = null!;
    private int? pointedAt;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the cards", () =>
        {
            Clear();

            YuukiColorPalette palette = ResultsTestRuns.Palette;

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(new FlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 24, Top = 24 },
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(16),
                Children = new Drawable[]
                {
                    spreadCard = card("Timing spread", "Early or late overall?", palette),
                    lanesCard = card("Lanes", "Which finger is off?", palette),
                    typesCard = card("Note types", "Holds or swipes?", palette),
                    figuresCard = card("Timing", "Should I change my offset?", palette)
                }
            });

            spreadCard.Body.Add(spread = new TimingSpread(palette));
            lanesCard.Body.Add(lanes = new LaneColumns(palette) { LaneHovered = lane => pointedAt = lane });
            typesCard.Body.Add(types = new NoteTypeBars(palette));
            figuresCard.Body.Add(figures = new TimingFigures(palette));
        });
    }

    private static ResultsCard card(string title, string question, YuukiColorPalette palette) => new ResultsCard(title, question, palette, true)
    {
        Size = new Vector2(444, 212)
    };

    private void show(GameplayResult result)
    {
        var run = new PlayAnalysis(result);
        spread.SetRun(run);
        lanes.SetRun(run);
        types.SetRun(run);
        figures.SetRun(run);
        lanesCard.Question = LaneColumns.Verdict(run);
    }

    [Test]
    public void TestARun()
    {
        AddStep("a run with a rough patch", () => show(ResultsTestRuns.Rich()));
        AddAssert("lane 3 runs late", () => lanesCard.Question.StartsWith("Lane 3 runs", System.StringComparison.Ordinal));
        AddAssert("the spread says it in words", () => spread.Line.Contains("spread ±", System.StringComparison.Ordinal) && !spread.Line.Contains('σ'));
        AddSliderStep("spread grown", 0f, 1f, 1f, grown => spread.Progress = grown);
        AddStep("a clean one", () => show(ResultsTestRuns.Rich(8, rough: false)));
        AddAssert("nothing pointed at yet", () => pointedAt == null);
    }

    [Test]
    public void TestWaiting()
    {
        AddStep("a run", () => show(ResultsTestRuns.Rich()));
        AddStep("waiting", () => spreadCard.Waiting = lanesCard.Waiting = typesCard.Waiting = figuresCard.Waiting = true);
        AddWaitStep("shimmering", 1200);
        AddStep("in", () => spreadCard.Waiting = lanesCard.Waiting = typesCard.Waiting = figuresCard.Waiting = false);
        AddUntilStep("shown", () => spreadCard.Body.Alpha == 1);
    }
}
