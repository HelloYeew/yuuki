// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// The best-mark chip: a rank blade in the rank's color, with a medal blade joined on for a full combo or
/// an all perfect.
/// </summary>
[TestFixture]
public class BestMarkTest
{
    private static readonly ScoreRank[] all_ranks = Enum.GetValues<ScoreRank>();

    [Test]
    public void NoBestIsNoChip() => Assert.That(BestMark.PiecesFor(null), Is.Null);

    [Test]
    public void AClearIsJustItsRank()
    {
        var pieces = BestMark.PiecesFor(new ScoreInfo { Rank = ScoreRank.S })!.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pieces.Rank.Label, Is.EqualTo("S"));
            Assert.That(pieces.Rank.Fill, Is.EqualTo(SetlistColors.TierFill(JudgementColors.For(ScoreRank.S))));
            Assert.That(pieces.Medal, Is.Null);
        }
    }

    [Test]
    public void AFullComboJoinsOnItsMedal()
    {
        var pieces = BestMark.PiecesFor(new ScoreInfo { Rank = ScoreRank.A, IsFullCombo = true })!.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pieces.Rank.Label, Is.EqualTo("A"));
            Assert.That(pieces.Medal?.Label, Is.EqualTo("FC"));
            Assert.That(pieces.Medal?.Fill, Is.EqualTo(SetlistColors.TierFill(JudgementColors.FULL_COMBO)));
        }
    }

    [Test]
    public void AnAllPerfectOutranksTheFullCombo()
    {
        var pieces = BestMark.PiecesFor(new ScoreInfo { Rank = ScoreRank.S, IsFullCombo = true, IsAllPerfect = true })!.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pieces.Rank.Label, Is.EqualTo("S"));
            Assert.That(pieces.Medal?.Label, Is.EqualTo("AP"));
            Assert.That(pieces.Medal?.Fill, Is.EqualTo(SetlistColors.TierFill(JudgementColors.ALL_PERFECT)));
        }
    }

    /// <summary>
    /// A medal sharing its rank's color would merge into one block: FC was B's blue, and AP was S's gold.
    /// Each medal must be at least as far from every rank as the closest two ranks (B and C) are from each
    /// other, which is about 40.
    /// </summary>
    [Test]
    public void TheMedalsStandApartFromEveryRank()
    {
        const double min_distance = 30;

        using (Assert.EnterMultipleScope())
        {
            foreach (var rank in all_ranks)
            {
                Assert.That(distance(JudgementColors.FULL_COMBO, JudgementColors.For(rank)), Is.GreaterThan(min_distance), $"FC against {rank}");
                Assert.That(distance(JudgementColors.ALL_PERFECT, JudgementColors.For(rank)), Is.GreaterThan(min_distance), $"AP against {rank}");
            }

            Assert.That(distance(JudgementColors.FULL_COMBO, JudgementColors.ALL_PERFECT), Is.GreaterThan(min_distance), "FC against AP");
        }
    }

    [Test]
    public void EveryBladeReads()
    {
        var scores = all_ranks.SelectMany(rank => new[]
        {
            new ScoreInfo { Rank = rank },
            new ScoreInfo { Rank = rank, IsFullCombo = true },
            new ScoreInfo { Rank = rank, IsFullCombo = true, IsAllPerfect = true }
        });

        using (Assert.EnterMultipleScope())
        {
            foreach (var score in scores)
            {
                var pieces = BestMark.PiecesFor(score)!.Value;

                foreach (var piece in new[] { pieces.Rank, pieces.Medal }.OfType<BestMark.Piece>())
                    Assert.That(contrast(piece.Fill, piece.Ink), Is.GreaterThanOrEqualTo(4.5), $"{piece.Label} on {score.Rank}");
            }
        }
    }

    /// <summary>
    /// How different two colors look: the distance between them in CIELAB (CIE76).
    /// </summary>
    private static double distance(Color a, Color b)
    {
        var (l1, a1, b1) = lab(a);
        var (l2, a2, b2) = lab(b);
        return Math.Sqrt(Math.Pow(l1 - l2, 2) + Math.Pow(a1 - a2, 2) + Math.Pow(b1 - b2, 2));
    }

    private static (double L, double A, double B) lab(Color c)
    {
        double r = linear(c.R), g = linear(c.G), b = linear(c.B);

        double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;

        static double f(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16 / 116.0;

        return (116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z)));
    }

    private static double contrast(Color a, Color b)
    {
        double la = luminance(a), lb = luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double luminance(Color c) => 0.2126 * linear(c.R) + 0.7152 * linear(c.G) + 0.0722 * linear(c.B);

    private static double linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
