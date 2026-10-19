// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The direction a <see cref="Carousel{TModel,TCard}"/> lays its cards out in.
/// </summary>
public enum CarouselOrientation
{
    /// <summary>
    /// Square cards laid out left-to-right; the centred card pops up and scales (coverflow).
    /// </summary>
    Horizontal,

    /// <summary>
    /// Wide bars stacked top-to-bottom; the centred bar shifts out sideways.
    /// </summary>
    Vertical
}
