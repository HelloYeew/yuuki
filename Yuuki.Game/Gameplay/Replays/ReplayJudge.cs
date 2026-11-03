// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Gameplay.Replays;

/// <summary>
/// Judges a saved run again from its replay, on the chart it was played on, as watching it would: its inputs go through a
/// playfield of its own, a frame of the run at a time (as a replay seek steps). What it finds is what the results screen
/// shows of a saved play, which keeps only its totals: every judgement, when each hold broke, and how far it has got.
/// </summary>
/// <remarks>
/// <para>It works on a thread from the pool, which it gives back when it's done, so it takes nothing from the update
/// thread's frames. Its playfield is never added to anything, so nothing else touches it: nothing updates it, draws it or
/// sends it input. It's loaded on the pool too.</para>
/// <para>It starts once it's loaded, and stops at the chart's end, once every note is judged, or where health ran out (a
/// failed run stopped there). <see cref="IsDone"/> and <see cref="Completed"/> come on the update thread. Put it
/// anywhere; it fills nothing and draws nothing. Disposed part way, it stops.</para>
/// </remarks>
public partial class ReplayJudge : Container
{
    /// <summary>
    /// A frame of the run, in ms: as a replay seek steps, at 60 fps.
    /// </summary>
    private const double step = 1000.0 / 60;

    /// <summary>
    /// How long past the chart's end it keeps judging: as a replay plays on, for the last releases and misses.
    /// </summary>
    private const double end_margin = 2000;

    private readonly PlayableBeatmap chart;
    private readonly ReplayData replay;
    private readonly double chartLength;
    private readonly double endTime;
    private readonly int totalJudgements;

    private readonly JudgeClock clock = new JudgeClock();
    private readonly SkillProcessor skills = new SkillProcessor();
    private readonly HealthProcessor health = new HealthProcessor();
    private readonly ScoreProcessor score = new ScoreProcessor();
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

    // What it's found, added to on its thread and read on the update thread: taken under this lock.
    private readonly object found = new object();
    private readonly List<JudgementResult> judgements = new List<JudgementResult>();
    private readonly List<double> holdBreakTimes = new List<double>();

    private Playfield? playfield;
    private HitWindows hitWindows = new HitWindows();
    private Task? work;
    private volatile bool workDone;

    // Written on its thread, read on the update thread.
    private double time;
    private int judgedNotes;
    private int judgementCount;

    /// <summary>
    /// How long a note is in sight before the line, in ms, as the run is watched: a Pass or Touch note is only judged
    /// while it's in sight. Set before load.
    /// </summary>
    public double Preempt { get; init; } = 750;

    /// <summary>
    /// Where in the run it starts judging, in ms: the run's lead-in, before the song. Set before load.
    /// </summary>
    public double StartTime { get; init; } = -2000;

    /// <summary>
    /// Whether a Bloom counts as the Critical it was before there were any: for a play saved with none (played before
    /// BLOOM, all of them), so what it finds matches the counts it was saved with. Set before load.
    /// </summary>
    public bool CountsBloomAsCritical { get; init; }

    /// <summary>
    /// How many times faster than the song it may go: as fast as it can by default, slower only to watch it (tests). It can
    /// change as it goes, and 0 holds it where it's got to.
    /// </summary>
    public double SongSpeed
    {
        get => Volatile.Read(ref songSpeed);
        set => Volatile.Write(ref songSpeed, value);
    }

    private double songSpeed = double.PositiveInfinity;

    /// <summary>
    /// Whether it has judged the whole run (or stopped on an error, see <see cref="Faulted"/>).
    /// </summary>
    public bool IsDone { get; private set; }

    /// <summary>
    /// Whether it stopped on an error: what it found is only part of the run.
    /// </summary>
    public bool Faulted { get; private set; }

    /// <summary>
    /// How far into the run it has judged, in ms of the song.
    /// </summary>
    public double JudgedTime => Volatile.Read(ref time);

    /// <summary>
    /// How far through the chart it has got, 0–1.
    /// </summary>
    public float Progress => IsDone ? 1 : chartLength > 0 ? (float)Math.Clamp(JudgedTime / chartLength, 0, 1) : 0;

    /// <summary>
    /// How many of the chart's notes it has judged (a hold once, at its press).
    /// </summary>
    public int JudgedNotes => Volatile.Read(ref judgedNotes);

    /// <summary>
    /// How many judgements it has made so far (a hold's press and release are two).
    /// </summary>
    public int JudgementCount => Volatile.Read(ref judgementCount);

    /// <summary>
    /// How many notes the chart has.
    /// </summary>
    public int TotalNotes => chart.Notes.Length;

    /// <summary>
    /// The chart it's judging on.
    /// </summary>
    public PlayableBeatmap Chart => chart;

    /// <summary>
    /// Every judgement, in order, once it's done (<see cref="Snapshot"/> before that).
    /// </summary>
    public IReadOnlyList<JudgementResult> Judgements => judgements;

    /// <summary>
    /// When each hold broke, in ms of the song, in order, once it's done (<see cref="Snapshot"/> before that).
    /// </summary>
    public IReadOnlyList<double> HoldBreakTimes => holdBreakTimes;

    /// <summary>
    /// Where health ran out, in ms of the song, once it's done; null where it didn't.
    /// </summary>
    public double? FailedAt { get; private set; }

    /// <summary>
    /// The real time it took to judge, once it's done.
    /// </summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>
    /// The thread it judged on, once it has started.
    /// </summary>
    public int JudgingThreadId { get; private set; }

    /// <summary>
    /// Fired once, on the update thread, when it's done.
    /// </summary>
    public event Action? Completed;

    public ReplayJudge(PlayableBeatmap chart, ReplayData replay)
    {
        this.chart = chart;
        this.replay = replay;

        chartLength = chart.LastNoteTime;
        endTime = chartLength + end_margin;

        foreach (BeatmapNote note in chart.Notes)
            totalJudgements += note.Type == NoteType.Hold ? 2 : 1;

        score.Prepare(chart);
    }

    /// <summary>
    /// The chart a beatmap's replays are judged on, as gameplay builds it; null where it has no notes. It leaves the beatmap
    /// as it was, so it's safe on any thread.
    /// </summary>
    public static PlayableBeatmap? ChartFor(Beatmap beatmap, BeatmapNoteStore? noteStore)
    {
        BeatmapNote[] notes = noteStore?.Read(beatmap) ?? beatmap.Notes;

        return notes.Length == 0 ? null : PlayableBeatmap.FromBeatmap(beatmap, notes, LaneKeyLayouts.Clamp(beatmap.LaneCount));
    }

    /// <summary>
    /// What it has found so far, safe to read while it's still judging.
    /// </summary>
    public (JudgementResult[] Judgements, double[] HoldBreakTimes) Snapshot()
    {
        lock (found)
            return (judgements.ToArray(), holdBreakTimes.ToArray());
    }

    /// <summary>
    /// The run as it's been judged, once it's done: its totals, as gameplay's would be, and every judgement.
    /// </summary>
    public GameplayResult BuildResult() => new GameplayResult
    {
        Score = score.Score.Value,
        MaxCombo = score.MaxCombo.Value,
        BloomCount = judgements.Count(j => j.Result == HitResult.Bloom),
        CriticalCount = judgements.Count(j => j.Result == HitResult.Critical),
        BreakCount = judgements.Count(j => j.Result == HitResult.Break),
        HitCount = judgements.Count(j => j.Result == HitResult.Hit),
        MissCount = judgements.Count(j => j.Result == HitResult.Miss),
        HoldBreakCount = holdBreakTimes.Count,
        Accuracy = score.Accuracy.Value,
        Failed = FailedAt != null,
        FailedAt = FailedAt,
        Judgements = judgements.ToArray(),
        HoldBreakTimes = holdBreakTimes.ToArray(),
        LaneCount = chart.LaneCount,
        HitWindows = hitWindows,
        ChartLength = chartLength,
        AudioOffset = replay.AudioOffset,
        SongOffset = replay.SongOffset
    };

    /// <summary>
    /// Whether it got through every note: a run that failed stops short of the rest.
    /// </summary>
    public bool JudgedEverything => !Faulted && judgements.Count >= totalJudgements;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        time = StartTime;
        clock.CurrentTime = StartTime;

        // On a clock of its own that stands where the judging has got to.
        var field = new Playfield(chart)
        {
            Clock = clock,
            Preempt = Preempt,
            Silent = true,
            AcceptsUserInput = false,
            DisplayLatency = 0,
            ReplayInputs = replay.Inputs
        };

        hitWindows = field.HitWindows;
        field.JudgementFilter = judgement => BloomlessJudgement.Apply(skills.Filter(judgement, time), CountsBloomAsCritical);
        field.NewJudgement += onJudgement;
        field.HoldBroken += _ =>
        {
            score.BreakCombo();

            lock (found)
                holdBreakTimes.Add(time);
        };

        playfield = field;

        LoadComponentAsync(field, _ => work = Task.Run(judge), cancellation.Token).ContinueWith(load =>
        {
            Logger.Error("Failed to load a playfield to judge a replay on.", load.Exception!);
            Faulted = true;
            workDone = true;
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public override bool HandlePositionalInput => false;

    public override void Update()
    {
        base.Update();

        if (IsDone || !workDone)
            return;

        IsDone = true;
        releasePlayfield();
        Completed?.Invoke();
    }

    // On a thread from the pool, as fast as it goes (a three-minute chart takes about 10 ms), then the thread goes back.
    private void judge()
    {
        var running = Stopwatch.StartNew();
        JudgingThreadId = Environment.CurrentManagedThreadId;

        // How far it may have got by now, and when that was worked out: the speed can change along the way.
        double allowedTo = StartTime;
        double allowedAt = 0;

        try
        {
            while (!finished() && !cancellation.IsCancellationRequested)
            {
                double next = Math.Min(time + step, endTime);

                while (!cancellation.IsCancellationRequested && !double.IsPositiveInfinity(SongSpeed) && next > allowedTo)
                {
                    Thread.Sleep(1);

                    double now = running.Elapsed.TotalMilliseconds;
                    allowedTo += (now - allowedAt) * SongSpeed;
                    allowedAt = now;
                }

                Volatile.Write(ref time, next);
                clock.CurrentTime = next;

                // As a frame would: the field (inputs, then judging), then the running skills.
                playfield!.SimulateFrame(next);
                skills.Update(next);
            }
        }
        catch (Exception e)
        {
            Logger.Error("Failed to judge a replay again.", e);
            Faulted = true;
        }
        finally
        {
            Elapsed = running.Elapsed;
            workDone = true;
        }
    }

    private bool finished() => time >= endTime || FailedAt != null || (totalJudgements > 0 && judgements.Count >= totalJudgements);

    private void onJudgement(JudgementResult judgement)
    {
        score.Apply(judgement);
        health.Apply(judgement);
        skills.Apply(judgement, time);

        lock (found)
            judgements.Add(judgement);

        Volatile.Write(ref judgementCount, judgementCount + 1);

        if (!judgement.IsHoldTail)
            Volatile.Write(ref judgedNotes, judgedNotes + 1);

        if (health.IsFailed)
            FailedAt ??= time;
    }

    // Its playfield is nobody's child, so nothing else disposes it. Only once it's loaded: a load still going is thrown
    // away by the framework, when it's cancelled.
    private void releasePlayfield()
    {
        if (playfield is { IsLoaded: true })
            playfield.Dispose();

        playfield = null;
    }

    protected override void Dispose(bool isDisposing)
    {
        cancellation.Cancel();

        // It checks between the steps of a few microseconds, so this is over at once.
        work?.Wait(1000);

        releasePlayfield();
        cancellation.Dispose();

        base.Dispose(isDisposing);
    }

    /// <summary>
    /// A clock that stands still where it's put.
    /// </summary>
    private class JudgeClock : IFrameBasedClock
    {
        public double CurrentTime { get; set; }

        public double Rate => 1;

        public bool IsRunning => false;

        public double ElapsedFrameTime => 0;

        public double FramesPerSecond => 0;

        public void ProcessFrame()
        {
        }
    }
}
