// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Graphics.Colors;

public sealed class ScreenPalette
{
    public YuukiScreenPart Part { get; }

    public YuukiColorPalette Palette { get; }

    public ScreenPalette(YuukiScreenPart part, YuukiColorPalette palette)
    {
        Part = part;
        Palette = palette;
    }

    /// <summary>
    /// The palette for something that may be on a screen: the screen's, or (outside any screen, as in an
    /// overlay or a test) <paramref name="fallback"/>'s.
    /// </summary>
    public static YuukiColorPalette Resolve(ScreenPalette? screen, IYuukiColorProvider? provider, YuukiScreenPart fallback = YuukiScreenPart.Play) =>
        screen?.Palette ?? provider?.GetPalette(fallback) ?? DefaultYuukiColorProvider.GetDefaultPaletteForPart(fallback);
}
