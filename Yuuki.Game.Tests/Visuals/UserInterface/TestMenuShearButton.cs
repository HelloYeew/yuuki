// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public class TestMenuShearButton : YuukiManualInputManagerTestScene
{
    private MenuShearButton button = null!;

    [SetUp]
    public void Setup()
    {
        AddStep("add button", () =>
        {
            TestContent.Add(button = new MenuShearButton()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                // Without an action it would be a switched-off button, which doesn't flash.
                Action = () => { }
            });
        });
    }

    [Test]
    public void TestButton()
    {

    }

    [Test]
    public void TestHoverAndClick()
    {
        AddStep("move mouse to button", () => InputManager.MoveMouseTo(button));
        AddStep("click", () => InputManager.Click(MouseButton.Left));
    }
}
