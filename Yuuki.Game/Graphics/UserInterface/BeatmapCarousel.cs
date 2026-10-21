// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapCarousel : Carousel<Beatmap, BeatmapCard>
{
    // Difficulty rows are taller than song rows, with a shorter window: there's a level tab to fit, and
    // no cover or details line.
    protected override CarouselLayoutStrategy CreateLayout(CarouselOrientation orientation) => orientation == CarouselOrientation.Vertical
        ? new RailCarouselLayout { RowPitch = 88, WindowHeight = 124 }
        : base.CreateLayout(orientation);

    protected override CarouselFocusWindow<Beatmap> CreateFocusWindow() => new BeatmapFocusWindow();
}
