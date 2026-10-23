// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Yuuki.Game.Database;

/// <summary>
/// Files the system hands over as links rather than paths: an .osz a phone's file manager or browser opens
/// with the game, or one picked in its file dialog (a content:// link on Android). The platform's head
/// provides this; a platform whose files all have paths has none.
/// </summary>
public interface IExternalFiles
{
    /// <summary>
    /// Whether <paramref name="path"/> is one of these links, rather than a path on disk.
    /// </summary>
    bool IsLink(string path);

    /// <summary>
    /// The file's name, with its extension, as the app it came from gives it.
    /// </summary>
    string NameOf(string link);

    /// <summary>
    /// Opens the file for reading. Called off the update thread.
    /// </summary>
    Stream Open(string link);
}
