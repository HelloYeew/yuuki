// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// The music player's seek bar: a finger dragging it sees the time it's on, which the tooltip shows only a mouse.
/// </summary>
public partial class TestMusicProgressBar : YuukiManualInputManagerTestScene
{
    private MusicProgressBar bar = null!;
    private float seekedTo = -1;

    [SetUp]
    public void SetUpBar()
    {
        AddStep("a three-minute song's bar", () =>
        {
            seekedTo = -1;
            TestContent.Add(new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(400, 40),
                Child = bar = new MusicProgressBar(Color.White)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    DisplayDurationMs = 180_000,
                    Seeked = fraction => seekedTo = fraction
                }
            });
        });
        AddUntilStep("loaded", () => bar.IsLoaded);
    }

    [Test]
    public void TestAFingerSeesTheTime()
    {
        AddStep("a finger on it, dragged to the middle", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, at(0.1f));
            InputManager.MoveTouchTo(TouchSource.Touch1, at(0.5f));
        });
        AddAssert("the time it's on shows", () => bar.TouchTimeShown == "1:30 / 3:00");
        AddAssert("and it seeks there", () => near(seekedTo, 0.5f));
        AddStep("lift it", () => InputManager.TouchUp(TouchSource.Touch1));
        AddAssert("the time goes", () => bar.TouchTimeShown == string.Empty);
    }

    [Test]
    public void TestTheMouseHasItsTooltip()
    {
        AddStep("drag with the mouse", () =>
        {
            InputManager.MoveMouseTo(at(0.1f));
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(at(0.5f));
        });
        AddAssert("no time over it", () => bar.TouchTimeShown == string.Empty);
        AddStep("let go", () => InputManager.ReleaseButton(MouseButton.Left));
    }

    private Vector2 at(float fraction) => bar.ToScreenSpace(new Vector2(bar.DrawWidth * fraction, bar.DrawHeight / 2));

    private static bool near(float value, float expected) => System.Math.Abs(value - expected) < 0.01f;
}
