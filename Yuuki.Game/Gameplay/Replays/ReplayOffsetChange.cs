// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text.Json.Serialization;

namespace Yuuki.Game.Gameplay.Replays;

/// <summary>
/// The offsets a recorded run went on with from a moment on.
/// </summary>
/// <remarks>
/// At the song's own time (the gameplay clock's, without its offset), which the change doesn't move: at the clock's,
/// a change taking the clock back past where it was made would undo itself on playback.
/// </remarks>
public readonly record struct ReplayOffsetChange(
    [property: JsonPropertyName("t")] double SongTime,
    [property: JsonPropertyName("a")] double AudioOffset,
    [property: JsonPropertyName("s")] double SongOffset);
