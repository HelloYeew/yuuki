// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// Timing windows (ms of absolute error) that map a timing error to a <see cref="HitResult"/>.
/// The window extends past the last good result into a miss window: a press that early (or
/// late) still takes the note, as a Miss, so mashing ahead of the notes is punished instead of free.
/// </summary>
public class HitWindows
{
    public double Critical { get; set; } = 45;
    public double Break { get; set; } = 90;
    public double Hit { get; set; } = 135;

    /// <summary>
    /// The outer edge: a press within it takes the nearest note (as a Miss beyond <see cref="Hit"/>), and
    /// an untouched note past it is missed automatically.
    /// </summary>
    public double MissWindow { get; set; } = 180;

    /// <summary>
    /// How much wider a hold's release windows are than its press windows,
    /// so releases timed alongside presses of other notes aren't awkward.
    /// </summary>
    public double ReleaseLenience { get; set; } = 1.5;

    /// <summary>
    /// The release counterpart of <see cref="MissWindow"/>: beyond it a release can't grade the tail.
    /// </summary>
    public double ReleaseWindow => MissWindow * ReleaseLenience;

    /// <summary>
    /// Whether a press with this signed error is close enough to take the note at all.
    /// </summary>
    public bool CanHit(double timingError) => Math.Abs(timingError) <= MissWindow;

    /// <summary>
    /// Grades a hold release's signed timing error against the lenient release windows.
    /// </summary>
    public HitResult ResultForRelease(double releaseError) => ResultFor(releaseError / ReleaseLenience);

    /// <summary>
    /// Grades a signed timing error (actual − target, ms).
    /// </summary>
    public HitResult ResultFor(double timingError)
    {
        double error = Math.Abs(timingError);
        if (error <= Critical) return HitResult.Critical;
        if (error <= Break) return HitResult.Break;
        if (error <= Hit) return HitResult.Hit;
        return HitResult.Miss;
    }
}
