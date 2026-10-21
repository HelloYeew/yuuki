// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// How a carousel is moving this frame, shared with its cards and focus window so they can follow it:
/// compacting while it skims, and (through the layout) the cue leading the way.
/// </summary>
public class CarouselMotion
{
    /// <summary>
    /// How far into skim mode the list is: 0 at rest, 1 compacted. A list goes into skim mode when it
    /// moves faster than about seven items a second, and eases back out a moment after it slows.
    /// </summary>
    public float Skim { get; internal set; }

    /// <summary>
    /// The look-ahead, -1 to 1: positive while moving towards later items. The cue moves back by it, so
    /// more of what's coming shows; it settles to 0 when the list stops.
    /// </summary>
    public float Lead { get; internal set; }
}
