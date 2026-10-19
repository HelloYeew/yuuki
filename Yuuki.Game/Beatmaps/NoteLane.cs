// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// A single lane on the playfield. The prototype uses four lanes (<see cref="Lane1"/>–<see cref="Lane4"/>)
/// mapped to D F J K; the remaining lanes are reserved for the dense 7-lane layout (S D F Space J K L).
/// </summary>
public enum NoteLane
{
    Lane1,
    Lane2,
    Lane3,
    Lane4,
    Lane5,
    Lane6,
    Lane7,
    Lane8
}
