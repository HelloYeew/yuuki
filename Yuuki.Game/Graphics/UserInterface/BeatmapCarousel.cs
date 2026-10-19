// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapCarousel : Carousel<Beatmap, BeatmapCard>
{
    public BeatmapCarousel()
    {
        CardSize = BeatmapCard.CARD_SIZE;
    }
}
