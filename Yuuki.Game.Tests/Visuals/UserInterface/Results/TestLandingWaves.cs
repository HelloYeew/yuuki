// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestLandingWaves : YuukiTestScene
{
    private Container stage = null!;
    private LandingWaves waves = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the ring's middle", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(stage = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(440),
                Children = new Drawable[]
                {
                    new BlossomRing { Anchor = Anchor.Centre, Origin = Anchor.Centre, Scale = new Vector2(440 / BlossomRing.RING_SIZE), Fill = 0.97f },
                    waves = new LandingWaves { RelativeSizeAxes = Axes.Both }
                }
            });
        });
    }

    [Test]
    public void TestLanding()
    {
        AddSliderStep("zoom", 0.5f, 3f, 1f, scale => stage.Scale = new Vector2(scale));

        foreach (ScoreRank rank in new[] { ScoreRank.S, ScoreRank.A, ScoreRank.B, ScoreRank.D })
            AddStep($"{rank} lands", () => waves.Land(JudgementColors.For(rank)));

        AddStep("S lands, then breaks", () =>
        {
            waves.Land(JudgementColors.For(ScoreRank.S));
            waves.Crack(waves.Clock.CurrentTime + 320);
        });
        AddStep("clear", () => waves.Clear());
    }
}
