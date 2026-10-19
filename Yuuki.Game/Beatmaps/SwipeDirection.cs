// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// Which swipe input a <see cref="NoteType.Swipe"/> note accepts
/// </summary>
public enum SwipeDirection
{
    /// <summary>
    /// Either swipe hits it. The default, so charts that predate directions stay playable.
    /// </summary>
    Any,

    /// <summary>
    /// Only the left swipe hits it.
    /// </summary>
    Left,

    /// <summary>
    /// Only the right swipe hits it.
    /// </summary>
    Right
}
