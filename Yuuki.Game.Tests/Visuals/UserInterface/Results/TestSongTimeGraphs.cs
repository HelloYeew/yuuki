// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
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
/// The More info page's two graphs through the song, on their own: the timing graph (<see cref="TimingScatter"/>) and
/// health and combo (<see cref="HealthComboGraph"/>), drawn in left to right, behind a re-judge's playhead, filtered,
/// a lane brought out, the best ghosted under it, the hardest stretch shaded.
/// </summary>
public partial class TestSongTimeGraphs : YuukiTestScene
{
    private TimingScatter scatter = null!;
    private HealthComboGraph health = null!;
    private PlayAnalysis run = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the graphs", () =>
        {
            Clear();

            YuukiColorPalette palette = ResultsTestRuns.Palette;
            run = new PlayAnalysis(ResultsTestRuns.Rich());

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = palette.Surface });
            Add(new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Left = 24, Right = 24, Top = 24 },
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 24),
                Children = new Drawable[]
                {
                    scatter = new TimingScatter(palette) { RelativeSizeAxes = Axes.X },
                    health = new HealthComboGraph(palette) { RelativeSizeAxes = Axes.X }
                }
            });

            scatter.SetRun(run);
            health.SetRun(run);
        });
    }

    [Test]
    public void TestDrawingIn()
    {
        AddSliderStep("drawn to", 0f, 1f, 1f, drawn => scatter.Reveal = health.Reveal = drawn);
        AddSliderStep("re-judged to (s, 0 for done)", 0f, 130f, 0f, seconds =>
        {
            var playhead = seconds <= 0 ? ((double, int, int)?)null : (seconds * 1000.0, (int)(run.Points.Count(p => !p.IsHoldTail) * seconds / 130), run.Points.Count(p => !p.IsHoldTail));
            scatter.Judging = health.Judging = playhead;
        });
    }

    [Test]
    public void TestPicking()
    {
        AddStep("hide the Blooms", () => scatter.Show(HitResult.Bloom, false));
        AddAssert("not shown", () => !scatter.Shows.Contains(HitResult.Bloom));
        AddStep("just lane 3", () => scatter.ShowLane(2, true));
        AddStep("every lane, lane 3 brought out", () =>
        {
            scatter.ShowLane(2, false);
            scatter.BroughtOut = 2;
        });
        AddStep("the Blooms back, nothing brought out", () =>
        {
            scatter.Show(HitResult.Bloom, true);
            scatter.BroughtOut = null;
        });
        AddStep("the best ghosted under it", () =>
        {
            scatter.SetBest(new PlayAnalysis(ResultsTestRuns.Rich(9, rough: false)));
            scatter.Compare = true;
        });
        AddStep("the hardest stretch shaded", () => scatter.Highlight = health.Highlight = run.Hardest);
        AddAssert("there is one", () => run.Hardest != null);
    }
}
