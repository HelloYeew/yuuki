// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// The graded outcome of a note: <see cref="Bloom"/> best, then Critical to Miss. Saves keep these as numbers, so a new
/// one goes last (Bloom does); <see cref="HitResultExtensions.Grade"/> orders them by how good they are.
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
    Miss,

    /// <summary>
    /// Better than Critical: half its window. It counts as a Critical for accuracy, combo and health, and adds a point of
    /// score past 1,000,000. Every note a Bloom is a Full Bloom.
    /// </summary>
    Bloom
}

public static class HitResultExtensions
{
    /// <summary>
    /// Whether this result counts as a successful hit (keeps/extends combo).
    /// </summary>
    public static bool IsHit(this HitResult result) => result is HitResult.Bloom or HitResult.Critical or HitResult.Break or HitResult.Hit;

    /// <summary>
    /// Whether this is the best timing there is, or the next: a Bloom or a Critical, which count the same.
    /// </summary>
    public static bool IsCritical(this HitResult result) => result is HitResult.Bloom or HitResult.Critical;

    /// <summary>
    /// How good this result is, for comparing two: Bloom 4, Critical 3, Break 2, Hit 1, Miss 0 (None −1). The enum's own
    /// order isn't this: Bloom was added last.
    /// </summary>
    public static int Grade(this HitResult result) => result switch
    {
        HitResult.Bloom => 4,
        HitResult.Critical => 3,
        HitResult.Break => 2,
        HitResult.Hit => 1,
        HitResult.Miss => 0,
        _ => -1
    };

    /// <summary>
    /// How much of a note this result is worth, 0–1, for both accuracy and the accuracy part of the
    /// score: Bloom and Critical 1, Break ⅔, Hit ⅓, Miss 0.
    /// </summary>
    public static double Value(this HitResult result) => result switch
    {
        HitResult.Bloom or HitResult.Critical => 1,
        HitResult.Break => 2.0 / 3,
        HitResult.Hit => 1.0 / 3,
        _ => 0
    };
}
