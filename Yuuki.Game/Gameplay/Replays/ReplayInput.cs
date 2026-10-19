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
    Resume
}

/// <summary>
/// One recorded input, at the gameplay time the playfield judged it at. Short property names keep a
/// replay of a few thousand inputs small on disk.
/// </summary>
public readonly record struct ReplayInput(
    [property: JsonPropertyName("t")] double Time,
    [property: JsonPropertyName("k")] ReplayInputKind Kind,
    [property: JsonPropertyName("l")] int Lane = -1);
