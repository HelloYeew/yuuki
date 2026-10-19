// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Drawables;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;

namespace Yuuki.Game.Graphics.Containers;

public partial class BeatSyncContainer : Container
{
    private const double default_bpm = 60;

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    /// <summary>
    /// The number of subdivisions per beat. 1 = one event per quarter-note beat, 2 = eighth notes,
    /// 4 = sixteenth notes, and so on.
    /// </summary>
    public int Divisor { get; set; } = 1;

    /// <summary>
    /// The meter of the timing point the beat is currently following.
    /// </summary>
    public TimeSignature TimeSignature { get; private set; } = TimeSignature.CommonTime;

    /// <summary>
    /// Fires each beat this many milliseconds early, letting animations pre-empt the audible beat.
    /// </summary>
    public double EarlyActivationMilliseconds { get; set; }

    /// <summary>
    /// Beats shorter than this (in ms) are doubled up so extreme BPMs do not fire absurdly fast.
    /// Zero disables the clamp.
    /// </summary>
    public double MinimumBeatLength { get; set; }

    /// <summary>
    /// When true the beat keeps ticking off this drawable's own clock even when no track is playing, at
    /// the selected song's tempo (or <see cref="UseDefaultTempoWhenStopped"/>).
    /// </summary>
    public bool AllowBeatSyncWhenStopped { get; set; }

    /// <summary>
    /// With nothing playing (and <see cref="AllowBeatSyncWhenStopped"/>), keep a steady default 60 BPM
    /// rather than the selected song's tempo, which nothing is playing to.
    /// </summary>
    public bool UseDefaultTempoWhenStopped { get; set; }

    /// <summary>
    /// Fired on every new beat. Arguments are the running beat index, the effective beat length in
    /// milliseconds, and the current channel amplitude snapshot.
    /// </summary>
    public event Action<int, double, ChannelAmplitudes>? NewBeat;

    /// <summary>
    /// The most recently fired beat index (-1 before any beat has fired).
    /// </summary>
    protected int CurrentBeat { get; private set; } = -1;

    /// <summary>
    /// The effective beat length (ms) currently in use, already divided by <see cref="Divisor"/>.
    /// </summary>
    public double BeatLength { get; private set; }

    /// <summary>
    /// How far the playback position is past the latest beat, in ms (0 to <see cref="BeatLength"/>),
    /// as of this frame. For animations that follow the beat continuously rather than on each new one.
    /// </summary>
    public double TimeSinceBeat { get; private set; }

    /// <summary>
    /// True when the beat is currently being driven by a playing track (as opposed to the fallback clock).
    /// </summary>
    protected bool IsBeatSyncedWithTrack { get; private set; }

    private int lastBeat = -1;

    public override void Update()
    {
        base.Update();

        IAudioChannel? channel = audioManager?.CurrentChannel;
        var amplitudes = ChannelAmplitudes.Empty;
        double currentTime;

        IsBeatSyncedWithTrack = channel != null && channel.IsRunning.Value;

        if (IsBeatSyncedWithTrack)
        {
            currentTime = channel!.CurrentTime;
            amplitudes = channel.CurrentAmplitudes;
        }
        else if (AllowBeatSyncWhenStopped)
        {
            currentTime = Clock.CurrentTime;
        }
        else
        {
            // Nothing playing and no fallback requested: reset so the next play starts cleanly.
            lastBeat = -1;
            return;
        }

        double time = currentTime + EarlyActivationMilliseconds;
        TimingPoint point = (IsBeatSyncedWithTrack || !UseDefaultTempoWhenStopped ? currentTiming() : default_timing).TimingPointAt(time);

        // A tempo of nothing (a chart's broken timing) would fire every frame: keep the default instead.
        if (!(point.BeatLength > 0) || double.IsInfinity(point.BeatLength))
            point = default_timing.TimingPointAt(time);

        double beatLength = point.BeatLength / Math.Max(1, Divisor);

        while (MinimumBeatLength > 0 && beatLength > 0 && beatLength < MinimumBeatLength)
            beatLength *= 2;

        BeatLength = beatLength;
        TimeSignature = point.TimeSignature;

        double position = time - point.Time;
        int beat = (int)Math.Floor(position / beatLength);
        TimeSinceBeat = position - beat * beatLength;

        if (beat == lastBeat && point == lastPoint)
            return;

        lastBeat = beat;
        lastPoint = point;
        CurrentBeat = beat;

        OnNewBeat(beat, beatLength, TimeSignature, amplitudes);
        NewBeat?.Invoke(beat, beatLength, amplitudes);
    }

    /// <summary>
    /// Called once whenever the playback position crosses into a new beat. Override to react to the
    /// beat
    /// </summary>
    /// <param name="beat">The running beat index on the bar-agnostic grid (can be negative early on).</param>
    /// <param name="beatLength">The effective beat length in milliseconds.</param>
    /// <param name="timeSignature">The meter of the timing point the beat belongs to.</param>
    /// <param name="amplitudes">The channel amplitude snapshot at the moment the beat fired.</param>
    protected virtual void OnNewBeat(int beat, double beatLength, TimeSignature timeSignature, ChannelAmplitudes amplitudes)
    {
    }

    /// <summary>
    /// The zero-based index of the given beat within its bar, honouring <see cref="TimeSignature"/>.
    /// A value of 0 is the downbeat of the bar.
    /// </summary>
    public int CurrentBeatInBar(int beat)
    {
        int n = Math.Max(1, TimeSignature.Numerator);
        return ((beat % n) + n) % n;
    }

    private TimingPoint? lastPoint;

    /// <summary>The steady default beat: with nothing playing (<see cref="UseDefaultTempoWhenStopped"/>), or no usable tempo.</summary>
    private static readonly ControlPointInfo default_timing = ControlPointInfo.FromBpm(default_bpm);

    private BeatmapSet? timingSet;
    private ControlPointInfo timing = new ControlPointInfo();

    /// <summary>
    /// The timing of the song that is playing: the selected difficulty's, or, when the music player has
    /// moved on to another set (which leaves the selected difficulty behind), that set's first difficulty
    /// with timing. A chart from before timing points falls back to the set's BPM from 0.
    /// </summary>
    private ControlPointInfo currentTiming()
    {
        var set = workingBeatmap?.BeatmapSet;
        var selected = workingBeatmap?.Beatmap;

        if (selected?.BeatmapSet != null && set != null && selected.BeatmapSet.ID == set.ID && selected.ControlPoints.HasTiming)
            return selected.ControlPoints;

        if (set != timingSet)
        {
            timingSet = set;
            timing = findSetTiming(set);
        }

        return timing;
    }

    private ControlPointInfo findSetTiming(BeatmapSet? set)
    {
        if (set == null)
            return ControlPointInfo.FromBpm(default_bpm);

        if (beatmapsCollection != null)
        {
            foreach (var beatmap in beatmapsCollection.GetBeatmapsFromBeatmapSet(set, false))
            {
                if (beatmap.ControlPoints.HasTiming)
                    return beatmap.ControlPoints;
            }
        }

        return ControlPointInfo.FromBpm(set.BPM > 0 ? set.BPM : default_bpm);
    }
}
