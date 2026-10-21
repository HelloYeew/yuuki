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

    // Skimming, rows, tiles and the rail's window compact; the title and artist stay on all of them. On a
    // phone the rows are only a little taller than the desktop's, so the short canvas still shows a couple
    // of songs either side of the window; the tiles are bigger, for thumbs.
    protected override CarouselLayoutStrategy CreateLayout(CarouselOrientation orientation)
    {
        if (orientation == CarouselOrientation.Vertical)
        {
            return IsPhone
                ? new RailCarouselLayout { RowPitch = 68, SkimRowPitch = 52, WindowHeight = 136, SkimWindowHeight = 88 }
                : new RailCarouselLayout { SkimRowPitch = 50, SkimWindowHeight = 80 };
        }

        return IsPhone
            ? PhoneStrip(skimPitch: 132, skimTileSize: new Vector2(116, 156))
            : new StripCarouselLayout { SkimPitch = 112, SkimTileSize = new Vector2(100, 140) };
    }

    /// <summary>
    /// The strip on a phone: bigger tiles at a wider pitch, and a hero a little smaller than the desktop's
    /// so the strip still leaves room above it on a 720-tall canvas.
    /// </summary>
    public static StripCarouselLayout PhoneStrip(float? skimPitch = null, Vector2? skimTileSize = null) => new StripCarouselLayout
    {
        Pitch = 168,
        TileSize = new Vector2(148, 190),
        TileTop = 104,
        WindowSize = new Vector2(228, 276),
        SkimPitch = skimPitch,
        SkimTileSize = skimTileSize
    };

    protected override CarouselFocusWindow<BeatmapSet> CreateFocusWindow() => new BeatmapSetFocusWindow();
}
