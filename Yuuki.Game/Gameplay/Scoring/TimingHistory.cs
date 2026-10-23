// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// What a play's timing says against the plays before it: whether the player leans the same way on every song, which
/// shifting the timing fixes, or only on this one, which its song offset fixes.
/// </summary>
/// <remarks>
/// A single play's lean comes and goes by ten-odd ms with the song and the player, so one play alone has to lean a long
/// way before it's worth acting on, and a few together much less. Each play's lean is taken as it would be with the
/// offsets in use now (<see cref="LeanNow"/>), so a shift already made doesn't count again.
/// </remarks>
public class TimingHistory
{
    /// <summary>The most recent plays of other difficulties the player's own lean is taken from.</summary>
    public const int RECENT_PLAYS = 10;

    /// <summary>The most recent plays of the difficulty its own lean is taken from.</summary>
    public const int SONG_PLAYS = 5;

    /// <summary>Plays of other difficulties needed before a song's lean can be told from the player's.</summary>
    public const int PLAYS_FOR_SONG_LEAN = 3;

    /// <summary>The smallest shift worth suggesting, however many plays agree on it.</summary>
    public const double MINIMUM_SHIFT = 4;

    /// <summary>
    /// How far one play alone has to lean before it's worth suggesting a shift: as far as a play leans by chance now
    /// and then. Over <c>n</c> plays it's this over √n, as their median wanders less.
    /// </summary>
    public const double ONE_PLAY_SHIFT = 20;

    /// <summary>The smallest song offset worth suggesting: a lean a song's own timing would rarely cause less of.</summary>
    public const double MINIMUM_SONG_SHIFT = 10;

    private readonly Func<string?, int> audioOffsetNow;
    private readonly Func<int, int> songOffsetNow;

    /// <param name="audioOffsetNow">The audio offset an audio device (by name) would be played with now.</param>
    /// <param name="songOffsetNow">The song offset a difficulty (by ID) would be played with now.</param>
    public TimingHistory(Func<string?, int> audioOffsetNow, Func<int, int> songOffsetNow)
    {
        this.audioOffsetNow = audioOffsetNow;
        this.songOffsetNow = songOffsetNow;
    }

    /// <summary>
    /// How late <paramref name="play"/> would have been on average with the offsets in use now: its mean error, plus
    /// however much later its device's audio offset and its song offset judge now. Null where it has no mean error.
    /// </summary>
    public double? LeanNow(ScoreInfo play) =>
        play.MeanError is double mean
            ? mean + (audioOffsetNow(play.AudioDevice) - play.AudioOffset) + (songOffsetNow(play.BeatmapId) - play.SongOffset)
            : null;

    /// <summary>
    /// What to suggest after <paramref name="play"/>, given the plays before it (in the order they were played).
    /// </summary>
    public OffsetSuggestion Suggest(ScoreInfo play, IEnumerable<ScoreInfo> earlier)
    {
        if (LeanNow(play) is not double lean)
            return OffsetSuggestion.None;

        List<ScoreInfo> earlierPlays = earlier.Where(p => p != play && p.MeanError != null).ToList();

        List<double> others = earlierPlays.Where(p => p.BeatmapId != play.BeatmapId)
                                          .TakeLast(RECENT_PLAYS)
                                          .Select(p => LeanNow(p)!.Value)
                                          .ToList();

        // This difficulty against the player's other songs, once there are enough of those to know the player by.
        if (others.Count >= PLAYS_FOR_SONG_LEAN)
        {
            List<double> song = earlierPlays.Where(p => p.BeatmapId == play.BeatmapId)
                                            .TakeLast(SONG_PLAYS - 1)
                                            .Select(p => LeanNow(p)!.Value)
                                            .Append(lean)
                                            .ToList();

            double songLean = Median(song) - Median(others);

            if (Math.Abs(songLean) >= Math.Max(MINIMUM_SONG_SHIFT, ONE_PLAY_SHIFT / Math.Sqrt(song.Count)))
                return new OffsetSuggestion(OffsetSuggestionKind.Song, (int)Math.Round(songLean), song.Count);
        }

        List<double> player = others.Append(lean).ToList();
        double playerLean = Median(player);

        if (Math.Abs(playerLean) >= Math.Max(MINIMUM_SHIFT, ONE_PLAY_SHIFT / Math.Sqrt(player.Count)))
            return new OffsetSuggestion(OffsetSuggestionKind.Timing, (int)Math.Round(playerLean), player.Count);

        return OffsetSuggestion.None;
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        double[] sorted = values.Order().ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}

public enum OffsetSuggestionKind
{
    None,

    /// <summary>The player leans the same way on every song: shift the judging on every device.</summary>
    Timing,

    /// <summary>Only this difficulty leans: its song offset.</summary>
    Song
}

/// <summary>
/// An offset worth changing, and by how much.
/// </summary>
/// <param name="Kind">Which offset.</param>
/// <param name="Lean">How late the plays leaned, in ms (negative: early); what the change takes off.</param>
/// <param name="Plays">How many plays it was worked out from.</param>
public readonly record struct OffsetSuggestion(OffsetSuggestionKind Kind, int Lean, int Plays)
{
    public static readonly OffsetSuggestion None = new OffsetSuggestion(OffsetSuggestionKind.None, 0, 0);
}
