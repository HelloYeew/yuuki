// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Database;

/// <summary>
/// The local score history: every finished or failed play
/// TODO: This should move to some more persistent storage. yeah json anywhere for now......
/// </summary>
public class ScoreStore
{
    private const string file_name = "scores.json";
    private const string replay_directory = "replays";

    private readonly Storage? storage;
    private readonly List<ScoreInfo> scores = new List<ScoreInfo>();

    /// <summary>
    /// Replays kept when there's no storage to write them to.
    /// </summary>
    private readonly Dictionary<string, ReplayData> memoryReplays = new Dictionary<string, ReplayData>();

    /// <summary>
    /// Fired after a play is added, so screens showing counts or rankings can refresh.
    /// </summary>
    public event Action<ScoreInfo>? ScoreAdded;

    // The last write off the update thread: each waits for the one before.
    private Task writing = Task.CompletedTask;

    public ScoreStore(Storage? storage)
    {
        this.storage = storage;
        load();
    }

    public IReadOnlyList<ScoreInfo> Scores => scores;

    /// <summary>
    /// Records a play and saves the history, with its replay when there is one.
    /// </summary>
    public void Add(ScoreInfo score, ReplayData? replay = null)
    {
        if (replay != null)
        {
            score.ReplayId ??= Guid.NewGuid().ToString("N");

            // A replay that can't be written just leaves the play with totals only.
            if (!saveReplay(score.ReplayId, replay))
                score.ReplayId = null;
        }

        scores.Add(score);
        save();
        ScoreAdded?.Invoke(score);
    }

    /// <summary>
    /// The replay saved with <paramref name="score"/>, or null if it has none (or it can't be read).
    /// </summary>
    public ReplayData? GetReplay(ScoreInfo score)
    {
        if (score.ReplayId == null)
            return null;

        if (storage == null)
            return memoryReplays.GetValueOrDefault(score.ReplayId);

        string path = replayPath(score.ReplayId);

        try
        {
            Storage directory = storage.GetStorageForDirectory(replay_directory);
            if (!directory.Exists(path))
                return null;

            using Stream stream = directory.GetStream(path);
            return JsonSerializer.Deserialize<ReplayData>(stream);
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to read the replay {path}.", e);
            return null;
        }
    }

    private bool saveReplay(string id, ReplayData replay)
    {
        if (storage == null)
        {
            memoryReplays[id] = replay;
            return true;
        }

        try
        {
            Storage directory = storage.GetStorageForDirectory(replay_directory);
            using Stream stream = directory.CreateFileSafely(replayPath(id));
            JsonSerializer.Serialize(stream, replay);
            return true;
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to save the replay {replayPath(id)}.", e);
            return false;
        }
    }

    private static string replayPath(string id) => $"{id}.json";

    /// <summary>
    /// Plays of one difficulty, failed ones included.
    /// </summary>
    public int GetPlayCount(Beatmap beatmap) => scores.Count(s => s.BeatmapId == beatmap.ID);

    /// <summary>
    /// Plays across every difficulty of a song.
    /// </summary>
    public int GetPlayCount(BeatmapSet set) => scores.Count(s => s.BeatmapSetId == set.ID);

    /// <summary>
    /// Plays of one difficulty on the day of <paramref name="now"/> (today if not given), by the local clock, failed ones
    /// included: the tries a failed run counts itself among.
    /// </summary>
    public int GetPlaysToday(Beatmap beatmap, DateTimeOffset? now = null)
    {
        DateTime today = (now ?? DateTimeOffset.Now).LocalDateTime.Date;
        return scores.Count(s => s.BeatmapId == beatmap.ID && s.Date.LocalDateTime.Date == today);
    }

    /// <summary>
    /// The highest-scoring play of a difficulty that wasn't a fail, if any.
    /// </summary>
    public ScoreInfo? GetPersonalBest(Beatmap beatmap) => GetRanking(beatmap, 1).FirstOrDefault();

    /// <summary>
    /// The play that earned the best mark on a difficulty (full bloom, all perfect, then rank, then full combo), if any.
    /// </summary>
    public ScoreInfo? GetBestMark(Beatmap beatmap) => bestMark(scores.Where(s => s.BeatmapId == beatmap.ID));

    /// <summary>
    /// The play that earned the best mark across a song's difficulties, if any.
    /// </summary>
    public ScoreInfo? GetBestMark(BeatmapSet set) => bestMark(scores.Where(s => s.BeatmapSetId == set.ID));

    private static ScoreInfo? bestMark(IEnumerable<ScoreInfo> plays) =>
        plays.Where(s => s.IsRanked)
             .OrderByDescending(s => s.IsFullBloom)
             .ThenByDescending(s => s.IsAllPerfect)
             .ThenByDescending(s => s.Rank)
             .ThenByDescending(s => s.IsFullCombo)
             .ThenByDescending(s => s.Score)
             .FirstOrDefault();

    /// <summary>
    /// The best <paramref name="count"/> unfailed plays of a difficulty, best first (all of them by default).
    /// </summary>
    public IReadOnlyList<ScoreInfo> GetRanking(Beatmap beatmap, int count = int.MaxValue) =>
        scores.Where(s => s.BeatmapId == beatmap.ID && s.IsRanked)
              .OrderByDescending(s => s.Score)
              .ThenByDescending(s => s.Accuracy)
              .ThenBy(s => s.Date)
              .Take(count)
              .ToList();

    /// <summary>
    /// Saves the history after plays in it have been changed in place, on a thread from the pool, after any write still
    /// going.
    /// </summary>
    /// <returns>When it's written.</returns>
    public Task SaveChanges()
    {
        ScoreInfo[] snapshot = scores.ToArray();
        return writing = writing.ContinueWith(_ => write(snapshot), TaskScheduler.Default);
    }

    /// <summary>
    /// Copies the history aside as <paramref name="name"/>, beside it, on a thread from the pool, after any write still
    /// going; a copy already there is kept, so it stays the one from before the first change.
    /// </summary>
    /// <returns>When it's copied.</returns>
    public Task BackUp(string name) => writing = writing.ContinueWith(_ =>
    {
        if (storage == null || !storage.Exists(file_name) || storage.Exists(name))
            return;

        try
        {
            using Stream history = storage.GetStream(file_name);
            using Stream copy = storage.CreateFileSafely(name);
            history.CopyTo(copy);
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to back up the score history to {name}.", e);
        }
    }, TaskScheduler.Default);

    private void load()
    {
        if (storage == null || !storage.Exists(file_name))
            return;

        try
        {
            using Stream stream = storage.GetStream(file_name);
            List<ScoreInfo>? loaded = JsonSerializer.Deserialize<List<ScoreInfo>>(stream);
            if (loaded != null)
            {
                // Ranked again rather than trusted, so plays saved under older rank rules (an S with a
                // miss) read the same in the Ranking tab as on their results screen.
                foreach (ScoreInfo score in loaded)
                    score.Rank = ScoreRankExtensions.ForRun(score.Accuracy, score.MissCount);

                scores.AddRange(loaded);
            }
        }
        catch (Exception e)
        {
            // A damaged history shouldn't stop the game; it starts empty and the next save replaces it.
            Logger.Error($"Failed to read the score history ({file_name}); starting empty.", e);
        }
    }

    private void save()
    {
        // After any write still going: they're one at a time, and in order.
        writing.Wait();
        write(scores);
    }

    private void write(IReadOnlyList<ScoreInfo> plays)
    {
        if (storage == null)
            return;

        try
        {
            // Written to a temporary file and moved into place, so a crash mid-write can't corrupt it.
            using Stream stream = storage.CreateFileSafely(file_name);
            JsonSerializer.Serialize(stream, plays);
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to save the score history ({file_name}).", e);
        }
    }
}
