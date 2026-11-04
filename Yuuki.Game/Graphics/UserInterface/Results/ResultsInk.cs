// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// The results screen's ink on its panels. The palette's muted text is too faint on them: on the play palette's surface
/// it's 1.85:1, so a card's title sank into it. Titles take the text itself (5.55:1); what goes with them, the text at
/// 90% (4.6:1, enough for small text).
/// </summary>
public static class ResultsInk
{
    /// <summary>
    /// A title or a label: what a card or a figure is.
    /// </summary>
    public static Color Label(YuukiColorPalette palette) => palette.Text;

    /// <summary>
    /// What goes with a title: a card's question, a line under a figure, an axis.
    /// </summary>
    public static Color Secondary(YuukiColorPalette palette) => palette.Text.WithAlpha(0.9f);
}
