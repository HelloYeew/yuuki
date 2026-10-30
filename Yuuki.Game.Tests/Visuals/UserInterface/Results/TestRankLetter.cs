// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestRankLetter : YuukiTestScene
{
    private RankLetter letter = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show a letter", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(letter = new RankLetter { Scale = new Vector2(1.5f) });
        });
    }

    [Test]
    public void TestLanding()
    {
        AddSliderStep("zoom", 0.5f, 3f, 1.5f, scale => letter.Scale = new Vector2(scale));

        foreach (ScoreRank rank in new[] { ScoreRank.S, ScoreRank.A, ScoreRank.B, ScoreRank.C, ScoreRank.D })
            AddStep($"land {rank}", () => letter.Land(rank, JudgementColors.For(rank)));

        AddStep("land S, it breaks, A lands", () =>
        {
            double now = letter.Clock.CurrentTime;
            letter.Land(ScoreRank.S, JudgementColors.For(ScoreRank.S), now + 200);
            letter.Crack(now + 520);
            Scheduler.AddDelayed(() => letter.Land(ScoreRank.A, JudgementColors.For(ScoreRank.A), letter.Clock.CurrentTime + RankLetter.FALL), 1180);
        });
        AddStep("clear", () => letter.Clear());
    }

    /// <summary>
    /// Told to land at a time already past, it shows as it would be by now: there, at its size.
    /// </summary>
    [Test]
    public void TestLandingInThePast()
    {
        AddStep("land a second ago", () => letter.Land(ScoreRank.A, JudgementColors.For(ScoreRank.A), letter.Clock.CurrentTime - 1000));
        AddAssert("it's showing A", () => letter.Rank == ScoreRank.A);
        AddUntilStep("it's there, at its size", () => letter.Children[0].Alpha == 1 && Precision.AlmostEquals(letter.Children[0].Scale.X, 1));
    }
}
