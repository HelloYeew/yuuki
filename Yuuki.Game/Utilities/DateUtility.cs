// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Globalization;

namespace Yuuki.Game.Utilities;

/// <summary>
/// Helper methods for showing dates.
/// </summary>
public static class DateUtility
{
    /// <summary>
    /// When something was played, in local time: "18 Oct 2026, 06:13".
    /// </summary>
    public static string FormatPlayed(DateTimeOffset date) => string.Create(CultureInfo.InvariantCulture, $"{date.LocalDateTime:d MMM yyyy, HH:mm}");

    /// <summary>
    /// How long ago, to the second while it's recent, then in the two largest units:
    /// "just now", "42s ago", "5m 12s ago", "3h 7m ago", "2d 4h ago", "4mo 2d ago", "1y 3mo ago".
    /// </summary>
    public static string Ago(DateTimeOffset date) => Ago(date, DateTimeOffset.Now);

    /// <inheritdoc cref="Ago(DateTimeOffset)"/>
    public static string Ago(DateTimeOffset date, DateTimeOffset now)
    {
        TimeSpan age = now - date;

        if (age.TotalSeconds < 1)
            return "just now";
        if (age.TotalMinutes < 1)
            return $"{age.Seconds}s ago";
        if (age.TotalHours < 1)
            return $"{age.Minutes}m {age.Seconds}s ago";
        if (age.TotalDays < 1)
            return $"{age.Hours}h {age.Minutes}m ago";
        if (age.TotalDays < 30)
            return $"{age.Days}d {age.Hours}h ago";

        int days = (int)age.TotalDays;
        if (days < 365)
            return $"{days / 30}mo {days % 30}d ago";

        return $"{days / 365}y {days % 365 / 30}mo ago";
    }
}
