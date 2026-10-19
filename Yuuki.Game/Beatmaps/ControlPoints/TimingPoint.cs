// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Text.Json.Serialization;

namespace Yuuki.Game.Beatmaps.ControlPoints;

/// <summary>
/// A point where the song's tempo or meter is set. Everything from <see cref="Time"/>
/// until the next timing point is on its beat grid.
/// </summary>
public class TimingPoint : IEquatable<TimingPoint>
{
    /// <summary>
    /// The shortest beat accepted (10,000 BPM), below it a beat grid stops meaning anything.
    /// </summary>
    public const double MIN_BEAT_LENGTH = 6;

    public const double MAX_BEAT_LENGTH = 60000;

    /// <summary>
    /// Where this point takes effect, in milliseconds on the audio timeline. This is the song's offset
    /// when it is the first point: a beat lands exactly here.
    /// </summary>
    public double Time { get; set; }

    private double beatLength = 500;

    /// <summary>
    /// The length of one beat in milliseconds. 500 is 120 BPM.
    /// </summary>
    public double BeatLength
    {
        get => beatLength;
        set => beatLength = Math.Clamp(value, MIN_BEAT_LENGTH, MAX_BEAT_LENGTH);
    }

    /// <summary>
    /// How the beats group into bars. Only the numerator affects the grid; a bar is that many beats.
    /// </summary>
    public TimeSignature TimeSignature { get; set; } = TimeSignature.CommonTime;

    [JsonIgnore]
    public double Bpm
    {
        get => 60000 / BeatLength;
        set => BeatLength = 60000 / value;
    }

    /// <summary>
    /// Beats per bar, never less than 1.
    /// </summary>
    [JsonIgnore]
    public int BeatsPerBar => Math.Max(1, TimeSignature.Numerator);

    public TimingPoint()
    {
    }

    public TimingPoint(double time, double beatLength, TimeSignature? timeSignature = null)
    {
        Time = time;
        BeatLength = beatLength;
        TimeSignature = timeSignature ?? TimeSignature.CommonTime;
    }

    public TimingPoint Clone() => new TimingPoint(Time, BeatLength, TimeSignature);

    public bool Equals(TimingPoint? other) =>
        other != null && Time.Equals(other.Time) && BeatLength.Equals(other.BeatLength) && TimeSignature.Equals(other.TimeSignature);

    public override bool Equals(object? obj) => obj is TimingPoint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Time, BeatLength, TimeSignature);

    public override string ToString() => $"{Bpm:0.##} BPM {TimeSignature} @ {Time:0.##}ms";
}
