// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text.Json.Serialization;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Gameplay.Replays;

/// <summary>
/// One judgement of a saved run, with as much of its note as the results screen reads, so a saved
/// score can show its full breakdown without loading the chart.
/// </summary>
public sealed record RecordedJudgement(
    [property: JsonPropertyName("n")] NoteType NoteType,
    [property: JsonPropertyName("s")] double NoteStart,
    [property: JsonPropertyName("e")] double NoteEnd,
    [property: JsonPropertyName("l")] int Lane,
    [property: JsonPropertyName("r")] HitResult Result,
    [property: JsonPropertyName("o")] double TimingError,
    [property: JsonPropertyName("h")] bool IsHoldTail)
{
    public static RecordedJudgement From(JudgementResult judgement) => new RecordedJudgement(
        judgement.Note.Type,
        judgement.Note.StartTime,
        judgement.Note.EndTime,
        (int)judgement.Note.Lane,
        judgement.Result,
        judgement.TimingError,
        judgement.IsHoldTail);

    /// <summary>
    /// Back to a judgement, on a stand-in note carrying the recorded details.
    /// </summary>
    public JudgementResult ToJudgement() => new JudgementResult(
        new BeatmapNote
        {
            Type = NoteType,
            StartTime = NoteStart,
            EndTime = NoteEnd,
            Lane = (NoteLane)Lane
        },
        Result,
        TimingError,
        IsHoldTail);
}
