// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Graphics.Screens;

namespace Yuuki.Game.Tests.Visuals.Screens;

public class TestPlayScreen : TestYuukiScreenStack
{
    private PlayScreen playScreen = null!;

    [Test]
    public void TestAddScreen()
    {
        AddStep("Add screen", () => screenStack.Push(playScreen = new PlayScreen()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddStep("Remove screen", () => playScreen.Exit());
    }
}
