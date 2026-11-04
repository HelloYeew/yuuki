// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestLightRays : YuukiTestScene
{
    private LightRays rays = null!;
    private Spinner spinner = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show rays", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(rays = new LightRays
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(760),
                Color = SetlistColors.TierFill(JudgementColors.For(ScoreRank.S))
            });
            Add(new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(334),
                Color = ColorExtensions.FromHex("#B4B4C4"),
                Alpha = 0.9f
            });
            Add(spinner = new Spinner(rays));
        });
    }

    [Test]
    public void TestRays()
    {
        AddSliderStep("spokes", 4, 48, 20, spokes => rays.Spokes = spokes);
        AddSliderStep("width", 0.05f, 1f, LightRays.THIN, width => rays.SpokeWidth = width);
        AddSliderStep("brightness", 0f, 1.5f, 0.6f, peak => rays.Peak = peak);
        AddSliderStep("turn (° a second)", -60f, 60f, 0f, speed => spinner.DegreesPerSecond = speed);
        AddStep("an S's", () =>
        {
            rays.Spokes = 20;
            rays.SpokeWidth = LightRays.THIN;
            rays.HeroColored = false;
            rays.Color = SetlistColors.TierFill(JudgementColors.For(ScoreRank.S));
            rays.Peak = 0.6f;
        });
        AddStep("an All Perfect's", () => rays.Color = SetlistColors.TierFill(JudgementColors.ALL_PERFECT));
        AddStep("a Full Bloom's: the heroes' colors", () =>
        {
            rays.Spokes = 18;
            rays.SpokeWidth = LightRays.WIDE;
            rays.HeroColored = true;
            rays.Peak = 0.75f;
        });
    }

    /// <summary>
    /// Turns the rays, as the screen will.
    /// </summary>
    private partial class Spinner : Component
    {
        private readonly LightRays rays;

        public float DegreesPerSecond;

        public Spinner(LightRays rays)
        {
            this.rays = rays;
        }

        public override void Update()
        {
            base.Update();
            rays.Rotation = (float)(Clock.CurrentTime / 1000 * DegreesPerSecond % 360);
        }
    }
}
