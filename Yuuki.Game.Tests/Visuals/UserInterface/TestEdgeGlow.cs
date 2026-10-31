// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestEdgeGlow : YuukiTestScene
{
    private EdgeGlow glow = null!;
    private Pulser pulser = null!;
    private HeroColorCycle cycle = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the light", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(glow = new EdgeGlow { GlowColor = JudgementColors.For(ScoreRank.S), Intensity = 0.4f });
            Add(pulser = new Pulser(glow));
            Add(cycle = new HeroColorCycle { Running = false });
            cycle.Add(c => glow.GlowColor = c);
        });
    }

    [Test]
    public void TestLight()
    {
        AddSliderStep("strength", 0f, 1.5f, 0.4f, intensity =>
        {
            pulser.Running = false;
            glow.Intensity = intensity;
        });
        AddStep("off", () =>
        {
            pulser.Running = false;
            glow.Intensity = 0;
        });
        AddAssert("hidden when off", () => glow.Alpha == 0);
        AddStep("a rank lands, then the beat", () => pulser.Land(JudgementColors.For(ScoreRank.S)));
        AddWaitStep("watch", 2500);
        AddStep("an A", () => pulser.Land(JudgementColors.For(ScoreRank.A)));
        AddWaitStep("watch", 2500);
        AddStep("the heroes' colors", () => cycle.Running = true);
        AddStep("one color", () =>
        {
            cycle.Running = false;
            glow.GlowColor = JudgementColors.For(ScoreRank.S);
        });
    }

    /// <summary>
    /// On a screen with a notch and bars, bleeding, it lights the screen's real edges rather than the safe area's (as the
    /// results screen has it).
    /// </summary>
    [Test]
    public void TestReachesTheScreensEdges()
    {
        SafeAreaDefiningContainer screen = null!;
        EdgeGlow bleeding = null!;

        AddStep("a screen with a notch and bars", () =>
        {
            Clear();

            Add(screen = new SafeAreaDefiningContainer
            {
                RelativeSizeAxes = Axes.Both,
                OverrideSafeArea = new MarginPadding { Top = 40, Bottom = 20, Left = 30, Right = 30 },
                Child = new SafeAreaContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") },
                        bleeding = new EdgeGlow { Bleed = Edges.All, GlowColor = JudgementColors.For(ScoreRank.S), Intensity = 0.8f }
                    }
                }
            });
        });
        AddUntilStep("the whole screen", () => Math.Abs(bleeding.DrawSize.X - screen.DrawSize.X) < 1 && Math.Abs(bleeding.DrawSize.Y - screen.DrawSize.Y) < 1);
    }

    /// <summary>
    /// Drives the light as the reveal will: a burst as the rank lands, dying down onto a pulse on each beat at 128 BPM.
    /// </summary>
    private partial class Pulser : Component
    {
        private const double beat_length = 60000 / 128.0;

        private readonly EdgeGlow glow;
        private double landed = double.NaN;

        public bool Running;

        public Pulser(EdgeGlow glow)
        {
            this.glow = glow;
        }

        public void Land(Color color)
        {
            glow.GlowColor = color;
            landed = Clock.CurrentTime;
            Running = true;
        }

        public override void Update()
        {
            base.Update();

            if (!Running || double.IsNaN(landed))
                return;

            double since = Clock.CurrentTime - landed;
            float landing = 0.3f + 0.8f * MathF.Exp((float)(-since / 320));
            float beat = 0.24f + 0.36f * MathF.Exp((float)(-(since % beat_length) / beat_length * 5));
            glow.Intensity = Math.Max(landing, beat);
        }
    }
}
