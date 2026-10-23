// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Yuuki.Game.Edit.Components;

/// <summary>
/// Two fingers spread apart or pinched together zoom the editor's time in or out, as the zoom buttons and Alt+scroll
/// do: spread twice as far, and the time on screen halves.
/// </summary>
/// <remarks>
/// It only watches the fingers, so one on its own still places, picks and drags notes, and two together click
/// nothing (the framework makes a gesture of several fingers no click).
/// </remarks>
public partial class PinchZoom : Container
{
    private readonly Reactive<double> span;
    private readonly double min;
    private readonly double max;

    // How far apart the fingers were, and the span, when the pinch began; NaN while there's no pinch.
    private float startDistance = float.NaN;
    private double startSpan;

    /// <param name="span">The time on screen, which a pinch zooms.</param>
    /// <param name="min">The least it goes to.</param>
    /// <param name="max">The most it goes to.</param>
    public PinchZoom(Reactive<double> span, double min, double max)
    {
        this.span = span;
        this.min = min;
        this.max = max;
    }

    public override bool HandlePositionalInput => false;

    public override void Update()
    {
        base.Update();

        InputState? state = GetContainingInputManager()?.CurrentState;

        if (state == null || state.ActiveTouchCount != 2 || !fingers(state, out Vector2 a, out Vector2 b))
        {
            startDistance = float.NaN;
            return;
        }

        float distance = Vector2.Distance(a, b);

        if (float.IsNaN(startDistance))
        {
            startDistance = distance;
            startSpan = span.Value;
            return;
        }

        if (distance > 0)
            span.Value = Math.Clamp(startSpan * startDistance / distance, min, max);
    }

    // The two fingers that are down, whichever they are.
    private static bool fingers(InputState state, out Vector2 a, out Vector2 b)
    {
        a = b = Vector2.Zero;
        int found = 0;

        for (int i = 0; i < InputState.MAX_TOUCHES && found < 2; i++)
        {
            var source = (TouchSource)i;
            if (!state.IsTouchActive(source))
                continue;

            if (found++ == 0)
                a = state.GetTouchPosition(source);
            else
                b = state.GetTouchPosition(source);
        }

        return found == 2;
    }
}
