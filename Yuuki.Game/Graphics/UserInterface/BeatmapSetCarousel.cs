// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapSetCarousel : Carousel<BeatmapSet, BeatmapSetCarouselCard>
{
    public BeatmapSetCarousel()
    {
        CardSize = BeatmapSetCarouselCard.CARD_SIZE;
    }
}
