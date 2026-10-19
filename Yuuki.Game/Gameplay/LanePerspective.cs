// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// The pseudo-3D projection for the note highway. Lanes fan out from a vanishing point near the top
/// (the horizon) down to a wide judgement line at the player's edge. A note's depth <c>z</c> runs
/// from 1 (just spawned, far/small/bunched near the centre) to 0 (at the judgement line,
/// near/large/spread), and continues negative as it flies past.
/// <para>
/// The perspective factor is <c>1 / (1 + z·k)</c>, tuned so it equals <see cref="PerspectiveFar"/>
/// at z = 1 and 1 at z = 0. Screen X scales with it (spread), and screen Y maps the same factor
/// between the horizon and the player line so notes accelerate toward the player.
/// </para>
/// </summary>
public sealed class LanePerspective
{
    /// <summary>
    /// Horizontal centre of the highway (the vanishing point's X), in content coordinates.
    /// </summary>
    public float CentreX { get; set; }

    /// <summary>
    /// Y of the vanishing point / far edge.
    /// </summary>
    public float HorizonY { get; set; }

    /// <summary>
    /// Y of the judgement line / player edge (notes are hit here, at z = 0).
    /// </summary>
    public float PlayerY { get; set; }

    /// <summary>
    /// Half the width the lanes occupy at the player edge.
    /// </summary>
    public float BottomHalfWidth { get; set; }

    /// <summary>
    /// Perspective factor at the far edge (z = 1) — i.e. the top edge width as a fraction of the
    /// bottom. Higher = a wider; lower = a deeper highway that
    /// converges toward a point.
    /// </summary>
    public float PerspectiveFar { get; set; } = 0.4f;

    /// <summary>Number of lanes the highway is divided into.</summary>
    public int LaneCount { get; set; } = 4;

    /// <summary>
    /// Maximum perspective factor once a note passes the player edge (z &lt; 0), bounding how big it
    /// gets while flying by. By then it's below the field and faded out (see <see cref="DrawableNote"/>),
    /// so this only keeps the numbers finite.
    /// </summary>
    public float PerspectiveMax { get; set; } = 2f;

    /// <summary>
    /// Lays the highway out in a <paramref name="width"/> × <paramref name="height"/> playfield: centred,
    /// spanning nearly its full width at the player edge. The vanishing point is high and tight, so
    /// the lanes read as receding "endlessly" toward a far point of view; notes fade in there (see
    /// <see cref="DrawableNote"/>) rather than popping at a hard edge.
    /// </summary>
    public void LayOut(float width, float height)
    {
        CentreX = width / 2;
        HorizonY = height * 0.06f;
        PlayerY = height * 0.9f;
        BottomHalfWidth = width * 0.47f;
        PerspectiveFar = 0.14f;
    }

    private float k => 1f / PerspectiveFar - 1f;

    private float laneSpacing => 2 * BottomHalfWidth / LaneCount;

    /// <summary>
    /// The world X offset (at the player edge) of lane <paramref name="index"/>'s centre.
    /// </summary>
    public float LaneOffset(int index) => -BottomHalfWidth + laneSpacing * (index + 0.5f);

    /// <summary>
    /// The world X offset of the separator between lanes (0..LaneCount).
    /// </summary>
    public float EdgeOffset(int edge) => -BottomHalfWidth + laneSpacing * edge;

    /// <summary>
    /// The perspective factor at depth <paramref name="z"/> (<see cref="PerspectiveFar"/> far -> 1.0 at the player edge).
    /// </summary>
    public float Perspective(double z)
    {
        // Past the player edge the hyperbola races to a pole at z = -1/k and flips negative beyond
        // it, so it continues along its tangent there instead: notes carry on past the line at the
        // speed they reached it, with nothing to blow up. (A clamp on the factor alone stopped them
        // dead just below the line, where a smaller playfield left them on screen.)
        double factor = z >= 0 ? 1.0 / (1.0 + z * k) : 1.0 - z * k;
        return (float)Math.Min(factor, PerspectiveMax);
    }

    /// <summary>
    /// Projects a depth and lane offset to a screen position in content coordinates.
    /// </summary>
    public Vector2 Project(double z, float laneOffset)
    {
        float persp = Perspective(z);
        float yNorm = (persp - PerspectiveFar) / (1 - PerspectiveFar);
        float y = HorizonY + (PlayerY - HorizonY) * yNorm;
        float x = CentreX + laneOffset * persp;
        return new Vector2(x, y);
    }
}
