// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Edit.Components;

public readonly record struct EditorColors(
    YuukiColorPalette Palette,
    Color Backdrop,
    Color Panel,
    Color Control,
    Color ControlHover,
    Color Accent,
    Color AccentStrong,
    Color Selection,
    Color SwipeNote,
    Color[] LaneColors)
{
    public static readonly Color TIMING_POINT = ColorExtensions.FromHex("eb4747");
    public static readonly Color PREVIEW_POINT = ColorExtensions.FromHex("66ff73");

    public static EditorColors From(IYuukiColorProvider? provider)
    {
        var edit = provider?.GetPalette(YuukiScreenPart.Edit) ?? DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Edit);
        var play = provider?.GetPalette(YuukiScreenPart.Play) ?? DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

        Color[] cycle = { play.Accent, play.TextLight, play.TextMuted, play.Surface, play.Control, play.TextLight, play.Accent, play.TextMuted };

        return new EditorColors(
            Palette: edit,
            Backdrop: edit.Text.Darken(0.6f),
            Panel: edit.Text.Darken(0.35f),
            Control: edit.Text.Darken(0.1f),
            ControlHover: edit.Text.Lighten(0.15f),
            Accent: edit.AccentLight,
            AccentStrong: edit.Accent,
            Selection: ColorExtensions.FromHex("ffcc22"),
            SwipeNote: play.Control,
            LaneColors: cycle);
    }

    public Color LaneColor(int lane) => LaneColors[(lane % LaneColors.Length + LaneColors.Length) % LaneColors.Length];
}
