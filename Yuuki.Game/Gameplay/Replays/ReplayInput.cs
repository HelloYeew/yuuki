// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text.Json.Serialization;

namespace Yuuki.Game.Gameplay.Replays;

/// <summary>
/// What happened at one moment of a recorded run.
/// </summary>
public enum ReplayInputKind
{
    LanePress,
    LaneRelease,
    SwipeLeft,
    SwipeRight,

    /// <summary>
    /// Input stopped counting (the pause menu, or the run ending): presses only mark lanes held.
    /// </summary>
    Pause,

    /// <summary>
    /// Input counts again; holds let go of while paused are released now.
    /// </summary>
    Resume,

    // By touch, each with its finger: a hold a finger takes is its own, so which one matters. Last, so the
    // kinds before them keep their numbers in replays already saved.

    /// <summary>
    /// A finger touched down in the lane: a press (see <see cref="Playfield.TouchDown"/>).
    /// </summary>
    TouchDown,

    /// <summary>
    /// A finger slid into the lane: held, but nothing pressed (see <see cref="Playfield.TouchSlide"/>).
    /// </summary>
    TouchSlide,

    /// <summary>
    /// A finger lifted, from the lane it was in (see <see cref="Playfield.TouchUp"/>).
    /// </summary>
    TouchUp
}

/// <summary>
/// One recorded input, at the gameplay time the playfield judged it at, and the finger for one by touch.
/// Short property names keep a replay of a few thousand inputs small on disk.
/// </summary>
public readonly record struct ReplayInput(
    [property: JsonPropertyName("t")] double Time,
    [property: JsonPropertyName("k")] ReplayInputKind Kind,
    [property: JsonPropertyName("l")] int Lane = -1,
    [property: JsonPropertyName("f"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Finger = null);
