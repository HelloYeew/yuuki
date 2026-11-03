// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Graphics.Text;

/// <summary>
/// The game's three typefaces, each with its own job. Japanese in either of the first two comes from
/// <see cref="MPlusRound1C"/> by itself, and Thai from Sarabun.
/// </summary>
public enum Typeface
{
    /// <summary>
    /// Everything that isn't running text: titles and headings, song, artist and difficulty names, the details under them
    /// (source, genre, mapper), labels, buttons, tabs and setting names, short status lines ("Early 292 · Late 408",
    /// "3d ago"), anything in uppercase, and every number (but for the footer's clock).
    /// </summary>
    Quicksand,

    /// <summary>
    /// Normal text, read as prose: sentences, descriptions, hints and instructions, dialog and notification messages,
    /// warnings; and what's typed or picked from a list (a text box, dropdown and menu items, sort options).
    /// </summary>
    Nunito,

    /// <summary>
    /// Japanese, as the fallback for both; not chosen for text of its own.
    /// </summary>
    MPlusRound1C
}
