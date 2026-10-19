// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework;
using Sakura.Framework.Platform;

namespace Yuuki.Game.Tests;

public class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using (AppHost host = new DesktopAppHost("yuuki-tests"))
        using (App app = new YuukiTestApp(typeof(Program).Assembly))
            host.Run(app);
    }
}
