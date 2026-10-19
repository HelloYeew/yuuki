// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests;

public abstract partial class YuukiTestScene : TestScene
{
    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }
}
