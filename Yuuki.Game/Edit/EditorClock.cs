// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Audio;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps.ControlPoints;

namespace Yuuki.Game.Edit;

/// <summary>
/// The editor's time: the song, through the same clock chain gameplay uses, kept between 0 and the end
/// of the track
/// </summary>
/// <remarks>
/// <para>
/// Seeks snap to the beat divisor (<see cref="SeekSnapped"/>, and the stepping in
/// <see cref="SeekForward"/>/<see cref="SeekBackward"/>), never crossing into the next timing section.
/// While stopped, a seek glides over 300 ms instead of jumping; <see cref="CurrentTimeAccurate"/> is where
/// it will land. Reaching the end stops the clock there; starting again from the end starts from 0.
/// </para>
/// <para>
/// <see cref="Rate"/> is the playback speed, applied as tempo so the pitch stays put. Call
/// <see cref="ProcessFrame"/> once per frame before anything reads the time.
/// </para>
/// </remarks>
public class EditorClock
{
    private const double smooth_seek_duration = 300;
    private const double smooth_seek_max_distance = 5000;

    private readonly GameplayClock clock;
    private readonly Func<ControlPointInfo> timing;
    private readonly EditorBeatDivisor divisor;

    /// <summary>
    /// How long the song is: the track's length, or, with no track, a minute past the last note.
    /// </summary>
    public double TrackLength { get; set; }

    private double? seekFrom;
    private double seekTarget;
    private double seekStartedAt;
    private readonly IClock realTime = new TimeSourceClock();

    /// <param name="channel">The song. Null runs a silent clock (no track).</param>
    /// <param name="trackLength">How long the song is, in ms.</param>
    /// <param name="timing">The timing to snap seeks to, asked each time since edits replace it.</param>
    /// <param name="divisor">The beat divisor seeks snap to.</param>
    /// <param name="audioOffset">The player's audio offset, as gameplay applies it.</param>
    public EditorClock(IAudioChannel? channel, double trackLength, Func<ControlPointInfo> timing, EditorBeatDivisor divisor, double audioOffset = 0)
    {
        this.timing = timing;
        this.divisor = divisor;

        TrackLength = trackLength;

        IAdjustableClock source = channel != null ? new TrackClock(channel) : new StopwatchClock();
        clock = new GameplayClock(source) { Offset = audioOffset };
    }

    /// <summary>
    /// The clock the editor's drawables run on.
    /// </summary>
    public GameplayClock Clock => clock;

    public double CurrentTime => Math.Clamp(clock.CurrentTime, 0, TrackLength);

    /// <summary>
    /// Where the clock is going: the end of a glide in progress, else the current time.
    /// </summary>
    public double CurrentTimeAccurate => seekFrom.HasValue ? seekTarget : CurrentTime;

    public bool IsRunning => clock.IsRunning;

    public bool IsSeeking => seekFrom.HasValue;

    /// <summary>
    /// Playback speed, 1 = normal. Pitch is preserved.
    /// </summary>
    public double Rate
    {
        get => clock.Rate;
        set => clock.Rate = value;
    }

    public void ProcessFrame()
    {
        if (seekFrom is double from)
        {
            double progress = Math.Clamp((realTime.CurrentTime - seekStartedAt) / smooth_seek_duration, 0, 1);
            double eased = 1 - Math.Pow(1 - progress, 5);

            clock.Seek(from + (seekTarget - from) * eased);

            if (progress >= 1)
                seekFrom = null;
        }

        clock.ProcessFrame();

        if (clock.IsRunning && clock.CurrentTime >= TrackLength)
        {
            clock.Stop();
            clock.Seek(TrackLength);
            clock.ProcessFrame();
        }
    }

    public void Start()
    {
        seekFrom = null;

        if (CurrentTime >= TrackLength - 1)
            clock.Seek(0);

        clock.Start();
    }

    public void Stop()
    {
        clock.Stop();
    }

    public void Toggle()
    {
        if (IsRunning)
            Stop();
        else
            Start();
    }

    /// <summary>
    /// Moves to <paramref name="time"/> at once.
    /// </summary>
    public void Seek(double time)
    {
        seekFrom = null;
        clock.Seek(Math.Clamp(time, 0, TrackLength));
    }

    /// <summary>
    /// Glides to <paramref name="time"/> while stopped and close by, jumps otherwise.
    /// </summary>
    public void SeekSmoothlyTo(double time)
    {
        time = Math.Clamp(time, 0, TrackLength);

        if (IsRunning || Math.Abs(time - CurrentTime) > smooth_seek_max_distance)
        {
            Seek(time);
            return;
        }

        seekFrom = CurrentTime;
        seekTarget = time;
        seekStartedAt = realTime.CurrentTime;
    }

    /// <summary>
    /// Seeks to the divisor tick nearest <paramref name="time"/>.
    /// </summary>
    public void SeekSnapped(double time) => SeekSmoothlyTo(timing().GetClosestSnappedTime(time, divisor.Value.Value));

    public void SeekForward(bool snapped = true, double amount = 1) => seekStep(1, snapped, amount);

    public void SeekBackward(bool snapped = true, double amount = 1) => seekStep(-1, snapped, amount);

    /// <summary>
    /// step seek: one divisor tick (times <paramref name="amount"/>) along the timing section in
    /// force, landing on its grid, never past the next section's start.
    /// </summary>
    private void seekStep(int direction, bool snapped, double amount)
    {
        var info = timing();
        double current = CurrentTimeAccurate;

        TimingPoint point = info.TimingPointAt(current);

        // Going back from exactly a section's start steps by the section before it.
        if (direction < 0 && point.Time.Equals(current))
            point = info.TimingPointAt(current - 1);

        double step = point.BeatLength / divisor.Value.Value * amount;
        double target = current + step * direction;

        if (!snapped || !info.HasTiming)
        {
            SeekSmoothlyTo(target);
            return;
        }

        double fromPoint = target - point.Time;
        long tick = direction > 0 ? (long)Math.Floor(fromPoint / step + 1e-6) : (long)Math.Ceiling(fromPoint / step - 1e-6);
        target = point.Time + tick * step;

        var next = info.TimingPointAfter(point.Time);

        if (next != null && target > next.Time)
            target = next.Time;

        // Rounding can land back where we started; then go a tick further.
        if (Math.Abs(target - current) < 0.5)
            target = point.Time + (tick + direction) * step;

        if (target < point.Time && info.TimingPoints.Count > 0 && point != info.TimingPoints[0])
            target = point.Time;

        SeekSmoothlyTo(target);
    }
}
