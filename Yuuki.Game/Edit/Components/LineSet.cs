// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Edit.Components;

/// <summary>
/// the beat grid behind the lanes, the ticks along the timeline.
/// </summary>
/// <remarks>
/// fill it with <see cref="Add"/> between <see cref="Clear"/> calls, in this drawable's local pixels;
/// it redraws on the next frame.
/// </remarks>
public partial class LineSet : Drawable
{
    private readonly List<(RectangleF Rect, Color Color, float Alpha)> lines = new List<(RectangleF, Color, float)>();

    protected override VertexTopology Topology => VertexTopology.Quads;

    public void Clear()
    {
        lines.Clear();
        Invalidate(InvalidationFlags.DrawInfo);
    }

    public void Add(RectangleF rect, Color color, float alpha = 1)
    {
        lines.Add((rect, color, alpha));
        Invalidate(InvalidationFlags.DrawInfo);
    }

    public int Count => lines.Count;

    protected override void GenerateVertices()
    {
        SetVertexCount(lines.Count * 4);

        float w = AppliedDrawSize.X > 0 ? AppliedDrawSize.X : 1;
        float h = AppliedDrawSize.Y > 0 ? AppliedDrawSize.Y : 1;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        int v = 0;

        foreach (var (rect, color, alpha) in lines)
        {
            var linear = new Vector4(
                ColorExtensions.SrgbToLinear(color.R),
                ColorExtensions.SrgbToLinear(color.G),
                ColorExtensions.SrgbToLinear(color.B),
                DrawAlpha * alpha * (color.A / 255f));

            var tl = Vector2.Transform(new Vector2(rect.X / w, rect.Y / h), ModelMatrix);
            var tr = Vector2.Transform(new Vector2((rect.X + rect.Width) / w, rect.Y / h), ModelMatrix);
            var br = Vector2.Transform(new Vector2((rect.X + rect.Width) / w, (rect.Y + rect.Height) / h), ModelMatrix);
            var bl = Vector2.Transform(new Vector2(rect.X / w, (rect.Y + rect.Height) / h), ModelMatrix);

            Vertices[v++] = new Vertex { Position = tl, Color = linear, TexCoords = new Vector2(0, 0) };
            Vertices[v++] = new Vertex { Position = tr, Color = linear, TexCoords = new Vector2(1, 0) };
            Vertices[v++] = new Vertex { Position = br, Color = linear, TexCoords = new Vector2(1, 1) };
            Vertices[v++] = new Vertex { Position = bl, Color = linear, TexCoords = new Vector2(0, 1) };

            minX = Math.Min(minX, Math.Min(tl.X, bl.X));
            maxX = Math.Max(maxX, Math.Max(tr.X, br.X));
            minY = Math.Min(minY, Math.Min(tl.Y, tr.Y));
            maxY = Math.Max(maxY, Math.Max(bl.Y, br.Y));
        }

        // The drawable's own bounds when empty, so masking still sees where it is.
        var origin = Vector2.Transform(Vector2.Zero, ModelMatrix);
        var far = Vector2.Transform(Vector2.One, ModelMatrix);

        DrawRectangle = lines.Count == 0
            ? new RectangleF(origin.X, origin.Y, far.X - origin.X, far.Y - origin.Y)
            : new RectangleF(Math.Min(minX, origin.X), Math.Min(minY, origin.Y), Math.Max(maxX, far.X) - Math.Min(minX, origin.X), Math.Max(maxY, far.Y) - Math.Min(minY, origin.Y));
    }
}

/// <summary>
/// A filled four-cornered shape, corners in local pixels, clockwise from the top left: the body of a
/// hold that drifts across lanes.
/// </summary>
public partial class QuadShape : Drawable
{
    private Vector2 topLeft, topRight, bottomRight, bottomLeft;

    protected override VertexTopology Topology => VertexTopology.Quads;

    public void SetCorners(Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl)
    {
        topLeft = tl;
        topRight = tr;
        bottomRight = br;
        bottomLeft = bl;
        Invalidate(InvalidationFlags.DrawInfo);
    }

    protected override void GenerateVertices()
    {
        SetVertexCount(4);

        float w = AppliedDrawSize.X > 0 ? AppliedDrawSize.X : 1;
        float h = AppliedDrawSize.Y > 0 ? AppliedDrawSize.Y : 1;

        var color = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha * (Color.A / 255f));

        Vector2 map(Vector2 p) => Vector2.Transform(new Vector2(p.X / w, p.Y / h), ModelMatrix);

        var a = map(topLeft);
        var b = map(topRight);
        var c = map(bottomRight);
        var d = map(bottomLeft);

        Vertices[0] = new Vertex { Position = a, Color = color, TexCoords = new Vector2(0, 0) };
        Vertices[1] = new Vertex { Position = b, Color = color, TexCoords = new Vector2(1, 0) };
        Vertices[2] = new Vertex { Position = c, Color = color, TexCoords = new Vector2(1, 1) };
        Vertices[3] = new Vertex { Position = d, Color = color, TexCoords = new Vector2(0, 1) };

        float minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        float minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        float maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        float maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
}
