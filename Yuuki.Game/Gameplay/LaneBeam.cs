// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// A glow up one lane from the judgement line, fading out toward the horizon — shown while the lane's
/// key is held (and flashed red on a miss). Like <see cref="LaneBackground"/>, a single perspective
/// quad in the parent's pixel space, but with its top corners fully transparent.
/// </summary>
public partial class LaneBeam : Drawable
{
    private Vector2 topLeft, topRight, bottomRight, bottomLeft;
    private bool hasCorners;

    /// <summary>
    /// Sets the beam's corners in the parent's content-pixel space (top = far end).
    /// </summary>
    public void SetCorners(Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl)
    {
        topLeft = tl;
        topRight = tr;
        bottomRight = br;
        bottomLeft = bl;
        hasCorners = true;
        Invalidate(InvalidationFlags.DrawInfo);
    }

    // Per-corner alpha is baked into the vertices, so the default corner-colour fast path used by
    // fades would flatten the gradient; regenerate instead.
    protected override void UpdateDrawColor() => UpdateTransforms();

    protected override void GenerateVertices()
    {
        if (!hasCorners)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        SetVertexCount(4);

        var near = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha * (Color.A / 255f));
        var far = new Vector4(near.X, near.Y, near.Z, 0);

        float w = AppliedDrawSize.X > 0 ? AppliedDrawSize.X : 1;
        float h = AppliedDrawSize.Y > 0 ? AppliedDrawSize.Y : 1;

        Vector2 vtl = Vector2.Transform(new Vector2(topLeft.X / w, topLeft.Y / h), ModelMatrix);
        Vector2 vtr = Vector2.Transform(new Vector2(topRight.X / w, topRight.Y / h), ModelMatrix);
        Vector2 vbr = Vector2.Transform(new Vector2(bottomRight.X / w, bottomRight.Y / h), ModelMatrix);
        Vector2 vbl = Vector2.Transform(new Vector2(bottomLeft.X / w, bottomLeft.Y / h), ModelMatrix);

        Vertices[0] = new Vertex { Position = vtl, TexCoords = new Vector2(0, 0), Color = far };
        Vertices[1] = new Vertex { Position = vtr, TexCoords = new Vector2(1, 0), Color = far };
        Vertices[2] = new Vertex { Position = vbr, TexCoords = new Vector2(1, 1), Color = near };
        Vertices[3] = new Vertex { Position = vbl, TexCoords = new Vector2(0, 1), Color = near };

        float minX = Math.Min(Math.Min(vtl.X, vtr.X), Math.Min(vbr.X, vbl.X));
        float minY = Math.Min(Math.Min(vtl.Y, vtr.Y), Math.Min(vbr.Y, vbl.Y));
        float maxX = Math.Max(Math.Max(vtl.X, vtr.X), Math.Max(vbr.X, vbl.X));
        float maxY = Math.Max(Math.Max(vtl.Y, vtr.Y), Math.Max(vbr.Y, vbl.Y));
        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
}
