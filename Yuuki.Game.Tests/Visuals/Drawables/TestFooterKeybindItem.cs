// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Yuuki.Game.Graphics.Drawables;

namespace Yuuki.Game.Tests.Visuals.Drawables;

public partial class TestFooterKeybindItem : YuukiTestScene
{
    private SpriteText pressCountText = null!;
    private int pressCount;

    [Test]
    public void TestFooter()
    {
        AddStep("Add item", () =>
        {
            Add(new FooterKeybindItem("Play", () =>
            {
                pressCount++;
                pressCountText.Text = $"Press count : {pressCount}";
            }, InputKeyExtensions.FromKey(Key.P))
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            });

            Add(pressCountText = new SpriteText()
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = "Press count : 0",
            });
        });
    }
}
