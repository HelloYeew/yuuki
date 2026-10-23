// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Yuuki.Game.Desktop;

/// <summary>
/// Puts the game in Windows' "Open with" for .osz files, as it's installed (and takes it out as it's uninstalled),
/// and makes it what opens them where nothing else does: an osu! that's installed keeps them, and the player can pick.
/// </summary>
/// <remarks>
/// Under HKEY_CURRENT_USER, as the game itself is installed for the one user. A file opened this way starts the game
/// with its path, which <see cref="SingleInstance"/> hands to the game if it's already running.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsFileAssociations
{
    private const string classes_key = @"Software\Classes";
    private const string extension = ".osz";
    private const string program_id = "dev.helloyeew.yuuki.BeatmapArchive";

    // What Explorer calls the file: the same as osu!, whose format it is.
    private const string description = "osu! beatmap archive";

    private const int shcne_assocchanged = 0x08000000;
    private const uint shcnf_idlist = 0;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    /// <summary>
    /// Registers the game, at where it's running from now: on an install or an update, which keep it in the same place.
    /// </summary>
    public static void Register()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("The game's own path isn't known.");

        using (var classes = Registry.CurrentUser.CreateSubKey(classes_key))
        {
            using (var program = classes.CreateSubKey(program_id))
            {
                program.SetValue(null, description);

                using (var icon = program.CreateSubKey("DefaultIcon"))
                    icon.SetValue(null, $"\"{exe}\",0");

                using (var command = program.CreateSubKey(@"shell\open\command"))
                    command.SetValue(null, $"\"{exe}\" \"%1\"");
            }

            using (var type = classes.CreateSubKey(extension))
            {
                // Read from the merged view, so an osu! registered for every user counts too.
                if (Registry.ClassesRoot.OpenSubKey(extension)?.GetValue(null) is not string { Length: > 0 } current || current == program_id)
                    type.SetValue(null, program_id);

                using (var openWith = type.CreateSubKey("OpenWithProgids"))
                    openWith.SetValue(program_id, string.Empty);
            }
        }

        SHChangeNotify(shcne_assocchanged, shcnf_idlist, 0, 0);
    }

    /// <summary>
    /// Takes the game out again, leaving whatever else opens .osz files as it is.
    /// </summary>
    public static void Unregister()
    {
        using (var classes = Registry.CurrentUser.OpenSubKey(classes_key, true))
        {
            if (classes == null)
                return;

            using (var type = classes.OpenSubKey(extension, true))
            {
                if (type?.GetValue(null) is string current && current == program_id)
                    type.DeleteValue(string.Empty, false);

                using (var openWith = type?.OpenSubKey("OpenWithProgids", true))
                    openWith?.DeleteValue(program_id, false);
            }

            classes.DeleteSubKeyTree(program_id, false);
        }

        SHChangeNotify(shcne_assocchanged, shcnf_idlist, 0, 0);
    }
}
