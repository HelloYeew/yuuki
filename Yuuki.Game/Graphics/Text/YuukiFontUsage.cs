// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Text;

namespace Yuuki.Game.Graphics.Text;

public static class YuukiFontUsage
{
    public static FontUsage GetFontUsage(Typeface typeface = Typeface.Nunito, int size = 16,
        FontWeight fontWeight = FontWeight.Regular, bool italics = false) =>
        new FontUsage(GetTypeFontString(typeface), size, fontWeight.ToString(), italics);

    public static string GetTypeFontString(Typeface typeface)
    {
        return typeface switch
        {
            Typeface.Quicksand => "Quicksand",
            Typeface.Nunito => "Nunito",
            Typeface.MPlusRound1C => "MPLUSRounded1c",
            _ => ""
        };
    }
}
