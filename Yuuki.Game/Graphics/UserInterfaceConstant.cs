// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics;

public static class UserInterfaceConstant
{
    /// <summary>
    /// The game's lean (about 33°), for short bars and buttons: the info panel's rows, BACK / NEXT.
    /// A sheared box leans by its height times this, so on anything taller (or stacked) it gets heavy.
    /// </summary>
    public static Vector2 Shear => new Vector2(-0.66f, 0);

    /// <summary>
    /// More gentle of <see cref="Shear"/>, use in some element that normal look too heavy
    /// like song selection search
    /// </summary>
    public static Vector2 GentleShear => new Vector2(-0.2f, 0);

    /// <summary>
    /// A gentle settle for slide-ins: overshoots by about 3% of the travel, against
    /// <see cref="Easing.OutBack"/>'s 10%, which is bouncy enough to pull an edge-anchored sheared
    /// panel far enough to reveal its slanted end.
    /// </summary>
    public static EasingFunction SoftBounce { get; } = new EasingFunction(new CubicBezierEasing(0.25, 1.3, 0.5, 1));

    /// <summary>The red of an action that can't be taken back, like quitting the game.</summary>
    public static Color DangerColor { get; } = ColorExtensions.FromHex("#D9534F");
}
