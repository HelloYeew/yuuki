// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.Screens;

public partial class GameplayScreen : YuukiScreen, IHaveScreenAction
{
    private enum PlayState
    {
        Playing,
        Paused,
        CountingDown,
        Failing,
        Failed,
        Clearing,
        Finished
    }

    private const double end_margin = 2000;

    /// <summary>
    /// An intro is skippable when the first note is at least this far ahead.
    /// </summary>
    private const double skip_threshold = 5000;

    /// <summary>
    /// Skipping lands at least this long before the first note.
    /// </summary>
    private const double skip_lead = 2000;

    /// <summary>
    /// And at least this long before the first note starts scrolling in which matters with slow scroll speeds.
    /// </summary>
    private const double skip_spare = 1000;

    private const double fail_duration = 1500;

    /// <summary>
    /// The countdown's step when the song's tempo is unknown.
    /// </summary>
    private const double countdown_step = 500;

    /// <summary>
    /// Beats of count-in before the song starts (the lead-in), ticked on the song's beat.
    /// </summary>
    private const int count_in_beats = 4;

    // The metronome. The plain tick has its own sample, so retuning the hold ticks doesn't change the
    // count-in.
    // TODO: the accent still borrows the tap hitsound; give it its own sample too and
    // schedule both against the audio clock rather than from Update, which can land a tick up to a
    // frame late.
    private const string tick_accent_sample = "normal-hitnormal.wav";
    private const string tick_sample = "count-in-tick.wav";

    private const double retry_hold_duration = 600;

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool AllowTrackChange => false;

    public override bool HideFooter => true;

    public override bool LoopPreview => false;

    public override bool HideVideo => false;

    public override bool DimBackground => true;

    protected override bool ScalesWithUI => false;

    [Resolved]
    private YuukiScreenStack screenStack { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapNoteStore? noteStore { get; set; }

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

    [Resolved(canBeNull: true)]
    private IAudioStore<ISample>? sampleStore { get; set; }

    [Resolved(canBeNull: true)]
    private IWindow? window { get; set; }

    /// <summary>
    /// The beatmap whose notes this screen loaded, so they can be dropped again on exit. Null when
    /// the run used a generated chart.
    /// </summary>
    private Beatmap? loadedNotesFor;

    /// <summary>
    /// Whether the chart is the beatmap's own (so its runs are saved), not a generated stand-in.
    /// </summary>
    private bool chartFromBeatmap;

    // This run as saved, for the results screen's Replay; null until (and unless) it's saved.
    private ScoreInfo? savedScore;
    private ReplayData? savedReplay;

    private GameplayContainer gameplay = null!;
    private PlayableBeatmap chart = null!;

    // The song's clock; null when the run is silent.
    private TrackClock? trackClock;
    private YuukiColorPalette palette;

    private GameplayMenuOverlay pauseMenu = null!;
    private GameplayMenuOverlay failMenu = null!;
    private ClearBanner clearBanner = null!;
    private SpriteText countdownText = null!;
    private SkipOverlay skipOverlay = null!;
    private Box retryProgress = null!;

    private PlayState state = PlayState.Playing;
    private double endTime;

    // Real time (screen clock) the fail slow-down started.
    private double failStartTime;

    // Real time the retry key went down, or null while it isn't held.
    private double? retryHoldStart;

    // Set by the results screen's Retry, read when it hands control back.
    private bool retryRequested;

    // Whether this run has been written to the score store (once per run).
    private bool saved;

    // The length of the beat in force at a time, from the chart's timing; 0 when the tempo is unknown,
    // which leaves the metronome out.
    private double beatLengthAt(double time) => chart.ControlPoints.HasTiming ? chart.ControlPoints.TimingPointAt(time).BeatLength : 0;

    // The beat the count-in last ticked on; MinValue until the first frame has placed it.
    private long lastCountInBeat = long.MinValue;

    // Bumped by every pause and resume, so a countdown that was interrupted doesn't fire its leftovers.
    private int countdownVersion;

    // Where a skip lands, on a beat of the song, and whether this chart's intro is long enough to skip.
    private double skipTime;
    private bool introSkippable;

    public GameplayScreen()
    {
        ScreenActions = new List<ScreenAction>
        {
            new ScreenAction
            {
                Name = "Pause",
                Action = onEscape,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape))
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = "Gameplay";
        ScreenHelperText.Value = "Hit the notes!";

        palette = Palette;

        BeatmapSet? set = workingBeatmap?.BeatmapSet;
        Beatmap? beatmap = workingBeatmap?.Beatmap;
        int seed = beatmap?.ID ?? set?.ID ?? 1;

        BeatmapNote[] notes = beatmap != null ? noteStore?.Load(beatmap) ?? beatmap.Notes : [];

        if (notes.Length > 0)
            loadedNotesFor = beatmap;

        chartFromBeatmap = beatmap != null && notes.Length > 0;

        if (chartFromBeatmap)
            chart = PlayableBeatmap.FromBeatmap(beatmap!, laneCount: LaneKeyLayouts.Clamp(beatmap!.LaneCount));
        else if (set != null)
            chart = ChartGenerator.Generate(set, laneCount: 4, seed: seed, density: 2);
        else
            chart = ChartGenerator.Generate(120, 60000, laneCount: 4, seed: seed, density: 2);

        endTime = chart.LastNoteTime + end_margin;

        double preempt = ScrollPreempt(config, chart);
        double leadIn = LeadInFor(chart);

        skipTime = skipTargetFor(chart, preempt);
        introSkippable = chart.FirstNoteTime >= skip_threshold && skipTime > -leadIn;

        IAudioChannel? channel = audioManager?.BeginGameplay();
        trackClock = channel != null ? new TrackClock(channel) : null;
        IAdjustableClock? source = trackClock;

        AddRange(new Drawable[]
        {
            // First, so it sees Space before the lanes do (see SkipKeyHandler).
            new SkipKeyHandler { Skip = () => skipOverlay.TriggerSkip() },
            gameplay = new GameplayContainer(chart, source)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Preempt = preempt,
                LeadInTime = leadIn,
                LaneKeys = LaneKeyLayouts.Parse(config?.Get<string>(YuukiConfigManager.LaneKeysSetting(chart.LaneCount)).Value, chart.LaneCount),
                SwipeLeftKey = config?.Get<Key>(YuukiSetting.SwipeLeftKey).Value,
                SwipeRightKey = config?.Get<Key>(YuukiSetting.SwipeRightKey).Value,
                AudioOffset = config?.Get(YuukiSetting.AudioOffset, 0).Value ?? 0,
                VisualOffset = config?.Get(YuukiSetting.VisualOffset, 0).Value ?? 0,
                SongLength = channel != null && channel.Length > 0 ? channel.Length : null,
                PauseRequested = pauseIfRunning
            },
            retryProgress = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 6,
                Width = 0,
                Color = palette.Accent
            },
            // Off until Update has checked the intro is skippable, so it never flashes up on charts that aren't.
            skipOverlay = new SkipOverlay(-leadIn, skipTime, beatLengthAt(chart.FirstNoteTime), () => gameplay.GameplayClock.CurrentTime)
            {
                RequestSkip = skipIntro,
                Active = false
            },
            clearBanner = new ClearBanner(),
            countdownText = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 140, FontWeight.Bold),
                Color = palette.TextLight,
                Alpha = 0
            },
            pauseMenu = new GameplayMenuOverlay("Paused", "Take a breath.", palette,
                (IconUsage.PlayArrow, "Continue", resume),
                (IconUsage.Replay, "Retry", Restart),
                (IconUsage.Logout, "Quit", quit)),
            failMenu = new GameplayMenuOverlay("Failed", "Your health ran out.", palette,
                (IconUsage.Replay, "Retry", Restart),
                (IconUsage.Logout, "Quit", quit))
        });

        gameplay.Failed += onFailed;
    }

    /// <summary>
    /// How long notes are on screen. With "follow BPM" on, the setting is the time at 120 BPM and
    /// scales with the song's tempo, so faster songs scroll faster. Shared with replays.
    /// </summary>
    internal static double ScrollPreempt(YuukiConfigManager? config, PlayableBeatmap chart)
    {
        double preempt = config?.Get(YuukiSetting.ScrollSpeed, 750).Value ?? 750;

        if ((config?.Get<bool>(YuukiSetting.ScrollSpeedFollowsBpm).Value ?? false) && chart.Bpm > 0)
            preempt *= 120 / chart.Bpm;

        return Math.Clamp(preempt, 150, 3000);
    }

    /// <summary>
    /// The lead-in before the song: a count-in of a few beats when the tempo is known, so the ticks
    /// lead into the song. Shared with replays, which start the same way.
    /// </summary>
    internal static double LeadInFor(PlayableBeatmap chart)
    {
        double beat = chart.ControlPoints.HasTiming ? chart.ControlPoints.TimingPointAt(0).BeatLength : 0;
        return beat > 0 ? Math.Clamp(count_in_beats * beat, 1500, 4000) : 2000;
    }

    /// <summary>
    /// Where skipping the intro lands: far enough ahead of the first note for it to scroll in with time
    /// to spare, then back to the beat that starts in, so the skip lands on the song's beat.
    /// </summary>
    private static double skipTargetFor(PlayableBeatmap chart, double preempt)
    {
        double target = chart.FirstNoteTime - Math.Max(skip_lead, preempt + skip_spare);

        if (!chart.ControlPoints.HasTiming)
            return target;

        // A hair of slack so a target already on a beat isn't pushed back a whole one by rounding.
        return chart.ControlPoints.BeatStartAt(target + 1e-6);
    }

    #region Lifecycle

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        this.FadeIn(300);

        // Finish as soon as the track ends. With no track (silent fallback) there's no channel to
        // hook — Update()'s endTime check still ends the run. The channel may fire this off the
        // update thread, so hop back onto it.
        if (audioManager?.GameplayChannel != null)
            audioManager.GameplayChannel.OnEnd += onTrackEnded;

        if (window != null)
        {
            window.FocusLost += onFocusLost;
            window.EnteringBackground += onFocusLost;
        }
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);

        if (retryRequested)
        {
            retryRequested = false;
            this.FadeIn(300);
            Restart();
            return;
        }

        // Results have been dismissed — cascade back to song selection.
        Exit();
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        if (audioManager?.GameplayChannel != null)
            audioManager.GameplayChannel.OnEnd -= onTrackEnded;

        if (window != null)
        {
            window.FocusLost -= onFocusLost;
            window.EnteringBackground -= onFocusLost;
        }

        gameplay.Stop();
        audioManager?.EndGameplay();
        this.FadeOut(300);

        // The chart has been copied into the PlayableBeatmap this screen owns, so the beatmap's own
        // copy can go. Keeping it would mean the library slowly accumulates every chart ever played.
        if (loadedNotesFor != null)
        {
            noteStore?.Unload(loadedNotesFor);
            loadedNotesFor = null;
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // Start the run once fully loaded (includes the container's lead-in).
        gameplay.Restart();
    }

    #endregion

    public override void Update()
    {
        base.Update();

        // Out of the way while notes are coming; back for the pause and fail menus.
        CursorHidden.Value = state is not (PlayState.Paused or PlayState.Failed);

        updateRetryHold();

        skipOverlay.Active = introSkippable && state == PlayState.Playing;

        switch (state)
        {
            case PlayState.Playing:
                updateCountIn();

                if (gameplay.AllJudged || gameplay.GameplayClock.CurrentTime >= endTime)
                    beginClear();
                break;

            case PlayState.Failing:
                updateFailing();
                break;
        }
    }

    #region Pause

    private void onEscape()
    {
        switch (state)
        {
            case PlayState.Playing:
            case PlayState.CountingDown:
                pause();
                break;

            case PlayState.Paused:
                resume();
                break;

            case PlayState.Failed:
                quit();
                break;
        }
    }

    // Leaving the game pauses it: another window taking focus, or a phone's home screen, a call or the
    // notification shade. Either comes in on the window's own thread.
    private void onFocusLost() => Scheduler.Add(pauseIfRunning);

    // The pause button's way in: like Escape, but never resuming or quitting.
    private void pauseIfRunning()
    {
        if (state is PlayState.Playing or PlayState.CountingDown)
            pause();
    }

    private void pause()
    {
        state = PlayState.Paused;
        countdownVersion++;

        gameplay.Stop();
        gameplay.InputPaused = true;
        countdownText.ClearTransforms();
        countdownText.Alpha = 0;

        pauseMenu.Show();
    }

    /// <summary>
    /// Leaves the pause menu through a ticked 3-2-1. The ticks are a beat apart, and play resumes at
    /// the moment that makes the song's next beat land one tick after the "1" — so the count carries
    /// straight on into the music instead of the song restarting mid-beat against it.
    /// </summary>
    private void resume()
    {
        if (state != PlayState.Paused)
            return;

        state = PlayState.CountingDown;
        int version = ++countdownVersion;
        pauseMenu.Hide();

        // A beat per count, doubled up for very fast songs so the count stays readable.
        double time = gameplay.GameplayClock.CurrentTime;
        double beatLength = beatLengthAt(time);
        double step = beatLength > 0 ? beatLength * Math.Max(1, Math.Ceiling(300 / beatLength)) : countdown_step;

        for (int i = 0; i < 3; i++)
        {
            int number = 3 - i;
            bool accent = i == 0;

            Scheduler.AddDelayed(() =>
            {
                if (version != countdownVersion)
                    return;

                showCountdown(number, step);
                playSample(accent ? tick_accent_sample : tick_sample);
            }, i * step);
        }

        // How far past its last beat the song is paused; resuming (beat − that) before the fourth
        // tick would be puts the song's next beat right on it.
        double resumeAt = 3 * step;
        if (beatLength > 0)
        {
            double intoBeat = time - chart.ControlPoints.BeatStartAt(time);
            resumeAt -= beatLength - intoBeat;
        }

        Scheduler.AddDelayed(() =>
        {
            if (version != countdownVersion)
                return;

            countdownText.FadeOut(150);
            state = PlayState.Playing;
            gameplay.Start();
            gameplay.InputPaused = false;
        }, resumeAt);
    }

    private void showCountdown(int number, double step)
    {
        if (state != PlayState.CountingDown)
            return;

        countdownText.Text = number.ToString();
        countdownText.ClearTransforms();
        countdownText.Alpha = 1;
        countdownText.Scale = new Vector2(1.5f);
        countdownText.ScaleTo(1f, step, UserInterfaceConstant.SoftBounce);
    }

    /// <summary>
    /// Ticks the count-in: during the lead-in (before the song's time 0) a tick on every beat of the
    /// song's grid, the first beat of each bar accented.
    /// </summary>
    private void updateCountIn()
    {
        double time = gameplay.GameplayClock.CurrentTime;

        if (!chart.ControlPoints.HasTiming || time >= 0)
            return;

        // Before the first timing point its grid runs backwards, so the count-in leads into its beat.
        long beat = chart.ControlPoints.BeatIndexAt(time);

        // The first frame only places the grid; ticks come as each beat line is crossed.
        if (lastCountInBeat != long.MinValue && beat != lastCountInBeat)
            playSample(ControlPointInfo.IsDownbeat(chart.ControlPoints.TimingPointAt(time), beat) ? tick_accent_sample : tick_sample);

        lastCountInBeat = beat;
    }

    #endregion

    #region Fail

    private void onFailed()
    {
        if (state != PlayState.Playing)
            return;

        state = PlayState.Failing;
        failStartTime = Clock.CurrentTime;

        // The wind-down should drop in pitch like a record slowing to a stop, so it changes the
        // frequency rather than the pitch-keeping tempo the clock uses by default.
        if (trackClock != null)
            trackClock.PreservePitch = false;

        gameplay.InputPaused = true;
        gameplay.AllowJudgement = false;
        gameplay.FadeTo(0.4f, fail_duration, Easing.OutQuint);

        playSample("failsound.wav");
    }

    /// <summary>Winds the song down like a record slowing to a stop, then shows the fail menu.</summary>
    private void updateFailing()
    {
        double progress = Math.Min(1, (Clock.CurrentTime - failStartTime) / fail_duration);
        gameplay.Rate = Math.Max(0.05, 1 - progress);

        if (progress < 1)
            return;

        state = PlayState.Failed;
        gameplay.Stop();
        saveRun(gameplay.BuildResult());
        failMenu.Show();
    }

    #endregion

    #region Clear

    /// <summary>Every note is judged (or time ran out): show the clear banner, save, then the results.</summary>
    private void beginClear()
    {
        if (state != PlayState.Playing)
            return;

        state = PlayState.Clearing;
        gameplay.InputPaused = true;

        GameplayResult result = gameplay.BuildResult();
        bool isNewBest = saveRun(result);

        if (result.IsAllPerfect)
        {
            clearBanner.Play("All Perfect", JudgementColors.ALL_PERFECT);
            playSample("fountain-shoot.wav");
            playSample("spinnerbonus-max.wav");
        }
        else if (result.IsFullCombo)
        {
            clearBanner.Play("Full Combo", JudgementColors.FULL_COMBO);
            playSample("spinnerbonus-max.wav");
        }
        else
        {
            clearBanner.Play("Clear", palette.Control);
            playSample("normal-hitfinish.wav");
        }

        Scheduler.AddDelayed(() => showResults(result, isNewBest), ClearBanner.DURATION);
    }

    private void onTrackEnded() => Scheduler.Add(beginClear);

    private void showResults(GameplayResult result, bool isNewBest)
    {
        if (state != PlayState.Clearing)
            return;

        state = PlayState.Finished;

        // Not stopped: a chart often ends before its song does, and the song plays on into the results
        // until it's done; the results music waits for it. Leaving (or retrying) stops it.
        gameplay.SilenceHitSounds();

        screenStack.Push(new ScoreScreen(result, isNewBest, () => retryRequested = true, savedScore, savedReplay)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });
    }

    #endregion

    #region Retry, skip, quit

    /// <summary>Starts the run over in place: same chart, same track, everything reset.</summary>
    public void Restart()
    {
        state = PlayState.Playing;
        saved = false;
        savedScore = null;
        savedReplay = null;
        retryHoldStart = null;
        countdownVersion++;
        lastCountInBeat = long.MinValue;

        pauseMenu.Hide();
        failMenu.Hide();
        countdownText.ClearTransforms();
        countdownText.Alpha = 0;
        clearBanner.ClearTransforms();
        clearBanner.Alpha = 0;

        gameplay.ClearTransforms();
        gameplay.Alpha = 1;
        gameplay.AllowJudgement = true;
        gameplay.Restart();
        skipOverlay.Reset();

        // Back from a fail's wind-down (Restart has already put the rate back to 1).
        if (trackClock != null)
            trackClock.PreservePitch = true;
    }

    private void quit() => Exit();

    private void skipIntro()
    {
        if (state != PlayState.Playing || gameplay.GameplayClock.CurrentTime >= skipTime)
            return;

        gameplay.Seek(skipTime);
    }

    /// <summary>Holding the backquote key fills a bar across the top; when it's full the run restarts.</summary>
    private void updateRetryHold()
    {
        if (retryHoldStart is not double start)
        {
            if (retryProgress.Width > 0)
                retryProgress.ResizeTo(new Vector2(0, retryProgress.Height), 150, Easing.OutQuint);
            return;
        }

        float progress = (float)Math.Min(1, (Clock.CurrentTime - start) / retry_hold_duration);
        retryProgress.Width = progress;

        if (progress >= 1)
        {
            retryHoldStart = null;
            Restart();
        }
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.IsRepeat)
            return base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Grave when state is PlayState.Playing or PlayState.Paused or PlayState.Failed:
                retryHoldStart = Clock.CurrentTime;
                return true;
        }

        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (e.Key == Key.Grave)
            retryHoldStart = null;

        return base.OnKeyUp(e);
    }

    #endregion

    /// <summary>
    /// Records a finished or failed run once, for play counts and rankings. Generated charts aren't
    /// saved. Returns whether it beat the previous best (a fail never does).
    /// </summary>
    private bool saveRun(GameplayResult result)
    {
        if (saved || !chartFromBeatmap || scoreStore == null || workingBeatmap?.Beatmap == null)
            return false;

        saved = true;

        Beatmap beatmap = workingBeatmap.Beatmap;
        ScoreInfo? previousBest = scoreStore.GetPersonalBest(beatmap);

        // With the replay: watchable from the results, and again (with the full breakdown) whenever
        // the score is opened later.
        savedScore = ScoreInfo.From(result, beatmap.ID, beatmap.BeatmapSet?.ID ?? 0);
        savedReplay = gameplay.BuildReplay();
        scoreStore.Add(savedScore, savedReplay);

        return !result.Failed && (previousBest == null || result.Score > previousBest.Score);
    }

    private void playSample(string file)
    {
        // Played fire-and-forget; missing samples (headless tests) are skipped.
        ISample? sample = sampleStore?.Get("Gameplay/" + file);
        sample?.Play();
    }

    /// <summary>
    /// Takes Space for the intro skip while one is possible. Key presses reach the deepest drawables
    /// first, so where Space is a lane key (5 and 7 lanes) the playfield would always take it as a lane
    /// press; this sits ahead of the gameplay in the screen's children to see it first. Nothing is
    /// lost: the skip period ends well before the first note.
    /// </summary>
    private partial class SkipKeyHandler : Container
    {
        public Func<bool> Skip = null!;

        public override bool OnKeyDown(KeyEvent e) => !e.IsRepeat && e.Key == Key.Space && Skip();
    }
}
