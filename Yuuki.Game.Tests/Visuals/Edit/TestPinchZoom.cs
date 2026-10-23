// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Edit.Components;

namespace Yuuki.Game.Tests.Visuals.Edit;

/// <summary>
/// Two fingers zooming the editor's time: spread twice as far and the time on screen halves.
/// </summary>
public partial class TestPinchZoom : YuukiManualInputManagerTestScene
{
    private readonly Reactive<double> span = new Reactive<double>(2000);

    [SetUp]
    public void SetUpPinch()
    {
        AddStep("a pinch over the editor", () =>
        {
            span.Value = 2000;
            TestContent.Add(new PinchZoom(span, 500, 10_000));
        });
    }

    [Test]
    public void TestSpreadingZoomsInAndPinchingZoomsOut()
    {
        AddStep("two fingers 100 apart", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, new Vector2(300, 300));
            InputManager.TouchDown(TouchSource.Touch2, new Vector2(400, 300));
        });
        AddWaitStep("a frame", 50);
        AddStep("spread to 200", () => InputManager.MoveTouchTo(TouchSource.Touch2, new Vector2(500, 300)));
        AddUntilStep("half the time", () => span.Value == 1000);
        AddStep("pinch to 40", () => InputManager.MoveTouchTo(TouchSource.Touch2, new Vector2(340, 300)));
        AddUntilStep("two and a half times it", () => span.Value == 5000);
        AddStep("lift them", () =>
        {
            InputManager.TouchUp(TouchSource.Touch2);
            InputManager.TouchUp(TouchSource.Touch1);
        });
        AddWaitStep("a moment", 50);
        AddAssert("it stays", () => span.Value == 5000);
    }

    [Test]
    public void TestOneFingerDoesNotZoom()
    {
        AddStep("one finger, dragged", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, new Vector2(300, 300));
            InputManager.MoveTouchTo(TouchSource.Touch1, new Vector2(500, 300));
        });
        AddWaitStep("a moment", 50);
        AddAssert("as it was", () => span.Value == 2000);
        AddStep("lift it", () => InputManager.TouchUp(TouchSource.Touch1));
    }

    [Test]
    public void TestItStopsAtItsLimits()
    {
        AddStep("two fingers 100 apart", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, new Vector2(300, 300));
            InputManager.TouchDown(TouchSource.Touch2, new Vector2(400, 300));
        });
        AddWaitStep("a frame", 50);
        AddStep("spread far apart", () => InputManager.MoveTouchTo(TouchSource.Touch2, new Vector2(1300, 300)));
        AddUntilStep("no less than the least", () => span.Value == 500);
        AddStep("lift them", () =>
        {
            InputManager.TouchUp(TouchSource.Touch2);
            InputManager.TouchUp(TouchSource.Touch1);
        });
    }
}
