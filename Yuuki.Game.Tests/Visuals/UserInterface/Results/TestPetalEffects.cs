// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestPetalEffects : YuukiTestScene
{
    private PetalEffects effects = null!;
    private GustDriver gusts = null!;
    private readonly Random random = new Random(7);

    [SetUp]
    public void SetUp()
    {
        AddStep("show the petals", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400),
                Color = ColorExtensions.FromHex("#151D30"),
                Alpha = 0.5f
            });
            Add(effects = new PetalEffects());
            Add(gusts = new GustDriver(effects.Fall));
        });
    }

    [Test]
    public void TestBursts()
    {
        AddStep("an S lands", () => burst(PetalBurst.Landing(ScoreRank.S, false, false, JudgementColors.For(ScoreRank.S))));
        AddWaitStep("watch", 1500);
        AddStep("an A lands, full combo", () => burst(PetalBurst.Landing(ScoreRank.A, true, false, JudgementColors.For(ScoreRank.A))));
        AddWaitStep("watch", 1500);
        AddStep("a B lands", () => burst(PetalBurst.Landing(ScoreRank.B, false, false, JudgementColors.For(ScoreRank.B))));
        AddWaitStep("watch", 1500);
        AddStep("an All Perfect, with sparkles", () =>
        {
            burst(PetalBurst.Landing(ScoreRank.S, true, true, JudgementColors.For(ScoreRank.S)));
            effects.Burst(middle + new Vector2(0, 300), PetalBurst.Sparkles());
        });
        AddWaitStep("watch", 1500);
        AddStep("a rank crossed (A, out to the upper left)", () => effects.Burst(middle + new Vector2(-120, -160), PetalBurst.Crossing(random, ScoreRank.A, -2.2f)));
        AddStep("the fill's trail", () =>
        {
            for (int i = 0; i < 40; i++)
            {
                float angle = -MathF.PI / 2 + MathF.Tau * 0.9f * i / 40;
                var at = middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 198;
                effects.Burst(at, PetalBurst.Trail(random, angle, i < 18 ? 2 : 1), effects.Clock.CurrentTime + i * 36);
            }
        });
        AddWaitStep("watch", 2000);
        AddStep("an S broken: shards, wilted petals", () =>
        {
            effects.Burst(middle - new Vector2(0, 30), PetalBurst.Shards());
            effects.Burst(middle - new Vector2(0, 60), PetalBurst.Wilted(), effects.Clock.CurrentTime + 140);
        });
        AddWaitStep("watch", 1500);
        AddStep("a Full Bloom's storm", () => burst(PetalBurst.Storm()));
    }

    [Test]
    public void TestFalling()
    {
        AddStep("an S's fall (14 a second)", () =>
        {
            effects.Fall.Rate = 14;
            effects.Fall.Colors = fallColors;
        });
        AddSliderStep("rate (a second)", 0f, 60f, 14f, rate => effects.Fall.Rate = rate);
        AddSliderStep("most at once", 20, 400, 180, limit => effects.Fall.Limit = limit);
        AddSliderStep("wind", -0.1f, 0.1f, 0f, wind => effects.Fall.Wind = wind);
        AddSliderStep("gustiness", 0f, 0.1f, 0.03f, gustiness => effects.Fall.Gustiness = gustiness);
        AddStep("the beat's gusts on (128 BPM)", () => gusts.Enabled = true);
        AddStep("the beat's gusts off", () => gusts.Enabled = false);
        AddStep("the heroes' colors (an All Perfect)", () => effects.Fall.Colors = null);
        AddStep("pinks (any other rank)", () => effects.Fall.Colors = fallColors);
        AddStep("blast out from the middle", () => effects.Fall.Blast(middle, 0.9f));
        AddStep("stop", () => effects.Fall.Rate = 0);
    }

    [Test]
    public void TestABurstEndsAndClears()
    {
        AddStep("an S lands", () => burst(PetalBurst.Landing(ScoreRank.S, false, false, JudgementColors.For(ScoreRank.S))));
        AddUntilStep("pieces are out", () => effects.ActivePieces > 0);
        AddUntilStep("over in a few seconds", () => effects.ActiveBursts == 0 && effects.ActivePieces == 0);
    }

    [Test]
    public void TestABurstFromThePastIsAlreadyOnItsWay()
    {
        AddStep("a D lands two seconds ago", () => burst(PetalBurst.Landing(ScoreRank.D, false, false, JudgementColors.For(ScoreRank.D)), -2000));
        AddAssert("its sparks are already gone", () => effects.ActivePieces == 0);
    }

    [Test]
    public void TestTheFallFillsAndStaysUnderItsLimit()
    {
        AddStep("a heavy fall, held to 60", () =>
        {
            effects.Fall.Limit = 60;
            effects.Fall.Rate = 200;
        });
        AddUntilStep("sixty falling", () => effects.Fall.Petals.Count == 60);
        AddAssert("all drawn", () => effects.ActivePieces >= 60);
        AddStep("stop", () => effects.Fall.Rate = 0);
    }

    private Vector2 middle => effects.DrawSize / 2;

    private static Color[] fallColors => new[]
    {
        HeroColors.YUUNA.Main, Color.White, HeroColors.YUUNA.Light, ColorExtensions.FromHex("#F6C3CB"),
        HeroColors.KAORUKO.Main, HeroColors.KAORUKO.Light, HeroColors.KAORUKO.Deep
    };

    private void burst(IReadOnlyList<BurstPiece> pieces, double delay = 0) =>
        effects.Burst(middle, pieces, effects.Clock.CurrentTime + delay);

    /// <summary>
    /// Gusts on the music's beat, as the results music will give them: a push that dies away, the other way each beat.
    /// </summary>
    private partial class GustDriver : Component
    {
        private const double beat_length = 60000 / 128.0;

        private readonly FallingPetals fall;

        public bool Enabled;

        public GustDriver(FallingPetals fall)
        {
            this.fall = fall;
        }

        public override void Update()
        {
            base.Update();

            if (!Enabled)
            {
                fall.Gust = 0;
                return;
            }

            double beats = Clock.CurrentTime / beat_length;
            double phase = beats % 1;
            fall.Gust = 0.0004f * MathF.Exp((float)(-phase * 5)) * (Math.Floor(beats) % 2 == 0 ? -1 : 1);
        }
    }
}
