// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Cursor;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestYuukiCursor : YuukiManualInputManagerTestScene
{
    private YuukiCursorContainer cursor = null!;

    [SetUp]
    public void SetUp() => AddStep("add cursor", () =>
    {
        TestContent.Clear();
        TestContent.Add(cursor = new YuukiCursorContainer { HideWhenOutsideWindow = false });
    });

    [Test]
    public void TestPressDragRelease()
    {
        AddStep("move to the middle", () => InputManager.MoveMouseTo(cursor));
        AddStep("press", () => InputManager.PressButton(MouseButton.Left));
        AddUntilStep("held", () => cursor.IsHeld);
        AddStep("drag well away", () =>
        {
            var area = cursor.DrawRectangle;
            InputManager.MoveMouseTo(new Vector2(area.X + area.Width / 2, area.Y + area.Height / 2 + 300));
        });
        AddUntilStep("turning", () => cursor.CursorRotation != 0);
        AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
        AddUntilStep("let go", () => !cursor.IsHeld);
    }

    [Test]
    public void TestReleaseSpringsBack()
    {
        AddStep("move to the middle", () => InputManager.MoveMouseTo(cursor));
        AddStep("press", () => InputManager.PressButton(MouseButton.Left));
        AddUntilStep("squashed", () => cursor.CursorScale < 0.97f);
        AddStep("drag well away", () =>
        {
            var area = cursor.DrawRectangle;
            InputManager.MoveMouseTo(new Vector2(area.X + area.Width / 2, area.Y + area.Height / 2 + 300));
        });
        AddUntilStep("turned", () => cursor.CursorRotation > 10);
        AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));

        // Elastic returns overshoot before settling. Folded into the press's still-running animations
        // (the framework's in-flight retarget), they snapped straight back instead.
        AddUntilStep("springs past full size", () => cursor.CursorScale > 1.001f);
        AddUntilStep("swings past upright", () => cursor.CursorRotation < 0);
        AddUntilStep("settles", () => Math.Abs(cursor.CursorScale - 1) < 0.001f && Math.Abs(cursor.CursorRotation) < 0.5f);
    }

    [Test]
    public void TestHideAndShow()
    {
        AddStep("hide", () => cursor.Hidden.Value = true);
        AddStep("press while hidden", () => InputManager.PressButton(MouseButton.Left));
        AddAssert("ignored while hidden", () => !cursor.IsHeld);
        AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
        AddStep("show", () => cursor.Hidden.Value = false);
    }
}
