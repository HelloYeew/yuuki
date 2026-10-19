// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;

namespace Yuuki.Game.Graphics.Colors;

public class DefaultYuukiColorProvider : IYuukiColorProvider
{
    public static readonly Dictionary<YuukiScreenPart, YuukiColorPalette> PALETTES =
        new Dictionary<YuukiScreenPart, YuukiColorPalette>()
        {
            {
                YuukiScreenPart.MainMenu,
                GetDefaultPalette()
            },
            {
                YuukiScreenPart.Play,
                new YuukiColorPalette()
                {
                    Surface = ColorExtensions.FromHex("#B4B4C4"),
                    Accent = ColorExtensions.FromHex("#C4ACCC"),
                    AccentLight = ColorExtensions.FromHex("#C4ACCC"),
                    Control = ColorExtensions.FromHex("#3B324A"),
                    Backdrop = ColorExtensions.FromHex("#151D30"),
                    TextMuted = ColorExtensions.FromHex("#85828B"),
                    TextLight = ColorExtensions.FromHex("#B4B4C4"),
                    Text = ColorExtensions.FromHex("#393947")
                }
            },
            {
                YuukiScreenPart.Edit,
                new YuukiColorPalette()
                {
                    Surface = ColorExtensions.FromHex("#B4C4C4"),
                    Accent = ColorExtensions.FromHex("#447C94"),
                    AccentLight = ColorExtensions.FromHex("#94C4D0"),
                    Control = ColorExtensions.FromHex("#045384"),
                    Backdrop = ColorExtensions.FromHex("#02324F"),
                    TextMuted = ColorExtensions.FromHex("#63838F"),
                    TextLight = ColorExtensions.FromHex("#ACC1C7"),
                    Text = ColorExtensions.FromHex("#2C4254")
                }
            },
            {
                YuukiScreenPart.List,
                new YuukiColorPalette()
                {
                    Surface = ColorExtensions.FromHex("#B5C4B4"),
                    Accent = ColorExtensions.FromHex("#B4CC6C"),
                    AccentLight = ColorExtensions.FromHex("#DAF494"),
                    Control = ColorExtensions.FromHex("#338404"),
                    Backdrop = ColorExtensions.FromHex("#1F4F02"),
                    TextMuted = ColorExtensions.FromHex("#5C945E"),
                    TextLight = ColorExtensions.FromHex("#A8CC91"),
                    Text = ColorExtensions.FromHex("#464540")
                }
            },
            {
                YuukiScreenPart.More,
                new YuukiColorPalette()
                {
                    Surface = ColorExtensions.FromHex("#C4C3B4"),
                    Accent = ColorExtensions.FromHex("#F0BC3F"),
                    AccentLight = ColorExtensions.FromHex("#FCEC66"),
                    Control = ColorExtensions.FromHex("#84842E"),
                    Backdrop = ColorExtensions.FromHex("#4F4F1C"),
                    TextMuted = ColorExtensions.FromHex("#9F9361"),
                    TextLight = ColorExtensions.FromHex("#D0CEB2"),
                    Text = ColorExtensions.FromHex("#575C48")
                }
            }
        };

    public YuukiColorPalette GetPalette(YuukiScreenPart part)
    {
        return PALETTES.TryGetValue(part, out var palette) ? palette : throw new KeyNotFoundException($"No palette found for screen part: {part}");
    }

    public static YuukiColorPalette GetDefaultPalette() => new YuukiColorPalette()
    {
        Surface = ColorExtensions.FromHex("#E4C4A4"),
        Accent = ColorExtensions.FromHex("#C8645C"),
        AccentLight = ColorExtensions.FromHex("#E4C4A4"),
        Control = ColorExtensions.FromHex("#E4C4A4"),
        Backdrop = ColorExtensions.FromHex("#632621"),
        TextMuted = ColorExtensions.FromHex("#B16961"),
        TextLight = ColorExtensions.FromHex("#DFCFBC"),
        Text = ColorExtensions.FromHex("#523837")
    };

    public static YuukiColorPalette GetDefaultPaletteForPart(YuukiScreenPart part)
    {
        return PALETTES.TryGetValue(part, out var palette) ? palette : GetDefaultPalette();
    }

    public static YuukiColorPalette GetFallbackDefaultPalette() => new YuukiColorPalette()
    {
        Surface = Color.White,
        Accent = Color.White,
        AccentLight = Color.White,
        Control = Color.White,
        Backdrop = Color.White,
        TextMuted = Color.White,
        TextLight = Color.White,
        Text = Color.White
    };
}

public interface IYuukiColorProvider
{
    YuukiColorPalette GetPalette(YuukiScreenPart part);
}

public enum YuukiScreenPart
{
    MainMenu,
    Play,
    Edit,
    List,
    More
}
