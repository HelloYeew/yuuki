// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Yuuki.Game.Utilities;

public static class StringUtility
{
    private const string replace_char = "_";

    /// <summary>
    /// Clean a folder or file name of characters that are invalid on the current platform.
    /// </summary>
    public static string CleanFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return fileName;

        foreach (char character in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(character.ToString(), replace_char);

        return fileName;
    }
}
