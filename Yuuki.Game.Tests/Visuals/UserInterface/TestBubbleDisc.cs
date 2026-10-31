// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestBubbleDisc : YuukiTestScene
{
    private BubbleDisc bubble = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the bubble", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(bubble = new BubbleDisc
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(600),
                DiscColor = ColorExtensions.FromHex("#151D30"),
                DiscAlpha = 0.5f,
                LightColor = JudgementColors.For(ScoreRank.A).Blend(Color.White, 0.35f)
            });
            // Its near side, over what would be inside it (nothing here).
            Add(bubble.Front);
        });
    }

    [Test]
    public void TestBubble()
    {
        AddSliderStep("wobble", 0f, 3f, 1f, wobble => bubble.Wobble = wobble);
        AddSliderStep("ripples", 0f, 3f, 1f, ripples => bubble.Ripples = ripples);
        AddSliderStep("film", 0f, 3f, 1f, sheen => bubble.Sheen = sheen);
        AddSliderStep("glow", 0f, 3f, 1f, glow => bubble.Glow = glow);
        AddSliderStep("wobs by itself every (ms; 0 never)", 0, 6000, 2400, interval => bubble.IdleInterval = interval);
        AddSliderStep("zoom", 0.3f, 3f, 1f, zoom => bubble.Scale = new Vector2(zoom));

        AddStep("wob", () => bubble.Wob());
        AddWaitStep("watch", 1500);
        AddStep("a hard wob (a rank landing)", () => bubble.Wob(strength: 2));
        AddWaitStep("watch", 1500);
        AddStep("ripples in the S's color", () => bubble.LightColor = JudgementColors.For(ScoreRank.S).Blend(Color.White, 0.35f));
        AddStep("ripples in the All Perfect's", () => bubble.LightColor = JudgementColors.ALL_PERFECT.Blend(Color.White, 0.35f));
        AddStep("grey (a failed run)", () => bubble.Greyed = true);
        AddStep("colors back", () => bubble.Greyed = false);
    }

    /// <summary>
    /// A wob throws the edge out of round, and it settles round again; its ripple runs out and is gone.
    /// </summary>
    [Test]
    public void TestAWobSettles()
    {
        AddStep("no wobs by itself", () => bubble.IdleInterval = 0);
        AddStep("a wob", () => bubble.Wob(strength: 2));
        AddUntilStep("out of round", () => bubble.OutOfRound > 0.02f);
        AddAssert("one ripple", () => bubble.ActiveRipples == 1);
        AddUntilStep("round again", () => bubble.OutOfRound < 0.015f && bubble.ActiveRipples == 0);
    }

    /// <summary>
    /// Told to wob at a time already past, the disc shows as it would be by now: a wob two seconds ago has settled, one
    /// a moment ago is still rippling.
    /// </summary>
    [Test]
    public void TestWobbingInThePast()
    {
        AddStep("no wobs by itself, and no idle wobble", () =>
        {
            bubble.IdleInterval = 0;
            bubble.Wobble = 0;
        });
        AddStep("a wob two seconds ago", () => bubble.Wob(bubble.Clock.CurrentTime - 2000));
        AddAssert("no ripple", () => bubble.ActiveRipples == 0);
        AddStep("a wob a moment ago", () => bubble.Wob(bubble.Clock.CurrentTime - 300));
        AddAssert("a ripple", () => bubble.ActiveRipples == 1);
        AddAssert("kept round with no wobble", () => bubble.OutOfRound == 0);
    }
}
