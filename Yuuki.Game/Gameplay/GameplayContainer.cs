// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// The reusable Normal-phase gameplay slice: a <see cref="GameplayClockContainer"/> driving a
/// <see cref="Playfield"/>, wired to score/health/skill processors and a <see cref="GameplayHud"/>.
/// Drop it into any screen; call <see cref="Restart"/> to (re)start a run.
/// </summary>
public partial class GameplayContainer : Container
{
    private readonly PlayableBeatmap chart;
    private readonly IAdjustableClock? clockSource;

    private GameplayClockContainer clockContainer = null!;
    private Playfield playfield = null!;
    private GameplayHud hud = null!;
    private HitSoundPlayer hitSounds = null!;

    /// <summary>
    /// Combo lost at or above this length plays the combo-break sting.
    /// </summary>
    private const int combo_break_threshold = 20;

    /// <summary>
    /// The frame length a replay seeks re-runs the run in (60 fps, a typical live frame).
    /// </summary>
    private const double simulation_step = 1000.0 / 60;

    public ScoreProcessor Score { get; } = new ScoreProcessor();
    public HealthProcessor Health { get; } = new HealthProcessor();
    public SkillProcessor Skills { get; } = new SkillProcessor();

    private double preempt = 750;

    /// <summary>
    /// Lead-in before time 0, forwarded to the clock container. Set before the container loads.
    /// </summary>
    public double LeadInTime { get; set; } = 2000;

    /// <summary>
    /// What the HUD's progress bar spans, in ms. Set before load to the track's length when there is
    /// one; otherwise it's the chart's end plus a short tail.
    /// </summary>
    public double? SongLength { get; set; }

    /// <summary>
    /// The keys for each lane, left to right. Null keeps the default layout. Set before load.
    /// </summary>
    public Sakura.Framework.Input.Key[]? LaneKeys { get; set; }

    /// <summary>
    /// The left and right swipe keys. Null keeps the defaults. Set before load.
    /// </summary>
    public Sakura.Framework.Input.Key? SwipeLeftKey { get; set; }

    public Sakura.Framework.Input.Key? SwipeRightKey { get; set; }

    private double audioOffset;

    /// <summary>
    /// The audio offset (ms) applied to the gameplay clock. Shifts note positions and the judged time
    /// of every input together, so notes line up with when the music is actually heard.
    /// </summary>
    public double AudioOffset
    {
        get => audioOffset;
        set
        {
            audioOffset = value;
            if (clockContainer.IsNotNull())
                clockContainer.GameplayClock.Offset = value;
        }
    }

    /// <summary>Scroll speed passed to the playfield (ms a note is visible before the line).</summary>
    public double Preempt
    {
        get => preempt;
        set
        {
            preempt = value;
            if (playfield.IsNotNull())
                playfield.Preempt = value;
        }
    }

    private bool allowJudgement = true;

    /// <summary>When false, no auto-miss or input scoring occurs (for editor/replay/scrub use).</summary>
    public bool AllowJudgement
    {
        get => allowJudgement;
        set
        {
            allowJudgement = value;
            if (playfield.IsNotNull())
                playfield.AllowJudgement = value;
        }
    }

    public GameplayClock GameplayClock => clockContainer.GameplayClock;

    /// <summary>
    /// The lane the character stands in (see <see cref="Playfield.CharacterLane"/>).
    /// </summary>
    public int CharacterLane => playfield.CharacterLane;

    /// <summary>
    /// Bursts still playing (diagnostics/tests).
    /// </summary>
    public int ActiveEffectCount => playfield?.ActiveEffectCount ?? 0;

    /// <summary>
    /// Whether any lane's hit or miss flash is showing (diagnostics/tests).
    /// </summary>
    public bool AnyLaneFlashing => playfield?.AnyLaneFlashing ?? false;

    /// <summary>
    /// Notes currently on screen (diagnostics/tests).
    /// </summary>
    public int VisibleNoteCount => playfield?.VisibleNoteCount ?? 0;

    /// <summary>
    /// The chart being played.
    /// </summary>
    public PlayableBeatmap Chart => chart;

    /// <summary>
    /// Seeks the gameplay clock. The field reconciles the visible notes to the new time.
    /// </summary>
    public bool Seek(double time) => clockContainer.Seek(time);

    private int criticalCount;
    private int breakCount;
    private int hitCount;
    private int missCount;
    private int holdBreakCount;
    private bool failed;

    /// <summary>
    /// Fired once per run, the moment health runs out.
    /// </summary>
    public event Action? Failed;

    /// <summary>
    /// Whether every judgement the chart holds has happened — the run is over.
    /// </summary>
    public bool AllJudged => Score.TotalJudgements > 0 && judgements.Count >= Score.TotalJudgements;

    /// <summary>
    /// A saved run to play back instead of taking input: its inputs drive the field, which judges them
    /// as it did live. Set before load. Null for a live run, whose inputs are recorded instead (see
    /// <see cref="BuildReplay"/>).
    /// </summary>
    public ReplayData? Replay { get; init; }

    public bool IsReplay => Replay.IsNotNull();

    /// <summary>
    /// Makes the HUD's song progress bar a seek bar (see <see cref="GameplayHud.SeekRequested"/>):
    /// called with the time under the pointer, and whether the gesture has ended.
    /// </summary>
    public Action<double, bool>? ProgressBarSeek
    {
        get => hud.SeekRequested;
        set => hud.SeekRequested = value;
    }

    // A live run's inputs, in the order they happened.
    private readonly List<ReplayInput> recordedInputs = new List<ReplayInput>();

    // While a replay seek re-runs the run, the time it has reached (the clock isn't moving); null otherwise.
    private double? simulationTime;

    // The time judgements are happening at: the clock's, or the simulation's during a seek.
    private double judgingTime => simulationTime ?? GameplayClock.CurrentTime;

    /// <summary>
    /// The playback rate of the gameplay clock (and with it the track's pitch).
    /// </summary>
    public double Rate
    {
        get => clockContainer.Rate;
        set => clockContainer.Rate = value;
    }

    /// <summary>
    /// Pauses or resumes input judging (see <see cref="Playfield.InputPaused"/>).
    /// </summary>
    public bool InputPaused
    {
        get => playfield.InputPaused;
        set
        {
            if (playfield.InputPaused == value)
                return;

            // Recorded so the replay re-creates it: a press while paused only marks the lane held.
            if (!IsReplay)
                recordedInputs.Add(new ReplayInput(GameplayClock.CurrentTime, value ? ReplayInputKind.Pause : ReplayInputKind.Resume));

            playfield.InputPaused = value;

            if (!value)
                playfield.ReleaseHoldsNotHeld();
        }
    }

    // Every judgement in order, kept for the score screen's detailed breakdown.
    private readonly List<JudgementResult> judgements = new List<JudgementResult>();

    /// <summary>
    /// Builds the final result tally for the score screen.
    /// </summary>
    public GameplayResult BuildResult() => new GameplayResult
    {
        Score = Score.Score.Value,
        MaxCombo = Score.MaxCombo.Value,
        CriticalCount = criticalCount,
        BreakCount = breakCount,
        HitCount = hitCount,
        MissCount = missCount,
        HoldBreakCount = holdBreakCount,
        Accuracy = Score.Accuracy.Value,
        Failed = failed,
        Judgements = judgements.ToArray(),
        HitWindows = playfield.HitWindows,
        AudioOffset = audioOffset,
        ChartLength = chart.LastNoteTime
    };

    /// <summary>
    /// The run so far as a replay: its inputs to watch it again, and its judgements for the results
    /// screen's breakdown. Saved with the score.
    /// </summary>
    public ReplayData BuildReplay() => new ReplayData
    {
        ChartHash = ReplayData.ChartHashOf(chart),
        AudioOffset = audioOffset,
        ChartLength = chart.LastNoteTime,
        HitWindows = playfield.HitWindows,
        Inputs = recordedInputs.ToList(),
        Judgements = judgements.Select(RecordedJudgement.From).ToList()
    };

    /// <param name="chart">The chart to play.</param>
    /// <param name="clockSource">
    /// Optional audio source (e.g. a <see cref="TrackClock"/>) to sync gameplay to music. Null runs
    /// on an internal <see cref="StopwatchClock"/> (silent/deterministic).
    /// </param>
    public GameplayContainer(PlayableBeatmap chart, IAdjustableClock? clockSource = null)
    {
        this.chart = chart;
        this.clockSource = clockSource;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Loads and owns every gameplay sample (banks, additions, slide loops, combo break). It's a
        // non-visual sibling so it lives in real time — its playback is event-driven, not clock-read.
        hitSounds = new HitSoundPlayer();

        Children = new Drawable[]
        {
            hitSounds,
            clockContainer = new GameplayClockContainer(clockSource)
            {
                RelativeSizeAxes = Axes.Both,
                LeadInTime = LeadInTime,
                // A fixed-shape canvas for the field, fitted to the window and the player's
                // playfield size/position settings.
                Child = new PlayfieldAdjustmentContainer
                {
                    Child = playfield = new Playfield(chart, hitSounds)
                    {
                        Preempt = Preempt,
                        AllowJudgement = allowJudgement
                    }
                }
            },
            // HUD lives outside the gameplay clock so it animates in real time.
            hud = new GameplayHud(Score, Health, Skills, () => clockContainer.CurrentTime, SongLength ?? chart.LastNoteTime + 2000)
            {
                ComboClock = clockContainer.GameplayClock
            }
        };

        clockContainer.GameplayClock.Offset = audioOffset;

        Score.Prepare(chart);

        if (LaneKeys.IsNotNull() && LaneKeys.Length == chart.LaneCount)
            playfield.LaneKeys = LaneKeys;

        if (SwipeLeftKey is { } swipeLeft)
            playfield.SwipeLeftKey = swipeLeft;

        if (SwipeRightKey is { } swipeRight)
            playfield.SwipeRightKey = swipeRight;

        playfield.NewJudgement += onJudgement;
        playfield.JudgementFilter = judgement => Skills.Filter(judgement, judgingTime);
        playfield.HoldBroken += onHoldBroken;
        playfield.Beat += hud.PulseHeart;
        hud.ComboSpace = playfield;

        if (Replay.IsNotNull())
        {
            playfield.AcceptsUserInput = false;
            playfield.ReplayInputs = Replay.Inputs;
        }
        else
            playfield.InputReceived += input => recordedInputs.Add(input);
    }

    private void onHoldBroken(BeatmapNote note)
    {
        int comboBefore = Score.Combo.Value;

        Score.BreakCombo();
        holdBreakCount++;

        if (comboBefore >= combo_break_threshold)
            hitSounds.PlayComboBreak();
    }

    private void onJudgement(JudgementResult result)
    {
        // Capture the combo before scoring so we can tell whether this judgment breaks a run.
        int comboBefore = Score.Combo.Value;

        Score.Apply(result);
        Health.Apply(result);
        Skills.Apply(result, judgingTime);

        tally(result.Result);
        judgements.Add(result);

        if (Health.IsFailed && !failed)
        {
            failed = true;
            Failed?.Invoke();
        }

        // Positive hitsounds are played by the Playfield at press/release time; here we only add the
        // combo-break sting when a miss ends a long enough run. (Note samples fire from input timing.)
        if (!result.Result.IsHit() && comboBefore >= combo_break_threshold)
            hitSounds.PlayComboBreak();
    }

    private void tally(HitResult result)
    {
        switch (result)
        {
            case HitResult.Critical: criticalCount++; break;
            case HitResult.Break: breakCount++; break;
            case HitResult.Hit: hitCount++; break;
            case HitResult.Miss: missCount++; break;
        }
    }

    public override void Update()
    {
        base.Update();

        Skills.Update(GameplayClock.CurrentTime);
    }

    /// <summary>
    /// Resets score/health/skills and the playfield, then starts the clock from lead-in.
    /// </summary>
    public void Restart()
    {
        resetRun();
        Rate = 1;
        hud.ResetDisplay();
        clockContainer.Restart();
    }

    // Everything a run accumulates, back to its start (the clock aside).
    private void resetRun()
    {
        Score.Reset();
        Health.Reset();
        Skills.Reset();
        criticalCount = breakCount = hitCount = missCount = holdBreakCount = 0;
        failed = false;
        playfield.InputPaused = false;
        judgements.Clear();
        recordedInputs.Clear();
        playfield.Reset();
    }

    /// <summary>
    /// Moves a replay to <paramref name="target"/>: the run is re-played silently from its start up to
    /// there — judging depends on everything before it — then the field is redrawn and the clock moved.
    /// Keeps playing if it was.
    /// </summary>
    public void SeekReplay(double target)
    {
        if (!IsReplay)
            throw new InvalidOperationException("Only a replay can be sought; a live run would lose its judgements.");

        bool wasRunning = clockContainer.IsRunning;
        Stop();

        resetRun();
        playfield.Silent = true;

        try
        {
            double time = -clockContainer.LeadInTime;

            while (time < target)
            {
                double next = Math.Min(time + simulation_step, target);
                simulationTime = next;

                // As a frame would: the field (inputs, then judging), then the running skills.
                playfield.SimulateFrame(next);
                Skills.Update(next);

                time = next;
            }
        }
        finally
        {
            simulationTime = null;
            playfield.Silent = false;
        }

        playfield.SettleAfterSimulation();
        hud.SnapDisplay();
        clockContainer.Seek(target);

        if (wasRunning)
            Start();
    }

    /// <summary>Starts (or resumes) the clock; a replay also picks up the slide loops of holds it's holding.</summary>
    public void Start()
    {
        clockContainer.Start();

        if (IsReplay)
            playfield.ResumeHeldSlides();
    }

    /// <summary>
    /// Silences what play makes (a hold slide still looping) without stopping the clock, so the song
    /// plays on: a cleared run's song carries on into the results until it ends.
    /// </summary>
    public void SilenceHitSounds() => hitSounds.StopAllSlides();

    public void Stop()
    {
        clockContainer.Stop();
        // Silence any hold slide loop still running when play halts (pause, finish, quit).
        hitSounds.StopAllSlides();
    }
}
