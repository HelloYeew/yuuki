// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework;
using Sakura.Framework.Platform;
using Velopack;

namespace Yuuki.Game.Desktop;

public class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        using (AppHost host = new DesktopAppHost("yuuki", new HostOptions()))
        using (App app = new YuukiGame())
            host.Run(app);
    }
}
