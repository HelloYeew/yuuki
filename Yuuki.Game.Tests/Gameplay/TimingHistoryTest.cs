// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Tests.Gameplay;

/// <summary>
/// What the results screen suggests from a play and the ones before it (<see cref="TimingHistory"/>).
/// </summary>
[TestFixture]
public class TimingHistoryTest
{
    private const int song = 1;

    // Played as the offsets are now, unless a test says otherwise.
    private int audioNow;
    private readonly Dictionary<int, int> songOffsetsNow = new Dictionary<int, int>();

    private TimingHistory history = null!;

    [SetUp]
    public void SetUp()
    {
        audioNow = 0;
        songOffsetsNow.Clear();
        history = new TimingHistory(_ => audioNow, id => songOffsetsNow.GetValueOrDefault(id));
    }

    private static ScoreInfo play(double lean, int beatmapId = song, int audioOffset = 0, int songOffset = 0) => new ScoreInfo
    {
        BeatmapId = beatmapId,
        MeanError = lean,
        AudioOffset = audioOffset,
        SongOffset = songOffset
    };

    // Plays of other songs, one each, leaning as given.
    private static IEnumerable<ScoreInfo> otherSongs(params double[] leans) => leans.Select((lean, i) => play(lean, beatmapId: 100 + i));

    [Test]
    public void TestOnePlayHasToLeanFar()
    {
        Assert.That(history.Suggest(play(17), []), Is.EqualTo(OffsetSuggestion.None));
        Assert.That(history.Suggest(play(25), []), Is.EqualTo(new OffsetSuggestion(OffsetSuggestionKind.Timing, 25, 1)));
        Assert.That(history.Suggest(play(-25), []), Is.EqualTo(new OffsetSuggestion(OffsetSuggestionKind.Timing, -25, 1)));
    }

    [Test]
    public void TestManyPlaysAgreeOnASmallLean()
    {
        var earlier = otherSongs(6, 9, 7, 10, 8, 6, 9, 7, 10, 8).ToList();

        OffsetSuggestion suggestion = history.Suggest(play(8), earlier);

        Assert.That(suggestion.Kind, Is.EqualTo(OffsetSuggestionKind.Timing));
        Assert.That(suggestion.Lean, Is.EqualTo(8));
        Assert.That(suggestion.Plays, Is.EqualTo(11));
    }

    [Test]
    public void TestPlaysThatWanderSuggestNothing()
    {
        // About the user's own: a few ms late overall, ten-odd either way from play to play.
        var earlier = otherSongs(-7.5, 10.2, 3.0, 0.6, 6.6, 1.0, -15.4, -3.1, 9.8, 2.3).ToList();

        Assert.That(history.Suggest(play(8), earlier).Kind, Is.EqualTo(OffsetSuggestionKind.None));
    }

    [Test]
    public void TestOneSongLeaningAloneIsTheSong()
    {
        var earlier = otherSongs(1, -2, 0, 2, -1).ToList();

        OffsetSuggestion suggestion = history.Suggest(play(30), earlier);

        Assert.That(suggestion.Kind, Is.EqualTo(OffsetSuggestionKind.Song));
        Assert.That(suggestion.Lean, Is.EqualTo(30));
    }

    [Test]
    public void TestASongNeedsOtherSongsToCompareWith()
    {
        Assert.That(history.Suggest(play(30), otherSongs(0, 0)).Kind, Is.Not.EqualTo(OffsetSuggestionKind.Song));
    }

    [Test]
    public void TestASongsEarlierPlaysCount()
    {
        var earlier = otherSongs(1, -2, 0, 2, -1).Concat(new[] { play(12), play(14) }).ToList();

        // 13 ms over three plays of it is worth it; it wouldn't be from one.
        Assert.That(history.Suggest(play(13), earlier), Is.EqualTo(new OffsetSuggestion(OffsetSuggestionKind.Song, 13, 3)));
        Assert.That(history.Suggest(play(13), otherSongs(1, -2, 0, 2, -1)).Kind, Is.EqualTo(OffsetSuggestionKind.None));
    }

    [Test]
    public void TestAShiftAlreadyMadeDoesntCountAgain()
    {
        // Ten plays 10 ms late at an audio offset of 0, then shifted to -10.
        var earlier = otherSongs(10, 10, 10, 10, 10, 10, 10, 10, 10, 10).ToList();
        audioNow = -10;

        Assert.That(history.LeanNow(earlier[0]), Is.EqualTo(0));
        Assert.That(history.Suggest(play(0, audioOffset: -10), earlier).Kind, Is.EqualTo(OffsetSuggestionKind.None));
    }

    [Test]
    public void TestASongOffsetAlreadySetDoesntCountAgain()
    {
        var earlier = otherSongs(0, 0, 0, 0, 0).Concat(new[] { play(30), play(30) }).ToList();
        songOffsetsNow[song] = -30;

        Assert.That(history.Suggest(play(0, songOffset: -30), earlier).Kind, Is.EqualTo(OffsetSuggestionKind.None));
    }

    [Test]
    public void TestPlaysWithoutAMeanAreLeftOut()
    {
        var earlier = otherSongs(1, -2, 0).Append(new ScoreInfo { BeatmapId = 200 }).ToList();

        Assert.That(history.Suggest(new ScoreInfo { BeatmapId = song }, earlier), Is.EqualTo(OffsetSuggestion.None));
        Assert.That(history.Suggest(play(30), earlier).Kind, Is.EqualTo(OffsetSuggestionKind.Song));
    }
}
