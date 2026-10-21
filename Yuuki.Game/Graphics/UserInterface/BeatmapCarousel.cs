// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapCarousel : Carousel<Beatmap, BeatmapCard>
{
    // Difficulty rows are taller than song rows, with a shorter window: there's a level tab to fit, and
    // no cover or details line. On a phone, bigger again.
    protected override CarouselLayoutStrategy CreateLayout(CarouselOrientation orientation)
    {
        if (orientation == CarouselOrientation.Vertical)
        {
            return IsPhone
                ? new RailCarouselLayout { RowPitch = 100, WindowHeight = 140 }
                : new RailCarouselLayout { RowPitch = 88, WindowHeight = 124 };
        }

        return IsPhone ? BeatmapSetCarousel.PhoneStrip() : base.CreateLayout(orientation);
    }

    protected override CarouselFocusWindow<Beatmap> CreateFocusWindow() => new BeatmapFocusWindow();
}
