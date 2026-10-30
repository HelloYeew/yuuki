// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestBlossomFlower : YuukiTestScene
{
    private Container stage = null!;
    private BlossomFlower[] flowers = null!;
    private BlossomFlower closedBud = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show flowers", () =>
        {
            Clear();

            Color[] colors = { HeroColors.KARIN.Main, HeroColors.SONOKO.Main, HeroColors.MIMORI.Main, HeroColors.ITSUKI.Main, HeroColors.FUU.Main };

            flowers = colors.Select((c, i) => new BlossomFlower
            {
                X = -260 + i * 110,
                FlowerColor = c,
                FlowerSize = 0.85f,
                FlowerTwist = i * 13,
                // Pointing out from a ring below them, as on the branch.
                BudDirection = -30 + i * 15
            }).Concat(new[]
            {
                closedBud = new BlossomFlower
                {
                    X = 300,
                    FlowerColor = HeroColors.FUU.Main,
                    FlowerSize = 0.45f,
                    BudDirection = -30,
                    StaysClosed = true,
                    StemFrom = new Vector2(-20, 28)
                },
                new BlossomFlower
                {
                    X = 420,
                    FlowerColor = HeroColors.YUUNA.Main,
                    FlowerSize = 1.3f,
                    BudDirection = -28
                }
            }).ToArray();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(stage = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Scale = new Vector2(1.8f),
                // The stems under the flowers, as on the ring.
                Children = flowers.Select(f => f.Stem).Concat(flowers).ToArray()
            });
        });
    }

    [Test]
    public void TestOpening()
    {
        AddSliderStep("zoom", 0.5f, 5f, 1.8f, scale => stage.Scale = new Vector2(scale));
        AddSliderStep("grown out", 0f, 1f, 1f, grow =>
        {
            foreach (BlossomFlower flower in flowers)
                flower.Grow = grow;
        });
        AddSliderStep("beat", 0f, 1f, 0f, beat =>
        {
            foreach (BlossomFlower flower in flowers)
                flower.Beat = beat;
        });

        AddStep("open, one after another", () =>
        {
            for (int i = 0; i < flowers.Length; i++)
                flowers[i].Open(flowers[i].Clock.CurrentTime + i * 90);
        });
        AddWaitStep("let them open", 1200);
        AddAssert("the closed bud is still a bud", () => !closedBud.IsOpen);
        AddStep("wilt the S", () => flowers[4].Wilt());
        AddWaitStep("let it fall", 1200);
        AddStep("Full Bloom wave", () =>
        {
            for (int i = 0; i < flowers.Length; i++)
                flowers[i].BloomWave(flowers[i].Clock.CurrentTime + i * 60, i * 60);
        });
        AddStep("grey (a failed run)", () =>
        {
            foreach (BlossomFlower flower in flowers)
                flower.Greyed = true;
        });
        AddStep("colors back", () =>
        {
            foreach (BlossomFlower flower in flowers)
                flower.Greyed = false;
        });
        AddStep("close all", () =>
        {
            foreach (BlossomFlower flower in flowers)
                flower.Close();
        });
    }

    /// <summary>
    /// Told to open at a time already past, a flower shows as it would be by now: open, its bud gone.
    /// </summary>
    [Test]
    public void TestOpeningInThePastLandsOpen()
    {
        AddStep("open a second ago", () =>
        {
            foreach (BlossomFlower flower in flowers)
                flower.Open(flower.Clock.CurrentTime - 1000);
        });
        AddAssert("every flower but the closed bud is open", () => flowers.Where(f => f != closedBud).All(f => f.IsOpen));
        AddStep("close all", () =>
        {
            foreach (BlossomFlower flower in flowers)
                flower.Close();
        });
        AddAssert("closed again", () => flowers.All(f => !f.IsOpen));
    }
}
