// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A clock that shows the time of day, redrawn whenever the second changes.
/// </summary>
public abstract partial class ClockDisplay : Container
{
    private int? lastSecond;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Shown at the time straight away, not animating to it from midnight.
        UpdateDisplay(DateTimeOffset.Now);
        FinishTransforms(true);
    }

    public override void Update()
    {
        base.Update();

        var now = DateTimeOffset.Now;

        if (now.Second != lastSecond)
        {
            lastSecond = now.Second;
            UpdateDisplay(now);
        }
    }

    protected abstract void UpdateDisplay(DateTimeOffset now);
}
