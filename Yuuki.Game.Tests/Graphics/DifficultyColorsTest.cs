// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// Difficulty colors: a gradient through the tiers' own colors, from one place.
/// </summary>
[TestFixture]
public class DifficultyColorsTest
{
    [Test]
    public void EachTierWearsItsOwnColorAtItsMiddle()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DifficultyColors.For(2), Is.EqualTo(DifficultyColors.BASIC));
            Assert.That(DifficultyColors.For(6), Is.EqualTo(DifficultyColors.ADVANCE));
            Assert.That(DifficultyColors.For(10), Is.EqualTo(DifficultyColors.EXPERT));
            Assert.That(DifficultyColors.For(14), Is.EqualTo(DifficultyColors.MASTER));
            Assert.That(DifficultyColors.For(18), Is.EqualTo(DifficultyColors.VIRTUOSO));
        }
    }

    /// <summary>
    /// The point of the gradient: a 7.9 and an 8.0 are neighbours, not two tiers apart. The old colors
    /// jumped from Advance's cyan to Expert's yellow right there.
    /// </summary>
    [Test]
    public void NeighbouringRatingsLookAlike()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(difference(DifficultyColors.For(7.99), DifficultyColors.For(8.0)), Is.LessThanOrEqualTo(2));
            Assert.That(difference(DifficultyColors.For(3.99), DifficultyColors.For(4.0)), Is.LessThanOrEqualTo(2));
        }
    }

    [Test]
    public void ATierEdgeIsBetweenItsNeighbours()
    {
        var edge = DifficultyColors.For(8);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(edge, Is.Not.EqualTo(DifficultyColors.ADVANCE));
            Assert.That(edge, Is.Not.EqualTo(DifficultyColors.EXPERT));
            Assert.That(difference(edge, DifficultyColors.ADVANCE), Is.GreaterThan(20), "well on its way from Advance");
            Assert.That(difference(edge, DifficultyColors.EXPERT), Is.GreaterThan(20), "and not yet Expert");
        }
    }

    [Test]
    public void PastTheEndsItHolds()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DifficultyColors.For(0), Is.EqualTo(DifficultyColors.BASIC));
            Assert.That(DifficultyColors.For(30), Is.EqualTo(DifficultyColors.VIRTUOSO));
        }
    }

    [Test]
    public void ATierAndALevelHaveTheirOwnColors()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DifficultyColors.ForTier(DifficultyLevel.Expert), Is.EqualTo(DifficultyColors.EXPERT));
            Assert.That(DifficultyColors.ForLevel(7), Is.EqualTo(DifficultyColors.For(7.5)), "a level is the middle of its ratings");
            Assert.That(DifficultyColors.For(new Beatmap { DifficultyRating = 11.2 }), Is.EqualTo(DifficultyColors.For(11.2)));
        }
    }

    /// <summary>
    /// The largest difference in any one channel, 0 to 255.
    /// </summary>
    private static int difference(Color a, Color b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
}
