// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Testing;
using Yuuki.Game.Graphics.ScreenStacks;

namespace Yuuki.Game.Tests.Visuals;

public abstract partial class TestYuukiScreenStack : TestScene
{
    public YuukiScreenStack screenStack = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        screenStack = new YuukiScreenStack()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,

            // One stack is built for the whole fixture and cached for the tests to resolve, but each
            // test's SetUp re-adds it after the scene has been cleared. Its lifetime belongs to the
            // fixture, not to whichever parent currently holds it, so removal must not dispose it.
            DisposeOnRemoval = false
        };
        Cache(screenStack);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("Add screen stack", () => Add(screenStack));
    }
}
