// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Audio;

public partial class YuukiAudioManager : Component
{
    [Resolved]
    private IAudioManager audioManager { get; set; } = null!;

    [Resolved]
    private WorkingBeatmap workingBeatmap { get; set; } = null!;

    [Resolved]
    private BeatmapsCollection beatmapsCollection { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private IAudioStore<ITrack>? resourceTrackStore { get; set; }

    private const string results_track_path = "theme/result.mp3";

    /// <summary>
    /// How long the result music takes to fade in.
    /// </summary>
    private const double results_fade_in = 1200;

    /// <summary>
    /// When the result music started for its fade-in. NaN once it's at full volume (or not playing).
    /// </summary>
    private double resultsFadeStart = double.NaN;

    /// <summary>
    /// Whether the result music has been asked for but is waiting for the gameplay song to play out
    /// (a chart often ends before its song does).
    /// </summary>
    public bool ResultsMusicWaiting { get; private set; }

    // Ducks in force (see Duck): the music's low-pass sits at the lowest cutoff among them.
    private readonly List<DuckParameters> duckOperations = new List<DuckParameters>();

    // The cutoff easing toward its target, and the filters applying it, one per channel ducked.
    private double cutoffFrom = ILowPassFilter.DefaultCutoffFrequency;
    private double cutoffTo = ILowPassFilter.DefaultCutoffFrequency;
    private double cutoffStart;
    private double cutoffDuration;
    private Easing cutoffEasing;
    private double currentCutoff = ILowPassFilter.DefaultCutoffFrequency;
    private readonly Dictionary<IAudioChannel, ILowPassFilter?> duckFilters = new Dictionary<IAudioChannel, ILowPassFilter?>();

    private TrackStore diskTrackStore = null!;
    private IAudioChannel channel = null!;
    private IAudioChannel? gameplayChannel = null!;
    private IAudioChannel? resultsChannel;

    public bool InGameplay { get; private set; }

    public IAudioChannel CurrentChannel => channel;
    public IAudioChannel? GameplayChannel => gameplayChannel;

    /// <summary>
    /// Pauses the menu preview and returns a fresh channel for the current beatmap's track, starting
    /// at position 0 and not yet playing — the gameplay clock drives it. Returns null if there's no
    /// track (gameplay then runs silent on its fallback clock).
    /// </summary>
    /// <param name="setOverride">
    /// Whose track to play instead of the working beatmap's: the editor's copy of a set whose song it has
    /// just changed, which isn't the working set (making it one would start the menu preview of it).
    /// </param>
    public IAudioChannel? BeginGameplay(BeatmapSet? setOverride = null)
    {
        channel?.Pause();

        gameplayChannel?.Stop();
        dropDuckFilter(gameplayChannel);
        gameplayChannel?.Dispose();
        gameplayChannel = null;

        BeatmapSet set = setOverride ?? workingBeatmap.BeatmapSet;
        if (set.IsNull())
            return null;

        // No song file (a set whose track is missing): gameplay and the editor run silent instead.
        ITrack? track = getTrack(set);
        if (track.IsNull())
            return null;

        // None where the file can't be decoded, which runs silent too.
        gameplayChannel = track.GetChannel();
        if (gameplayChannel.IsNull())
            return null;

        gameplayChannel.Looping = false;

        InGameplay = true;
        return gameplayChannel;
    }

    /// <summary>
    /// Stops the gameplay channel and resumes the menu preview, continuing the song from where
    /// gameplay left off rather than jumping back to the preview point.
    /// </summary>
    public void EndGameplay()
    {
        // Capture the play position before tearing the gameplay channel down.
        double resumeTime = gameplayChannel?.CurrentTime ?? -1;

        gameplayChannel?.Stop();
        dropDuckFilter(gameplayChannel);
        gameplayChannel?.Dispose();
        gameplayChannel = null;

        InGameplay = false;

        if (channel.IsNull())
            return;

        channel.Play();

        // Pick the song back up where gameplay was — unless it had essentially finished, since
        // seeking to the very end would immediately fire the advance-to-random handler. Song select
        // won't re-seek to the preview point on return (that only happens on the first entry).
        if (resumeTime > 0 && resumeTime < channel.Length - 1000)
            channel.CurrentTime = resumeTime;
    }

    /// <summary>
    /// Starts the score screen's looping BGM as soon as the gameplay song is done when the chart ends
    /// before its song does, the song plays on into the results until it ends and only
    /// then does this come in. <see cref="EndGameplay"/> resumes the menu preview after.
    /// </summary>
    public void BeginResultsMusic()
    {
        EndResultsMusic();

        if (gameplayChannel.IsNotNull() && gameplayChannel.IsRunning.Value)
        {
            ResultsMusicWaiting = true;
            return;
        }

        startResultsMusic();
    }

    private void startResultsMusic()
    {
        ITrack? track = resourceTrackStore?.Get(results_track_path);
        if (track.IsNull())
            return;

        resultsChannel = track.GetChannel();
        resultsChannel.Looping = true;
        resultsChannel.Volume.Value = 0;
        resultsChannel.Play();
        resultsFadeStart = Clock.CurrentTime;
    }

    public override void Update()
    {
        base.Update();

        // The song the results were waiting on has played out (it ends, or something stops it).
        if (ResultsMusicWaiting && (gameplayChannel.IsNull() || !gameplayChannel.IsRunning.Value))
        {
            ResultsMusicWaiting = false;
            startResultsMusic();
        }

        updateResultsFadeIn();
        updateDuck();
    }

    // Ease the results music in rather than cutting it on at full volume.
    private void updateResultsFadeIn()
    {
        if (resultsChannel.IsNull() || double.IsNaN(resultsFadeStart))
            return;

        double progress = Math.Clamp((Clock.CurrentTime - resultsFadeStart) / results_fade_in, 0, 1);
        resultsChannel.Volume.Value = progress;

        if (progress >= 1)
            resultsFadeStart = double.NaN;
    }

    /// <summary>
    /// Ducks the music and held until the returned handle is disposed. With
    /// several in force, the lowest cutoff wins, and letting one go eases back to the next, or to clear.
    /// Applies to whatever music is playing (menu, results, or gameplay), including songs that start
    /// while it's held. Nothing is heard on a backend without filters.
    /// </summary>
    public IDisposable Duck(DuckParameters? parameters = null)
    {
        parameters ??= new DuckParameters();
        duckOperations.Add(parameters);

        DuckParameters lowest = duckOperations.MinBy(p => p.DuckCutoffTo)!;
        easeCutoffTo(lowest.DuckCutoffTo, lowest.DuckDuration, lowest.DuckEasing);

        return new DuckHandle(() => Scheduler.Add(() =>
        {
            if (!duckOperations.Remove(parameters))
                return;

            DuckParameters? next = duckOperations.MinBy(p => p.DuckCutoffTo);
            easeCutoffTo(next?.DuckCutoffTo ?? ILowPassFilter.DefaultCutoffFrequency, parameters.RestoreDuration, parameters.RestoreEasing);
        }));
    }

    /// <summary>
    /// Ducks the music, then lets it go by itself after <paramref name="delayUntilRestore"/> ms.
    /// </summary>
    public void DuckMomentarily(double delayUntilRestore, DuckParameters? parameters = null)
    {
        IDisposable duck = Duck(parameters);
        Scheduler.AddDelayed(duck.Dispose, delayUntilRestore);
    }

    private void easeCutoffTo(double target, double duration, Easing easing)
    {
        cutoffFrom = currentCutoff;
        cutoffTo = target;
        cutoffStart = Clock.CurrentTime;
        cutoffDuration = duration;
        cutoffEasing = easing;
    }

    private void updateDuck()
    {
        double progress = cutoffDuration > 0 ? Math.Clamp((Clock.CurrentTime - cutoffStart) / cutoffDuration, 0, 1) : 1;
        currentCutoff = cutoffFrom + (cutoffTo - cutoffFrom) * EasingFunctions.Apply(cutoffEasing, progress);

        bool ducked = currentCutoff < ILowPassFilter.DefaultCutoffFrequency - 1 || duckOperations.Count > 0;

        if (!ducked)
        {
            releaseDuckFilters();
            return;
        }

        // Every channel playing music gets a filter. Ones replaced or let go drop theirs where they're
        // disposed (dropDuckFilter).
        applyDuckFilter(channel);
        applyDuckFilter(resultsChannel);
        applyDuckFilter(gameplayChannel);
    }

    private void applyDuckFilter(IAudioChannel? music)
    {
        if (music.IsNull())
            return;

        if (!duckFilters.TryGetValue(music, out ILowPassFilter? filter))
            duckFilters[music] = filter = music.AddLowPassFilter();

        if (filter.IsNotNull())
            filter.CutoffFrequency.Value = currentCutoff;
    }

    /// <summary>
    /// Detaches the duck's filter from a channel about to be disposed (a new one is fitted next frame).
    /// </summary>
    private void dropDuckFilter(IAudioChannel? music)
    {
        if (music.IsNull() || !duckFilters.Remove(music, out ILowPassFilter? filter))
            return;

        filter?.Dispose();
    }

    private void releaseDuckFilters()
    {
        if (duckFilters.Count == 0)
            return;

        foreach (ILowPassFilter? filter in duckFilters.Values)
            filter?.Dispose();

        duckFilters.Clear();
    }

    private sealed class DuckHandle : IDisposable
    {
        private Action? release;

        public DuckHandle(Action release) => this.release = release;

        public void Dispose()
        {
            release?.Invoke();
            release = null;
        }
    }

    /// <summary>
    /// Stops the score screen's BGM, if it's playing (or keeps it from starting, if it's waiting).
    /// </summary>
    public void EndResultsMusic()
    {
        ResultsMusicWaiting = false;
        resultsChannel?.Stop();
        dropDuckFilter(resultsChannel);
        resultsChannel?.Dispose();
        resultsChannel = null;
        resultsFadeStart = double.NaN;
    }

    public ReactiveBool LoopPreview { get; } = new ReactiveBool(false);

    /// <summary>
    /// Whether the song may be switched (<see cref="PlayNext"/>, <see cref="PlayPrevious"/>,
    /// <see cref="PlayRandom"/>, <see cref="PlaySet"/>). Follows the current screen's
    /// <c>AllowTrackChange</c>; while off, those calls do nothing.
    /// </summary>
    public ReactiveBool AllowTrackChange { get; } = new ReactiveBool(true);

    /// <summary>
    /// Whether the menu preview track is currently playing (false when paused or absent).
    /// </summary>
    public bool IsPlaying => channel.IsNotNull() && channel.IsRunning.Value;

    /// <summary>
    /// Play/pause the menu preview track (used by the music player). No-op during gameplay.
    /// </summary>
    public void TogglePlayback()
    {
        // The menu track is paused under gameplay and the results BGM; resuming it would play over them.
        if (channel.IsNull() || InGameplay)
            return;

        if (channel.IsRunning.Value)
            channel.Pause();
        else
            channel.Play();
    }

    /// <summary>
    /// Switches the working beatmap set to <paramref name="set"/> (e.g., picked from the music player's list).
    /// </summary>
    public void PlaySet(BeatmapSet set)
    {
        if (AllowTrackChange.Value)
            workingBeatmap.BeatmapSet = set;
    }

    /// <summary>
    /// Switches the working beatmap set to the next non-hidden one (wrapping).
    /// </summary>
    public void PlayNext() => step(1);

    /// <summary>
    /// Switches the working beatmap set to the previous non-hidden one (wrapping).
    /// </summary>
    public void PlayPrevious() => step(-1);

    /// <summary>
    /// The current play position of the menu track in ms, or 0 when there's no track.
    /// </summary>
    public double CurrentTime => channel?.CurrentTime ?? 0;

    /// <summary>
    /// The length of the menu track in ms, or 0 when there's no track.
    /// </summary>
    public double TrackLength => channel?.Length ?? 0;

    /// <summary>
    /// Seeks the menu track to a fraction (0–1) of its length. Used by the music player scrubber.
    /// </summary>
    public void SeekToFraction(float fraction)
    {
        if (channel.IsNull() || channel.Length <= 0)
            return;

        channel.CurrentTime = Math.Clamp(fraction, 0f, 1f) * channel.Length;
    }

    /// <summary>
    /// Switches to a random non-hidden beatmap set.
    /// </summary>
    public void PlayRandom()
    {
        if (!AllowTrackChange.Value)
            return;

        // Another set than the one playing, where there is one: picking the same changes nothing.
        BeatmapSet? next = beatmapsCollection.GetRandomBeatmapSet(workingBeatmap.BeatmapSet);
        if (next.IsNotNull())
            workingBeatmap.BeatmapSet = next;
    }

    private void step(int direction)
    {
        if (!AllowTrackChange.Value)
            return;

        var sets = beatmapsCollection.BeatmapSets.Where(s => !s.Hide).ToList();
        if (sets.Count == 0)
            return;

        int index = workingBeatmap.BeatmapSet.IsNotNull()
            ? sets.FindIndex(s => s.ID == workingBeatmap.BeatmapSet.ID)
            : -1;

        // Wrap in both directions.
        index = ((index + direction) % sets.Count + sets.Count) % sets.Count;
        workingBeatmap.BeatmapSet = sets[index];
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (storage.IsNotNull())
            diskTrackStore = new TrackStore(storage, audioManager);

        workingBeatmap.ReactiveWorkingBeatmapSet.BindValueChanged(e => onBeatmapSetChanged(e.OldValue, e.NewValue), true);
        LoopPreview.BindValueChanged(e =>
        {
            applyPreviewLoopState();
        }, true);
    }

    /// <summary>
    /// Applies the current <see cref="LoopPreview"/> setting to the active menu channel: loop the
    /// preview snippet when set, otherwise play through and let <see cref="onCurrentChannelEnded"/>
    /// advance to a random beatmap. The advance-on-end handler is wired once per channel in
    /// <see cref="onBeatmapSetChanged"/>, so toggling loop state never stacks duplicate handlers.
    /// </summary>
    private void applyPreviewLoopState()
    {
        if (channel.IsNull())
            return;

        if (LoopPreview.Value)
        {
            channel.Looping = true;
            if (workingBeatmap.BeatmapSet.IsNotNull())
                channel.RestartPoint = workingBeatmap.BeatmapSet.PreviewTime;
        }
        else
        {
            channel.Looping = false;
        }
    }

    private void onCurrentChannelEnded()
    {
        // A screen that has the song fixed (results, setup, gameplay) keeps it: the song starts over
        // from its preview point rather than moving on, which would change the beatmap under it. So does
        // a looping preview that stopped anyway, which it does when the song ran out just as the loop was
        // turned on (song select entered at the song's end): left alone, song select would be silent. One
        // still running has wrapped by itself, as a looping channel reports every time it does.
        if (LoopPreview.Value || !AllowTrackChange.Value)
        {
            bool wrapped = LoopPreview.Value && channel.IsNotNull() && channel.IsRunning.Value;

            if (!wrapped && channel.IsNotNull() && workingBeatmap.BeatmapSet.IsNotNull())
            {
                channel.CurrentTime = workingBeatmap.BeatmapSet.PreviewTime;
                channel.Play();
            }

            return;
        }

        var next = beatmapsCollection.GetRandomBeatmapSet(workingBeatmap.BeatmapSet);

        // Picking the set that's playing changes nothing, so the song would stay stopped: the only set
        // there is plays again from the start instead.
        if (next.IsNull() || Equals(next, workingBeatmap.BeatmapSet))
        {
            if (channel.IsNotNull())
            {
                channel.CurrentTime = 0;
                channel.Play();
            }

            return;
        }

        workingBeatmap.BeatmapSet = next;
    }

    private void onBeatmapSetChanged(BeatmapSet oldSet, BeatmapSet newSet)
    {
        if (newSet.IsNull())
            return;

        if (Equals(oldSet, newSet) && channel.IsNotNull())
            return;

        dropDuckFilter(channel);
        channel?.Stop();
        channel?.Dispose();
        channel = null;

        ITrack track = getTrack(newSet);
        if (track.IsNull())
            return;

        // None where the file can't be decoded: the set stays picked, just silent.
        channel = track.GetChannel();
        if (channel.IsNull())
            return;

        // A single advance-on-end subscription per channel. The channel is recreated on every set
        // change, so this never accumulates across sets.
        channel.OnEnd += onCurrentChannelEnded;

        applyPreviewLoopState();
        if (LoopPreview.Value)
            channel.CurrentTime = newSet.PreviewTime;

        channel.Play();
    }

    private ITrack getTrack(BeatmapSet set)
    {
        if (set.UseLocalSource)
            return string.IsNullOrEmpty(set.TrackPath) ? null : resourceTrackStore?.Get(set.TrackPath);

        return string.IsNullOrEmpty(set.TrackStoragePath) ? null : diskTrackStore?.Get(set.TrackStoragePath);
    }
}
