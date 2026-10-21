// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Yuuki.Game.Beatmaps;

using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapSetCarousel : Carousel<BeatmapSet, BeatmapSetCarouselCard>
{
    public BeatmapSetCarousel()
    {
        AllowSkim = true;
    }

    // Skimming, rows, tiles and the rail's window compact; the title and artist stay on all of them.
    protected override CarouselLayoutStrategy CreateLayout(CarouselOrientation orientation) => orientation == CarouselOrientation.Vertical
        ? new RailCarouselLayout { SkimRowPitch = 50, SkimWindowHeight = 80 }
        : new StripCarouselLayout { SkimPitch = 112, SkimTileSize = new Vector2(100, 140) };

    protected override CarouselFocusWindow<BeatmapSet> CreateFocusWindow() => new BeatmapSetFocusWindow();
}
