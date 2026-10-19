// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// Fills the lane trapezoid — the region between the outer rails from the far horizon down to the
/// player edge — rather than the playfield's full rectangular bounds. Corners are given in the
/// parent's content-pixel coordinates and emitted as a single quad, so the backdrop matches the
/// perspective highway exactly. Meant to fill the parent (<c>RelativeSizeAxes.Both</c>).
/// </summary>
public partial class LaneBackground : Drawable
{
    private Vector2 topLeft, topRight, bottomRight, bottomLeft;
    private bool hasCorners;

    /// <summary>
    /// Sets the trapezoid corners in the parent's content-pixel space.
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

    protected override void GenerateVertices()
    {
        if (!hasCorners)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        // Grow-only. This alternates between 0 and 4 as the lane gains and loses its corners, and
        // reallocating on each change allocated in both directions, times the three draw nodes every
        // drawable keeps for triple buffering.
        SetVertexCount(4);

        float rLinear = ColorExtensions.SrgbToLinear(Color.R);
        float gLinear = ColorExtensions.SrgbToLinear(Color.G);
        float bLinear = ColorExtensions.SrgbToLinear(Color.B);
        var calculatedColor = new Vector4(rLinear, gLinear, bLinear, DrawAlpha * (Color.A / 255f));

        float w = AppliedDrawSize.X > 0 ? AppliedDrawSize.X : 1;
        float h = AppliedDrawSize.Y > 0 ? AppliedDrawSize.Y : 1;

        Vector2 vtl = Vector2.Transform(new Vector2(topLeft.X / w, topLeft.Y / h), ModelMatrix);
        Vector2 vtr = Vector2.Transform(new Vector2(topRight.X / w, topRight.Y / h), ModelMatrix);
        Vector2 vbr = Vector2.Transform(new Vector2(bottomRight.X / w, bottomRight.Y / h), ModelMatrix);
        Vector2 vbl = Vector2.Transform(new Vector2(bottomLeft.X / w, bottomLeft.Y / h), ModelMatrix);

        // Quads topology: top-left, top-right, bottom-right, bottom-left.
        Vertices[0] = new Vertex { Position = vtl, TexCoords = new Vector2(0, 0), Color = calculatedColor };
        Vertices[1] = new Vertex { Position = vtr, TexCoords = new Vector2(1, 0), Color = calculatedColor };
        Vertices[2] = new Vertex { Position = vbr, TexCoords = new Vector2(1, 1), Color = calculatedColor };
        Vertices[3] = new Vertex { Position = vbl, TexCoords = new Vector2(0, 1), Color = calculatedColor };

        float minX = Math.Min(Math.Min(vtl.X, vtr.X), Math.Min(vbr.X, vbl.X));
        float minY = Math.Min(Math.Min(vtl.Y, vtr.Y), Math.Min(vbr.Y, vbl.Y));
        float maxX = Math.Max(Math.Max(vtl.X, vtr.X), Math.Max(vbr.X, vbl.X));
        float maxY = Math.Max(Math.Max(vtl.Y, vtr.Y), Math.Max(vbr.Y, vbl.Y));
        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
}
