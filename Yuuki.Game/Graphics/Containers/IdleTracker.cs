// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Yuuki.Game.Graphics.Containers;

/// <summary>
/// Notices when the player has gone quiet: no mouse movement, press, scroll,
/// key or gamepad button for <see cref="Timeout"/>, and <see cref="IsIdle"/> turns on.
/// </summary>
public partial class IdleTracker : Container
{
    /// <summary>
    /// How long without input before going idle, in ms.
    /// </summary>
    public double Timeout { get; set; } = 10_000;

    /// <summary>
    /// Whether the player has been away for <see cref="Timeout"/>.
    /// </summary>
    public ReactiveBool IsIdle { get; } = new ReactiveBool(false);

    /// <summary>
    /// Whether going idle is allowed right now (e.g. only while its screen is current and no overlay
    /// is open). While it says no, the tracker stays awake and the countdown keeps restarting.
    /// </summary>
    public Func<bool>? CanIdle { get; set; }

    private InputManager? inputManager;
    private Vector2? lastMousePosition;
    private double lastActivity;

    public IdleTracker()
    {
        RelativeSizeAxes = Axes.Both;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        inputManager = GetContainingInputManager();
        lastActivity = Clock.CurrentTime;
    }

    /// <summary>
    /// Wakes up (if idle) and starts the countdown over.
    /// </summary>
    public void Wake()
    {
        lastActivity = Clock.CurrentTime;
        IsIdle.Value = false;
    }

    public override void Update()
    {
        base.Update();

        if (sawActivity() || CanIdle?.Invoke() == false)
        {
            Wake();
            return;
        }

        if (!IsIdle.Value && Clock.CurrentTime - lastActivity >= Timeout)
            IsIdle.Value = true;
    }

    private bool sawActivity()
    {
        InputState? state = inputManager?.CurrentState;
        if (state == null)
            return false;

        bool moved = lastMousePosition is { } last && last != state.MousePosition;
        lastMousePosition = state.MousePosition;

        // Held input counts the whole time it's held: nobody holding a key is away.
        return moved
               || state.PressedKeys.Count > 0
               || state.PressedMouseButtons.Count > 0
               || state.Gamepads.Values.Any(g => g.PressedButtons.Count > 0);
    }

    /// <summary>
    /// Wakes on a press; while idle the press only wakes, nothing behind sees it.
    /// </summary>
    private bool wakeConsuming()
    {
        bool wasIdle = IsIdle.Value;
        Wake();
        return wasIdle;
    }

    public override bool OnMouseDown(MouseButtonEvent e) => wakeConsuming();

    public override bool OnScroll(ScrollEvent e) => wakeConsuming();

    public override bool OnKeyDown(KeyEvent e) => wakeConsuming();

    public override bool OnGamepadButtonDown(GamepadButtonEvent e) => wakeConsuming();
}
