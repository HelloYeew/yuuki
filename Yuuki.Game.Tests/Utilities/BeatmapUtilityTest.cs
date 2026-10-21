// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Utilities;

[TestFixture]
public class BeatmapUtilityTest
{
    [Test]
    public void LevelsShowOneDecimalPlace()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(BeatmapUtility.FormatLevel(13.4), Is.EqualTo("13.4"));
            Assert.That(BeatmapUtility.FormatLevel(7), Is.EqualTo("7.0"), "a whole level still shows its decimal");
            Assert.That(BeatmapUtility.FormatLevel(2.3), Is.EqualTo("2.3"), "not 2.2 from 2.3 * 10 falling just short of 23");
        }
    }

    /// <summary>
    /// Rounded down: 13.96 is filtered and sorted as a level 13, so it mustn't read 14.0.
    /// </summary>
    [Test]
    public void LevelsRoundDownToStayInTheirLevel()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(BeatmapUtility.FormatLevel(13.96), Is.EqualTo("13.9"));
            Assert.That(BeatmapUtility.FormatLevel(0.04), Is.EqualTo("0.0"));
        }
    }

    [Test]
    public void LevelRangeSpansTheSongsDifficulties()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(BeatmapUtility.FormatLevelRange(new[] { beatmap(2), beatmap(13.45), beatmap(7) }), Is.EqualTo("2.0–13.4"));
            Assert.That(BeatmapUtility.FormatLevelRange(new[] { beatmap(5.5), beatmap(5.55) }), Is.EqualTo("5.5"), "the same as shown");
            Assert.That(BeatmapUtility.FormatLevelRange(new Beatmap[0]), Is.Empty);
        }
    }

    private static Beatmap beatmap(double rating) => new Beatmap { DifficultyRating = rating };
}
