// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

/// <summary>
/// The timing graph zoomed in: it shows less of the song and less of how early or late, and is dragged along the song.
/// All of the song on show, a drag is the page's to scroll.
/// </summary>
public partial class TestTimingGraphZoom : YuukiManualInputManagerTestScene
{
    private TimingScatter scatter = null!;
    private HealthComboGraph health = null!;
    private PlayAnalysis run = null!;
    private double? watchedFrom;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("show the graphs", () =>
        {
            TestContent.Clear();
            watchedFrom = null;

            YuukiColorPalette palette = ResultsTestRuns.Palette;
            run = new PlayAnalysis(ResultsTestRuns.Rich());

            TestContent.Add(new Box { RelativeSizeAxes = Axes.Both, Color = palette.Surface });
            TestContent.Add(new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Left = 24, Right = 24, Top = 24 },
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 24),
                Children = new Drawable[]
                {
                    scatter = new TimingScatter(palette) { RelativeSizeAxes = Axes.X, WatchFrom = from => watchedFrom = from },
                    health = new HealthComboGraph(palette) { RelativeSizeAxes = Axes.X }
                }
            });

            health.View = scatter.View;
            scatter.SetRun(run);
            health.SetRun(run);
        });
        AddWaitStep("plotted", 2);
    }

    private IEnumerable<SpriteText> labels(Drawable graph) => descendants(graph).OfType<SpriteText>().Where(t => t.Alpha > 0);

    private bool says(Drawable graph, string text) => labels(graph).Any(t => t.Text == text);

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

    private void zoomIn(int steps = 1)
    {
        AddStep($"zoom in {steps}", () =>
        {
            for (int i = 0; i < steps; i++)
                scatter.View.ZoomIn();
        });
        AddWaitStep("plotted", 2);
    }

    /// <summary>
    /// Each step shows less of how early or late, to what a Break is, then a Critical, with Bloom's marked once there's room.
    /// </summary>
    [Test]
    public void TestShowingLessOfHowEarlyOrLate()
    {
        AddAssert("all of it, up to a Hit", () => says(scatter, "+135") && says(scatter, "−135") && says(scatter, "+45") && !says(scatter, "+22"));

        zoomIn();
        AddAssert("up to a Break", () => says(scatter, "+90") && says(scatter, "−90") && !says(scatter, "+135"));
        AddAssert("Bloom's marked", () => says(scatter, "+22") && says(scatter, "−22"));

        zoomIn();
        AddAssert("up to a Critical", () => says(scatter, "+45") && says(scatter, "−45") && !says(scatter, "+90"));
        AddAssert("on time, in the middle", () =>
        {
            SpriteText top = labels(scatter).First(t => t.Text == "+45");
            SpriteText middle = labels(scatter).First(t => t.Text == "0");
            SpriteText bottom = labels(scatter).First(t => t.Text == "−45");
            return System.Math.Abs(middle.Y - (top.Y + bottom.Y) / 2) < 1;
        });
    }

    /// <summary>
    /// A part of the song: its own time labels, and the graph under it lined up.
    /// </summary>
    [Test]
    public void TestShowingLessOfTheSong()
    {
        AddAssert("from the start", () => says(scatter, "0:00") && says(scatter, "2:00"));

        zoomIn();
        AddAssert("the middle of it", () => !says(scatter, "0:00") && !says(scatter, "2:00") && says(scatter, "1:00"));
        AddAssert("the graph under it too", () => !says(health, "0:00") && says(health, "1:00"));
        AddAssert("at the same place", () =>
        {
            SpriteText top = labels(scatter).First(t => t.Text == "1:00");
            SpriteText under = labels(health).First(t => t.Text == "1:00");
            return System.Math.Abs(top.X - under.X) < 1;
        });

        AddStep("move on to the end", () => scatter.View.Move(1));
        AddWaitStep("plotted", 2);
        AddAssert("the end of it", () => says(scatter, "2:00") && !says(scatter, "0:30"));
    }

    /// <summary>
    /// A moment is drawn where its share of what's shown puts it, the zoom and the place along the song both counting.
    /// </summary>
    [Test]
    public void TestWhereAMomentIsDrawn()
    {
        const float left = 58, right_gap = 12;

        float expected(double time) => left + (float)((time / run.Length - scatter.View.Start) * scatter.View.Zoom) * (scatter.DrawWidth - right_gap - left);
        float drawn(string text) => labels(scatter).First(t => t.Text == text).X;

        AddAssert("all of the song", () => System.Math.Abs(drawn("1:00") - expected(60_000)) < 1 && System.Math.Abs(drawn("0:00") - left) < 1);

        zoomIn();
        AddAssert("twice over, in the middle", () => scatter.View.Start == 0.25 && System.Math.Abs(drawn("1:00") - expected(60_000)) < 1);

        AddStep("move along", () => scatter.View.Move(0.1));
        AddWaitStep("plotted", 2);
        AddAssert("further along", () => System.Math.Abs(drawn("1:00") - expected(60_000)) < 1 && scatter.View.Start > 0.25);

        zoomIn();
        AddAssert("four times over", () => scatter.View.Zoom == 4 && System.Math.Abs(drawn("1:10") - expected(70_000)) < 1);
    }

    /// <summary>
    /// Sideways on a trackpad moves along the song, zoomed in. Up and down is the page's, and so is all of it at all of the song.
    /// </summary>
    [Test]
    public void TestSwipingSideways()
    {
        static ScrollEvent swipe(float x, float y) => new ScrollEvent(new MouseState(), new Vector2(x, y));

        AddAssert("all of the song: the page's", () => !scatter.OnScroll(swipe(-1, 0)) && scatter.View.Middle == 0.5);

        zoomIn();

        double before = 0;

        AddStep("note where it is", () => before = scatter.View.Middle);
        AddAssert("up and down: the page's", () => !scatter.OnScroll(swipe(0, -1)) && scatter.View.Middle == before);
        AddAssert("mostly up and down: the page's", () => !scatter.OnScroll(swipe(-0.2f, -1)) && scatter.View.Middle == before);
        AddAssert("sideways: its own", () => scatter.OnScroll(swipe(-1, 0)));
        AddAssert("on to later in the song", () => scatter.View.Middle > before);
    }

    /// <summary>
    /// Zoomed in, dragging moves along the song, the way the content goes: to the left, on to later. Both graphs go.
    /// </summary>
    [Test]
    public void TestDraggingAlongTheSong()
    {
        zoomIn();

        double before = 0;

        AddAssert("it takes the drag from the page", () => scatter.OnDragStart(new MouseButtonEvent(new MouseState(), MouseButton.Left, 1)));
        AddStep("press on the graph and drag left", () =>
        {
            before = scatter.View.Middle;

            InputManager.MoveMouseTo(scatter);
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(scatter.ToScreenSpace(new Vector2(scatter.DrawWidth / 2 - 100, scatter.DrawHeight / 2)));
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddWaitStep("plotted", 2);
        AddAssert("on to later in the song", () => scatter.View.Middle > before);
        AddAssert("the graph didn't offer to watch from there", () => scatter.OfferedWatch == null);
    }

    /// <summary>
    /// All of the song on show there's nowhere to move, and it doesn't take the drag from the page.
    /// </summary>
    [Test]
    public void TestAllOfTheSongLeavesADragToThePage()
    {
        AddAssert("doesn't take it", () => !scatter.OnDragStart(new MouseButtonEvent(new MouseState(), MouseButton.Left, 1)));
        AddStep("drag", () =>
        {
            InputManager.MoveMouseTo(scatter);
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(scatter.ToScreenSpace(new Vector2(scatter.DrawWidth / 2 - 100, scatter.DrawHeight / 2)));
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddAssert("it's where it was", () => scatter.View.Middle == 0.5);
    }

    /// <summary>
    /// A press that doesn't move still offers to watch from there, zoomed in or not.
    /// </summary>
    [Test]
    public void TestAClickStillOffersToWatch()
    {
        zoomIn();
        AddStep("click the graph", () =>
        {
            InputManager.MoveMouseTo(scatter);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("offered", () => scatter.OfferedWatch != null);
    }

    /// <summary>
    /// The hardest stretch shaded with it off to the side: nothing to shade, and nothing goes wrong.
    /// </summary>
    [Test]
    public void TestHighlightsAndTheLikeOffTheSide()
    {
        zoomIn(2);
        AddStep("to the start", () => scatter.View.Move(-1));
        AddStep("shade the hardest stretch", () => scatter.Highlight = health.Highlight = new PlayAnalysis(ResultsTestRuns.Rich()).Hardest);
        AddWaitStep("drawn", 2);
        AddStep("to the end", () => scatter.View.Move(1));
        AddWaitStep("drawn", 2);
        AddAssert("still there", () => scatter.IsLoaded);
    }
}
