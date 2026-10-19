// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Gameplay.Replays;

/// <summary>
/// Everything saved alongside a score: the run's inputs, so it can be watched again (fed back through
/// the playfield, which judges them as it did live), and its judgments, so the result screen can
/// show the full breakdown. Stored per score by the <see cref="Database.ScoreStore"/>.
/// </summary>
public class ReplayData
{
    public const int CURRENT_VERSION = 1;

    public int Version { get; set; } = CURRENT_VERSION;

    /// <summary>
    /// A fingerprint of the chart the run was played on (see <see cref="ChartHashOf"/>). A replay only
    /// plays back on the same notes; an edited chart would judge the inputs differently.
    /// </summary>
    public string ChartHash { get; set; } = "";

    /// <summary>
    /// The audio offset the run was played with, so playback lines the inputs up with the music the same way.
    /// </summary>
    public double AudioOffset { get; set; }

    public double ChartLength { get; set; }

    public HitWindows HitWindows { get; set; } = new HitWindows();

    /// <summary>
    /// The run's inputs in the order they happened (and so in time order).
    /// </summary>
    public List<ReplayInput> Inputs { get; set; } = new List<ReplayInput>();

    public List<RecordedJudgement> Judgements { get; set; } = new List<RecordedJudgement>();

    /// <summary>
    /// Whether this replay can be watched on <paramref name="chart"/>.
    /// </summary>
    public bool CanPlayOn(PlayableBeatmap chart) => Version == CURRENT_VERSION && Inputs.Count > 0 && ChartHash == ChartHashOf(chart);

    /// <summary>
    /// A short fingerprint of a chart's notes: anything that changes how inputs are judged changes it.
    /// </summary>
    public static string ChartHashOf(PlayableBeatmap chart)
    {
        var text = new StringBuilder();
        text.Append(chart.LaneCount).Append(';');

        foreach (BeatmapNote note in chart.Notes)
        {
            text.Append(note.Type).Append(',')
                .Append(note.StartTime.ToString("R")).Append(',')
                .Append(note.EndTime.ToString("R")).Append(',')
                .Append((int)note.Lane).Append(',')
                .Append(note.EndLane is NoteLane endLane ? (int)endLane : -1).Append(',')
                .Append(note.Direction).Append(',')
                .Append(note.Skill).Append(';');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexString(hash, 0, 12);
    }
}
