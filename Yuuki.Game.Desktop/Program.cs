// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Sakura.Framework.Platform;
using Velopack;

namespace Yuuki.Game.Desktop;

public class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var velopack = VelopackApp.Build();

        if (OperatingSystem.IsWindows())
            registerFileTypes(velopack);

        velopack.Run();

        // Files the game was opened with: an .osz opened from the file manager.
        string[] files = args.Where(File.Exists).Select(Path.GetFullPath).ToArray();

        using (var instance = SingleInstance.Claim())
        {
            // Already running: that copy takes them.
            if (instance == null)
            {
                SingleInstance.HandOver(files);
                return;
            }

            using (AppHost host = new DesktopAppHost("yuuki", new HostOptions { FriendlyAppName = "yuuki." }))
            using (YuukiGame game = new YuukiGame())
            {
                if (files.Length > 0)
                    game.ImportShared(files);

                instance.Listen(game.ImportShared);
                host.Run(game);
            }
        }
    }

    // As the game's installed, updated and uninstalled, which the installer runs it for and it then leaves.
    [SupportedOSPlatform("windows")]
    private static void registerFileTypes(VelopackApp velopack)
    {
        velopack.OnAfterInstallFastCallback(_ => WindowsFileAssociations.Register())
                .OnAfterUpdateFastCallback(_ => WindowsFileAssociations.Register())
                .OnBeforeUninstallFastCallback(_ => WindowsFileAssociations.Unregister());
    }
}
