// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;

namespace Yuuki.Game.Graphics.Colors;

public struct YuukiColorPalette
{
    /// <summary>
    /// The light base things sit on: the footer, panels, rows, idle buttons.
    /// </summary>
    public Color Surface;

    /// <summary>
    /// The part's own color, the one that says where you are: the main menu's buttons, the HUD's name tab.
    /// </summary>
    public Color Accent;

    /// <summary>
    /// A light tint of the accent, for highlights drawn on dark fills (a count, an icon, a check mark).
    /// </summary>
    public Color AccentLight;

    /// <summary>
    /// The dark fill for controls and selected states, with white or <see cref="TextLight"/> on it.
    /// </summary>
    public Color Control;

    /// <summary>
    /// The deepest colour: dims and panels behind everything else.
    /// </summary>
    public Color Backdrop;

    /// <summary>
    /// Secondary text on light surfaces: explanations, captions, labels.
    /// </summary>
    public Color TextMuted;

    /// <summary>
    /// Text and lines on dark fills (<see cref="Control"/>, <see cref="Backdrop"/>).
    /// </summary>
    public Color TextLight;

    /// <summary>
    /// Main text and icons on light surfaces.
    /// </summary>
    public Color Text;
}
