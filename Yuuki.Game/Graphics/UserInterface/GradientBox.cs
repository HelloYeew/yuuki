// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A <see cref="Box"/> filled with a Color gradient, left-to-right (or top-to-bottom when
/// <see cref="Vertical"/>).
/// <para>
/// Written before the framework had a gradient-Color API: it assigns <see cref="StartColor"/> to the
/// two left corners and <see cref="EndColor"/> to the two right corners and lets the renderer
/// interpolate across the quad. <see cref="Drawable.ColorInfo"/> now does the same for any drawable,
/// with the same sRGB-to-linear conversion and per-corner alpha, so this is kept only until it's removed.
/// </para>
/// </summary>
[Obsolete("Use a Box with ColorInfo = ColorInfo.GradientHorizontal(start, end), or ColorInfo.GradientVertical(start, end) for Vertical.")]
public partial class GradientBox : Box
{
    private Color startColor = Color.White;
    private Color endColor = Color.White;

    private bool vertical;

    /// <summary>
    /// Whether the gradient runs top-to-bottom (<see cref="StartColor"/> at the top) instead of
    /// left-to-right.
    /// </summary>
    public bool Vertical
    {
        get => vertical;
        set
        {
            vertical = value;
            Invalidate(InvalidationFlags.Color);
        }
    }

    /// <summary>
    /// The Color at the left edge of the box (the top when <see cref="Vertical"/>).
    /// </summary>
    public Color StartColor
    {
        get => startColor;
        set
        {
            startColor = value;
            Invalidate(InvalidationFlags.Color);
        }
    }

    /// <summary>
    /// The Color at the right edge of the box (the bottom when <see cref="Vertical"/>).
    /// </summary>
    public Color EndColor
    {
        get => endColor;
        set
        {
            endColor = value;
            Invalidate(InvalidationFlags.Color);
        }
    }

    protected override void GenerateVertices()
    {
        // Build the quad geometry (positions, model matrix, draw rectangle) as normal, then reColor.
        base.GenerateVertices();
        applyGradient();
    }

    protected override void UpdateDrawColor()
    {
        // base recomputes DrawAlpha (and flattens the Color); reapply our per-corner gradient on top.
        base.UpdateDrawColor();
        applyGradient();
    }

    private void applyGradient()
    {
        if (Vertices.Length < 4)
            return;

        Vector4 start = toLinear(startColor);
        Vector4 end = toLinear(endColor);

        // Vertex order from Drawable.GenerateVertices: 0 = top-left, 1 = top-right, 2 = bottom-right, 3 = bottom-left.
        Vertices[0].Color = start;
        Vertices[1].Color = vertical ? start : end;
        Vertices[2].Color = end;
        Vertices[3].Color = vertical ? end : start;
    }

    private Vector4 toLinear(Color c)
    {
        float r = ColorExtensions.SrgbToLinear(c.R);
        float g = ColorExtensions.SrgbToLinear(c.G);
        float b = ColorExtensions.SrgbToLinear(c.B);
        return new Vector4(r, g, b, DrawAlpha * (c.A / 255f));
    }
}
