// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.iOS;
using Sakura.Framework.Platform;

namespace Yuuki.Game.iOS;

public static class Program
{
    public static void Main(string[] args) => SakuraApplication.Run("yuuki", () => new YuukiGame(), () => new HostOptions { FriendlyAppName = "yuuki." });
}
