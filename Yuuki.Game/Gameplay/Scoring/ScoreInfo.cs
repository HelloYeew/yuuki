// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Text.Json.Serialization;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// One play as saved by the <see cref="Database.ScoreStore"/>: enough to rank it, count it as a play,
/// and show it in the Ranking tab, without the per-note detail of a <see cref="GameplayResult"/>.
/// </summary>
public class ScoreInfo
{
    public int BeatmapId { get; set; }
    public int BeatmapSetId { get; set; }

    public long Score { get; set; }
    public double Accuracy { get; set; }
    public int MaxCombo { get; set; }

    public int CriticalCount { get; set; }
    public int BreakCount { get; set; }
    public int HitCount { get; set; }
    public int MissCount { get; set; }
    public int HoldBreakCount { get; set; }

    public ScoreRank Rank { get; set; }
    public bool IsFullCombo { get; set; }
    public bool IsAllPerfect { get; set; }

    /// <summary>The player ran out of health. Counts as a play, never as a ranking entry.</summary>
    public bool Failed { get; set; }

    public DateTimeOffset Date { get; set; }

    /// <summary>
    /// Names the replay saved with this play (its inputs and per-note judgements), or null for plays
    /// saved before replays were recorded: those have totals only.
    /// </summary>
    public string? ReplayId { get; set; }

    /// <summary>
    /// Whether this play may appear in rankings and as a personal best.
    /// </summary>
    [JsonIgnore]
    public bool IsRanked => !Failed;

    /// <summary>
    /// Whether a replay (and with it the full breakdown) was saved with this play.
    /// </summary>
    [JsonIgnore]
    public bool HasReplay => ReplayId != null;

    public static ScoreInfo From(GameplayResult result, int beatmapId, int beatmapSetId) => new ScoreInfo
    {
        BeatmapId = beatmapId,
        BeatmapSetId = beatmapSetId,
        Score = result.Score,
        Accuracy = result.Accuracy,
        MaxCombo = result.MaxCombo,
        CriticalCount = result.CriticalCount,
        BreakCount = result.BreakCount,
        HitCount = result.HitCount,
        MissCount = result.MissCount,
        HoldBreakCount = result.HoldBreakCount,
        Rank = result.Rank,
        IsFullCombo = result.IsFullCombo,
        IsAllPerfect = result.IsAllPerfect,
        Failed = result.Failed,
        Date = DateTimeOffset.Now
    };
}
