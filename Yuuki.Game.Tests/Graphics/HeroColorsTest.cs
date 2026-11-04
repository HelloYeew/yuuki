// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Graphics;

[TestFixture]
public class HeroColorsTest
{
    [Test]
    public void TheCycleStartsOnYuunaAndMovesAHeroAtATime()
    {
        double oneHero = 1000 / HeroColors.CYCLE_PER_SECOND;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeroColors.Cycle(0, white: 0), Is.EqualTo(HeroColors.YUUNA.Main));
            Assert.That(HeroColors.Cycle(oneHero, white: 0), Is.EqualTo(HeroColors.FUU.Main));
            Assert.That(HeroColors.Cycle(5 * oneHero, white: 0), Is.EqualTo(HeroColors.KARIN.Main));
            // Round again, past Karin back to Yuuna.
            Assert.That(HeroColors.Cycle(6 * oneHero, white: 0), Is.EqualTo(HeroColors.YUUNA.Main));
        }
    }

    [Test]
    public void AHueStartsFurtherRound()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeroColors.Cycle(0, hue: 60, white: 0), Is.EqualTo(HeroColors.FUU.Main));
            Assert.That(HeroColors.Cycle(0, hue: -60, white: 0), Is.EqualTo(HeroColors.KARIN.Main));
            Assert.That(HeroColors.Cycle(1000 / HeroColors.CYCLE_PER_SECOND / 2, white: 0, speed: 2), Is.EqualTo(HeroColors.FUU.Main));
        }
    }

    /// <summary>
    /// Between two heroes it blends: halfway from Yuuna to Fuu is neither.
    /// </summary>
    [Test]
    public void TheCycleBlendsBetweenHeroes()
    {
        Color halfway = HeroColors.Cycle(500 / HeroColors.CYCLE_PER_SECOND, white: 0);
        Color expected = HeroColors.YUUNA.Main.Blend(HeroColors.FUU.Main, 0.5f);

        Assert.That(difference(halfway, expected), Is.LessThanOrEqualTo(1));
    }

    [Test]
    public void WhiteSoftensTheColors()
    {
        Color plain = HeroColors.Cycle(0, white: 0);
        Color soft = HeroColors.Cycle(0);

        Assert.That(soft.GetBrightness(), Is.GreaterThan(plain.GetBrightness()));
    }

    [Test]
    public void EachRankWearsTheHeroNearestItsColor()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeroColors.ForRank(ScoreRank.D), Is.EqualTo(HeroColors.KARIN));
            Assert.That(HeroColors.ForRank(ScoreRank.C), Is.EqualTo(HeroColors.SONOKO));
            Assert.That(HeroColors.ForRank(ScoreRank.B), Is.EqualTo(HeroColors.MIMORI));
            Assert.That(HeroColors.ForRank(ScoreRank.A), Is.EqualTo(HeroColors.ITSUKI));
            Assert.That(HeroColors.ForRank(ScoreRank.S), Is.EqualTo(HeroColors.FUU));
        }
    }

    [Test]
    public void TheRainbowRepeatsSeamlessly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RainbowColors.Sample(RainbowColors.PASTEL, 0), Is.EqualTo(RainbowColors.PASTEL[0]));
            Assert.That(RainbowColors.Sample(RainbowColors.PASTEL, 1), Is.EqualTo(RainbowColors.PASTEL[0]));
            Assert.That(RainbowColors.Sample(RainbowColors.PASTEL, -0.5), Is.EqualTo(RainbowColors.Sample(RainbowColors.PASTEL, 0.5)));
            Assert.That(RainbowColors.Sample(RainbowColors.INK, 1.0 / 6), Is.EqualTo(RainbowColors.INK[1]));
        }
    }

    [Test]
    public void TheRainbowMovesOneRunAPeriod()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(RainbowColors.Phase(0), Is.EqualTo(0));
            Assert.That(RainbowColors.Phase(RainbowColors.PERIOD / 4), Is.EqualTo(0.25).Within(1e-9));
            Assert.That(RainbowColors.Phase(RainbowColors.PERIOD * 1.5), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(RainbowColors.Phase(RainbowColors.PERIOD, speed: 0), Is.EqualTo(0));
        }
    }

    private static int difference(Color a, Color b) => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)), Math.Abs(a.B - b.B));
}
