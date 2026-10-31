// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestBlossomRing : YuukiTestScene
{
    private BlossomRing ring = null!;
    private RevealDriver driver = null!;
    private HeroColorCycle bandCycle = null!;
    private readonly List<ScoreRank> crossed = new List<ScoreRank>();

    [SetUp]
    public void SetUp()
    {
        AddStep("show the ring", () =>
        {
            Clear();
            crossed.Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(ring = new BlossomRing
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(1.1f)
            });
            Add(driver = new RevealDriver(ring));
            Add(bandCycle = new HeroColorCycle { Running = false });

            for (int i = 0; i < ring.BandColors.Length; i++)
            {
                int band = i;
                bandCycle.Add(c => ring.BandColors[band] = c, i * 60);
            }

            ring.ThresholdCrossed += rank => crossed.Add(rank);
        });
    }

    [Test]
    public void TestByHand()
    {
        AddSliderStep("fill", 0f, 1f, 0f, fill =>
        {
            driver.Stop();
            ring.Fill = fill;
            ring.FillColor = JudgementColors.For(ScoreRankExtensions.FromAccuracy(fill));
        });
        AddSliderStep("pulse", 0f, 1f, 0f, pulse => ring.Pulse = pulse);
        AddSliderStep("charge", 0f, 1f, 0f, charge => ring.Charge = charge);
        AddSliderStep("glow", 0f, 1f, 0f, glow => ring.Glow = glow);
        AddSliderStep("shimmer", 0f, 1f, 1f, shimmer => ring.Shimmer = shimmer);
        AddSliderStep("backdrop wobble", 0f, 3f, 1f, wobble => ring.Backdrop.Wobble = wobble);
        AddSliderStep("backdrop ripples", 0f, 3f, 1f, ripples => ring.Backdrop.Ripples = ripples);
        AddSliderStep("backdrop film", 0f, 3f, 1f, sheen => ring.Backdrop.Sheen = sheen);
        AddSliderStep("backdrop glow", 0f, 3f, 1f, glow => ring.Backdrop.Glow = glow);
        AddSliderStep("breeze", 0f, 3f, 1f, breeze => ring.Branch.Breeze = breeze);
        AddSliderStep("zoom", 0.3f, 3f, 1.1f, zoom => ring.Scale = new Vector2(zoom));
        AddStep("wob the backdrop", () => ring.Backdrop.Wob(strength: 1.6f));
        AddStep("a gust", () => ring.Branch.Gust(strength: 1.5f));
    }

    [Test]
    public void TestReveals()
    {
        AddStep("an S (97.4%)", () => driver.Play(0.974f));
        AddWaitStep("let it fill", 2600);
        AddStep("an A (91.2%)", () => driver.Play(0.912f));
        AddWaitStep("let it fill", 2600);
        AddStep("a C (73%)", () => driver.Play(0.73f));
        AddWaitStep("let it fill", 2600);
        AddStep("an All Perfect", () =>
        {
            driver.Play(1);
            ring.OpenAllPerfect(ring.Clock.CurrentTime + 2400);
        });
        AddWaitStep("let it fill", 3200);
        AddStep("then a Full Bloom", () =>
        {
            ring.BloomWave();
            bandCycle.StartTime = bandCycle.Clock.CurrentTime;
            bandCycle.Running = true;
            driver.FullBloom = true;
        });
    }

    /// <summary>
    /// An S lost to a miss: the S flowers wilt and fall, and the fill pulls back to where S begins.
    /// </summary>
    [Test]
    public void TestLosingTheS()
    {
        AddStep("an S's accuracy (96.1%)", () => driver.Play(0.961f));
        AddWaitStep("let it fill", 2300);
        AddStep("lose the S", () =>
        {
            ring.WiltRank(ScoreRank.S);
            driver.PullBackTo((float)ScoreRank.S.MinimumAccuracy() - 0.0005f, JudgementColors.For(ScoreRank.A));
        });
        AddWaitStep("let it fall", 1500);
    }

    /// <summary>
    /// Every flower is drawn at its size: in a parent of no size it would be drawn at none (Sakura draws a child through
    /// its parent's size), which only shows on screen.
    /// </summary>
    [Test]
    public void TestTheFlowersHaveTheirSizeOnScreen()
    {
        AddStep("fill to 97%", () => ring.Fill = 0.97f);
        AddAssert("16 flowers (three by C, B and A, four by S, three at the top), each at least 10 across", () =>
        {
            BlossomFlower[] flowers = descendants(ring).OfType<BlossomFlower>().ToArray();
            return flowers.Length == 16 && flowers.All(f => f.DrawRectangle.Width >= 10);
        });
    }

    /// <summary>
    /// A rank's buds grow out of the branch only as the fill comes near it: none show before the fill has started (only
    /// the three at the top, which are there from the start), and they don't show and then vanish when it does.
    /// </summary>
    [Test]
    public void TestTheBudsWaitForTheFill()
    {
        AddUntilStep("only the three at the top show before the fill", () => shownFlowers() == 3);
        AddStep("fill to 68%, just short of C", () => ring.Fill = 0.68f);
        AddUntilStep("C's three are growing too", () => shownFlowers() == 6);
    }

    private int shownFlowers() => descendants(ring).OfType<BlossomFlower>().Count(f => f.Alpha > 0);

    private static IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.InternalChildren)
            {
                foreach (Drawable d in descendants(child))
                    yield return d;
            }
        }
    }

    /// <summary>
    /// Still with no breeze; a gust bows the branch well past its gentle sway, and it settles back to that.
    /// </summary>
    [Test]
    public void TestAGustPassesAndSettles()
    {
        AddStep("no gusts by themselves, no breeze", () =>
        {
            ring.Branch.GustInterval = 0;
            ring.Branch.Breeze = 0;
        });
        AddAssert("still", () => turns.All(t => ring.Branch.SwayAt(t) == 0));
        AddStep("a breeze, and a gust", () =>
        {
            ring.Branch.Breeze = 1;
            ring.Branch.Gust(strength: 1.5f);
        });
        AddUntilStep("bowed by it", () => turns.Any(t => Math.Abs(ring.Branch.SwayAt(t)) > 4));
        AddUntilStep("back to its gentle sway", () => turns.All(t => Math.Abs(ring.Branch.SwayAt(t)) < 1.95f));
    }

    private static readonly float[] turns = Enumerable.Range(0, 16).Select(i => i / 16f).ToArray();

    [Test]
    public void TestEachThresholdIsCrossedOnceInOrder()
    {
        AddStep("fill to 97%", () => ring.Fill = 0.97f);
        AddAssert("C, B, A, S", () => string.Join(",", crossed) == "C,B,A,S");
        AddStep("a little more", () => ring.Fill = 0.99f);
        AddAssert("no more", () => crossed.Count == 4);
        AddStep("back to 85%", () => ring.Fill = 0.85f);
        AddStep("and up again", () => ring.Fill = 0.97f);
        AddAssert("A and S again", () => string.Join(",", crossed) == "C,B,A,S,A,S");
    }

    [Test]
    public void TestGreyed()
    {
        AddStep("an A", () => driver.Play(0.93f));
        AddWaitStep("let it fill", 2200);
        AddStep("grey it", () => ring.Greyed = true);
        AddStep("colors back", () => ring.Greyed = false);
    }

    /// <summary>
    /// Plays a fill as the reveal will (the screen will do this itself): ease-out over 1.9 s, each rank's color taken
    /// over 300 ms as it's reached, a swell at each, the head fading once the fill settles and the backdrop wobbing hard
    /// as it does.
    /// </summary>
    private partial class RevealDriver : Component
    {
        private const double duration = 1900;

        private readonly BlossomRing ring;
        private readonly List<(ScoreRank Rank, double Time)> crossings = new List<(ScoreRank, double)>();

        private double start = double.NaN;
        private bool landed;
        private float target;
        private double pullStart = double.NaN;
        private float pullFrom;
        private float pullTo;
        private Color pullColor;

        public bool FullBloom;

        public RevealDriver(BlossomRing ring)
        {
            this.ring = ring;
            ring.ThresholdCrossed += rank => crossings.Add((rank, Clock.CurrentTime));
        }

        public void Play(float accuracy)
        {
            ring.Reset();
            crossings.Clear();
            FullBloom = false;
            pullStart = double.NaN;
            target = accuracy;
            start = Clock.CurrentTime;
            landed = false;
        }

        public void Stop() => start = double.NaN;

        public void PullBackTo(float fill, Color color)
        {
            pullStart = Clock.CurrentTime;
            pullFrom = ring.Fill;
            pullTo = fill;
            pullColor = color;
        }

        public override void Update()
        {
            base.Update();

            if (double.IsNaN(start))
                return;

            double now = Clock.CurrentTime;
            float x = (float)Math.Clamp((now - start) / duration, 0, 1);
            float eased = 1 - (1 - x) * (1 - x);

            if (x >= 1 && !landed)
            {
                landed = true;
                ring.Backdrop.Wob(start + duration, 1.6f);
            }

            if (double.IsNaN(pullStart))
                ring.Fill = target * eased;
            else
            {
                float p = (float)Math.Clamp((now - pullStart) / 450, 0, 1);
                ring.Fill = pullFrom + (pullTo - pullFrom) * (1 - MathF.Pow(1 - p, 3));
            }

            // The rank reached so far, its color blended in from the last one's over 300 ms.
            Color color = JudgementColors.For(ScoreRank.D);
            float pulse = 0;

            foreach ((ScoreRank rank, double time) in crossings)
            {
                color = color.Blend(JudgementColors.For(rank), (float)Math.Clamp((now - time) / 300, 0, 1));
                pulse = Math.Max(pulse, MathF.Exp((float)(-(now - time) / 130)));
            }

            if (!double.IsNaN(pullStart))
                color = JudgementColors.For(ScoreRank.S).Blend(pullColor, (float)Math.Clamp((now - pullStart) / 300, 0, 1));

            ring.FillColor = FullBloom ? HeroColors.Cycle(now * 1.4, white: 0) : color;
            ring.Pulse = pulse;
            ring.HeadAlpha = x < 1 ? 1 : Math.Max(0, 1 - (float)((now - start - duration) / 300));
            ring.Glow = 0.9f * MathF.Exp((float)(-Math.Max(0, now - start - duration) / 500)) * (x < 1 ? x : 1);
        }
    }
}
