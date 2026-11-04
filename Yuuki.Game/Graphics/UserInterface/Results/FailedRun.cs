// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// What a results screen says of a run that ran out of health as how far it got, as a share of the song and as a time.
/// </summary>
/// <remarks>
/// The share is rounded down, so a run that didn't quite get there never reads as 100%.
/// </remarks>
public static class FailedRun
{
    /// <summary>
    /// The share reached, as a whole percent, rounded down.
    /// </summary>
    public static int Percent(double reached) => (int)Math.Floor(Math.Clamp(reached, 0, 1) * 100 + 1e-9);

    /// <summary>
    /// The footer's line while the run is shown: Failed · reached 72% of the song, or Failed where how far isn't known.
    /// </summary>
    public static string Footer(double? reached) => reached is double r ? $"Failed · reached {Percent(r)}% of the song" : "Failed";

    /// <summary>
    /// Under the accuracy in the ring: reached 1:47 of 2:30.
    /// </summary>
    public static string Ring(double failedAt, double length) => $"reached {Time(failedAt)} of {Time(length)}";

    /// <summary>
    /// Where the run stands among the plays: Failed at 1:47 · not ranked, or without the time where it isn't known.
    /// </summary>
    public static string Standing(double? failedAt) => failedAt is double at ? $"Failed at {Time(at)} · not ranked" : "Failed · not ranked";

    /// <summary>
    /// Under it: how many tries the song has had today, and the best that stays as it was (or that there's none yet).
    /// </summary>
    public static string Tries(int triesToday, long? bestScore, double? bestAccuracy)
    {
        string tries = $"Try {Math.Max(1, triesToday).ToString(CultureInfo.InvariantCulture)} today";

        if (bestScore is not long score || bestAccuracy is not double accuracy)
            return $"{tries} · no best yet";

        return $"{tries} · your best stays {score.ToString("0000000", CultureInfo.InvariantCulture)} · "
               + $"{(accuracy * 100).ToString("0.00", CultureInfo.InvariantCulture)}%";
    }

    /// <summary>
    /// m:ss, as the graphs write a time in the song.
    /// </summary>
    public static string Time(double ms)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }
}
