// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// The outcome of judging a single note, emitted by the <see cref="Playfield"/> for processors and
/// the HUD to consume.
/// </summary>
public readonly struct JudgementResult
{
    public readonly BeatmapNote Note;
    public readonly HitResult Result;

    /// <summary>
    /// Signed timing error in ms (input time − note time); 0 for perfect, positive = late. For a hold's
    /// tail it's the release against the hold's end, unscaled by the release lenience.
    /// </summary>
    public readonly double TimingError;

    /// <summary>
    /// Whether this is a hold's release (its tail) rather than a press.
    /// A hold is judged twice: once for the press and once for the release.
    /// </summary>
    public readonly bool IsHoldTail;

    public JudgementResult(BeatmapNote note, HitResult result, double timingError, bool isHoldTail = false)
    {
        Note = note;
        Result = result;
        TimingError = timingError;
        IsHoldTail = isHoldTail;
    }
}
