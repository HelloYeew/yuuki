// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Moves other drawables' colors through the heroes' (<see cref="HeroColors.Cycle"/>), every frame. Use in Full Bloom's
/// ring, lights and flowers do once everything has bloomed. Each target can start further round the cycle, so the colors
/// run across things side by side instead of changing all at once.
/// </summary>
/// <remarks>
/// While it runs it owns its targets' colors; a color set on one by anything else is overwritten next frame. Remove a
/// target (or this) to give its color back.
/// </remarks>
public partial class HeroColorCycle : Component
{
    private readonly List<(object Key, Action<Color> Apply, float Hue)> targets = new List<(object, Action<Color>, float)>();

    /// <summary>
    /// How much white the colors take, 0–1.
    /// </summary>
    public float White { get; set; } = HeroColors.CYCLE_WHITE;

    /// <summary>
    /// How fast the colors move, as a multiple of <see cref="HeroColors.CYCLE_PER_SECOND"/>.
    /// </summary>
    public double Speed { get; set; } = 1;

    /// <summary>
    /// When the colors started moving, on this cycle's clock; when it's first updated unless set.
    /// </summary>
    public double StartTime { get; set; } = double.NaN;

    /// <summary>
    /// Whether the colors move; while not, the targets keep whatever color they have.
    /// </summary>
    public bool Running { get; set; } = true;

    public HeroColorCycle(params Drawable[] targets)
    {
        foreach (Drawable target in targets)
            Add(target);
    }

    /// <summary>
    /// Starts moving <paramref name="target"/>'s color, <paramref name="hue"/> degrees round the cycle (60 is one hero on).
    /// </summary>
    public void Add(Drawable target, float hue = 0) => targets.Add((target, c => target.Color = c, hue));

    /// <summary>
    /// Starts handing the moving color to <paramref name="apply"/>, for a color that isn't a drawable's own (an
    /// <see cref="EdgeGlow"/>'s). Remove it by the same delegate.
    /// </summary>
    public void Add(Action<Color> apply, float hue = 0) => targets.Add((apply, apply, hue));

    public void Remove(Drawable target) => targets.RemoveAll(t => t.Key == target);

    public void Remove(Action<Color> apply) => targets.RemoveAll(t => t.Key == (object)apply);

    /// <summary>
    /// The color the cycle gives right now at <paramref name="hue"/>, for things drawn with colors of their own (a petal,
    /// a vertex).
    /// </summary>
    public Color ColorAt(float hue = 0) =>
        HeroColors.Cycle(double.IsNaN(StartTime) ? 0 : Clock.CurrentTime - StartTime, hue, White, Speed);

    public override void Update()
    {
        base.Update();

        if (double.IsNaN(StartTime))
            StartTime = Clock.CurrentTime;

        if (!Running)
            return;

        foreach ((_, Action<Color> apply, float hue) in targets)
            apply(ColorAt(hue));
    }
}
