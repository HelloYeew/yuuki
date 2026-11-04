// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Database;

/// <summary>
/// Brings saved plays up to the current judging rules (<see cref="ScoreInfo.CURRENT_RULES"/>) in the background after an
/// update
/// </summary>
public partial class ScoreMigration : Container
{
    /// <summary>
    /// How many plays it goes through between saves, so stopping part way loses little.
    /// </summary>
    private const int save_every = 25;

    /// <summary>
    /// The copy of the history kept from before the migration to these rules.
    /// </summary>
    public static string BackupName => $"scores.before-rules-{ScoreInfo.CURRENT_RULES}.json";

    private readonly ScoreStore store;
    private readonly Func<int, Beatmap?> beatmapFor;
    private readonly BeatmapNoteStore? noteStore;
    private readonly Queue<ScoreInfo> pending = new Queue<ScoreInfo>();

    private ProgressNotification? progress;
    private Task<(ReplayData Replay, PlayableBeatmap Chart)?>? reading;
    private ReplayJudge? judge;
    private ScoreInfo? current;
    private int total;
    private int unsaved;
    private bool finished;

    [Resolved(canBeNull: true)]
    private NotificationOverlay? notifications { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    /// <summary>
    /// Whether it should wait, besides while a song is being played (for tests).
    /// </summary>
    public Func<bool>? HoldOff { get; init; }

    /// <summary>
    /// Whether it has been through every play under older rules.
    /// </summary>
    public bool IsDone => finished;

    /// <summary>
    /// How many plays it has brought up to date.
    /// </summary>
    public int Updated { get; private set; }

    /// <summary>
    /// How many plays it left as they were played.
    /// </summary>
    public int Kept { get; private set; }

    /// <param name="store">The history to bring up to date.</param>
    /// <param name="beatmapFor">The beatmap with an ID, if it's still here. Asked on the update thread.</param>
    /// <param name="noteStore">Where beatmaps' notes are read from.</param>
    public ScoreMigration(ScoreStore store, Func<int, Beatmap?> beatmapFor, BeatmapNoteStore? noteStore)
    {
        this.store = store;
        this.beatmapFor = beatmapFor;
        this.noteStore = noteStore;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        foreach (ScoreInfo score in store.Scores.Where(s => s.RulesVersion < ScoreInfo.CURRENT_RULES && s.RulesTried < ScoreInfo.CURRENT_RULES))
            pending.Enqueue(score);

        total = pending.Count;
    }

    public override bool HandlePositionalInput => false;

    public override void Update()
    {
        base.Update();

        if (finished || audioManager?.InGameplay == true || HoldOff?.Invoke() == true)
            return;

        if (progress?.State == ProgressNotificationState.Cancelled)
        {
            finish();
            return;
        }

        if (judge != null)
        {
            if (judge.IsDone)
                finishJudging();
        }
        else if (reading != null)
        {
            if (reading.IsCompleted)
                startJudging();
        }
        else
            startNext();
    }

    // A play with nothing to read is passed over a frame at a time.
    private void startNext()
    {
        if (!pending.TryDequeue(out ScoreInfo? score))
        {
            finish();
            return;
        }

        if (!score.HasReplay || beatmapFor(score.BeatmapId) is not Beatmap beatmap)
        {
            Kept++;
            return;
        }

        current = score;
        reading = Task.Run(() => read(score, beatmap));
    }

    // On a thread from the pool: the play's replay and chart, if they can be judged.
    private (ReplayData Replay, PlayableBeatmap Chart)? read(ScoreInfo score, Beatmap beatmap)
    {
        try
        {
            ReplayData? replay = store.GetReplay(score);
            PlayableBeatmap? chart = replay == null ? null : ReplayJudge.ChartFor(beatmap, noteStore);

            return replay != null && chart != null && replay.CanPlayOn(chart) ? (replay, chart) : null;
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to read a play of beatmap {score.BeatmapId} to update it.", e);
            return null;
        }
    }

    // The first play that has something to judge is what shows the notification.
    private void startJudging()
    {
        (ReplayData Replay, PlayableBeatmap Chart)? read = reading!.Result;
        reading = null;

        if (read is not var (replay, chart))
        {
            Kept++;
            current = null;
            return;
        }

        if (progress == null)
            begin();

        updateProgress();

        Add(judge = new ReplayJudge(chart, replay)
        {
            Preempt = GameplayScreen.ScrollPreempt(config, chart),
            StartTime = -GameplayScreen.LeadInFor(chart)
        });
    }

    private void begin()
    {
        store.BackUp(BackupName);

        notifications?.Post(progress = new ProgressNotification
        {
            Text = "Updating your scores to today's judging",
            State = ProgressNotificationState.Active
        });
    }

    private void updateProgress()
    {
        if (progress == null)
            return;

        int done = total - pending.Count - 1;
        progress.Text = $"Updating your scores to today's judging ({done + 1}/{total})";
        progress.Progress = total > 0 ? (float)done / total : 0;
    }

    private void finishJudging()
    {
        GameplayResult result = judge!.BuildResult();
        ScoreInfo score = current!;

        // Only one that ended as it did: a pass that gets through every note, a fail that fails.
        if (!judge.Faulted && result.Failed == score.Failed && (score.Failed || judge.JudgedEverything))
        {
            score.ApplyJudging(result);
            Updated++;
        }
        else
        {
            score.RulesTried = ScoreInfo.CURRENT_RULES;
            Kept++;
        }

        if (++unsaved >= save_every)
        {
            store.SaveChanges();
            unsaved = 0;
        }

        Remove(judge);
        judge = null;
        current = null;
    }

    private void finish()
    {
        finished = true;

        if (judge != null)
        {
            Remove(judge);
            judge = null;
        }

        if (unsaved > 0)
            store.SaveChanges();

        if (progress == null || progress.State == ProgressNotificationState.Cancelled)
            return;

        string updated = Updated == 1 ? "Updated 1 score to today's judging." : $"Updated {Updated} scores to today's judging.";
        string kept = Kept switch
        {
            0 => "",
            _ => $" {Kept} kept as played."
        };

        progress.Progress = 1;
        progress.CompletionText = updated + kept;
        progress.State = ProgressNotificationState.Completed;
    }
}
