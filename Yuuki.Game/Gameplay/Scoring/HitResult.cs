// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// The graded outcome of a note, from best to worst.
/// </summary>
public enum HitResult
{
    /// <summary>
    /// No judgement yet.
    /// </summary>
    None,

    /// <summary>
    /// Best timing.
    /// </summary>
    Critical,

    /// <summary>
    /// Good timing.
    /// </summary>
    Break,

    /// <summary>
    /// Loose but valid timing.
    /// </summary>
    Hit,

    /// <summary>
    /// Missed (too far off, or never pressed).
    /// </summary>
    Miss
}

public static class HitResultExtensions
{
    /// <summary>
    /// Whether this result counts as a successful hit (keeps/extends combo).
    /// </summary>
    public static bool IsHit(this HitResult result) => result is HitResult.Critical or HitResult.Break or HitResult.Hit;

    /// <summary>
    /// How much of a note this result is worth, 0–1, for both accuracy and the accuracy part of the
    /// score: Critical 1, Break ⅔, Hit ⅓, Miss 0.
    /// </summary>
    public static double Value(this HitResult result) => result switch
    {
        HitResult.Critical => 1,
        HitResult.Break => 2.0 / 3,
        HitResult.Hit => 1.0 / 3,
        _ => 0
    };
}
