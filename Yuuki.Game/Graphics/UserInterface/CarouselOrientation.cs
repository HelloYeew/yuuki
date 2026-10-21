// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The direction a <see cref="Carousel{TModel,TCard}"/> lays its cards out in.
/// </summary>
public enum CarouselOrientation
{
    /// <summary>
    /// The strip: tiles left-to-right along the bottom, behind a hero tile fixed in the middle.
    /// </summary>
    Horizontal,

    /// <summary>
    /// The rail: rows top-to-bottom down the right side, behind a focus window fixed in the middle.
    /// </summary>
    Vertical
}
