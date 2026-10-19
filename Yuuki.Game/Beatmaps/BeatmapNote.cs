// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// A single hit object in a <see cref="PlayableBeatmap"/>. Times are in milliseconds on the
/// gameplay timeline. For <see cref="NoteType.Tap"/> notes <see cref="EndTime"/> equals
/// <see cref="StartTime"/>; for <see cref="NoteType.Hold"/> notes it marks the release time.
/// </summary>
public class BeatmapNote
{
    public NoteLane Lane { get; set; }

    public NoteType Type { get; set; }

    public double StartTime { get; set; }

    public double EndTime { get; set; }

    /// <summary>
    /// The sample bank this note's hitsounds are drawn from. Defaults to <see cref="SampleBank.Normal"/>.
    /// </summary>
    public SampleBank Bank { get; set; } = SampleBank.Normal;

    /// <summary>
    /// Extra hitsounds layered on top of the bank's <c>hitnormal</c>. Defaults to none.
    /// </summary>
    public HitSampleAddition Additions { get; set; } = HitSampleAddition.None;

    /// <summary>
    /// For a <see cref="NoteType.Swipe"/>, which swipe input hits it. Ignored by other types.
    /// </summary>
    public SwipeDirection Direction { get; set; } = SwipeDirection.Any;

    /// <summary>
    /// For a <see cref="NoteType.Skill"/>, what it does when hit. Ignored by other types.
    /// </summary>
    public SkillType Skill { get; set; } = SkillType.Heal;

    /// <summary>
    /// For a <see cref="NoteType.Hold"/>, the lane its tail ends in — the hold drifts across to it while
    /// held. Input stays on the starting lane's key (the character stays anchored there). Null keeps it
    /// in <see cref="Lane"/>.
    /// </summary>
    public NoteLane? EndLane { get; set; }

    /// <summary>
    /// The length of the note in milliseconds. Zero for instantaneous (tap) notes.
    /// </summary>
    public double Duration => EndTime - StartTime;

    /// <summary>
    /// A copy that shares nothing with this note (every property is a value).
    /// </summary>
    public BeatmapNote Clone() => (BeatmapNote)MemberwiseClone();

    public override string ToString() => $"{Type} @ {StartTime:0}ms ({Lane})";
}
