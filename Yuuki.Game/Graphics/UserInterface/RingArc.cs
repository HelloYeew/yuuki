// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A ring filled clockwise from 12 o'clock up to <see cref="Progress"/> — a circular progress bar.
/// The ring fills this drawable's bounds; <see cref="InnerRadius"/> sets its thickness. Emits one quad
/// per segment, like <see cref="Yuuki.Game.Gameplay.HoldNoteBody"/>, since the framework has no arc.
/// </summary>
public partial class RingArc : Drawable
{
    /// <summary>
    /// Segments in a full turn; a partial ring uses its share.
    /// </summary>
    private const int full_segments = 160;

    private float progress = 1;
    private float innerRadius = 0.85f;

    /// <summary>
    /// How much of the ring is drawn, 0–1.
    /// </summary>
    public float Progress
    {
        get => progress;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (value == progress)
                return;

            progress = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// The inner edge as a fraction of the outer radius (0 is a pie, near 1 a hairline).
    /// </summary>
    public float InnerRadius
    {
        get => innerRadius;
        set
        {
            innerRadius = Math.Clamp(value, 0, 1);
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    protected override VertexTopology Topology => VertexTopology.Triangles;

    // The color is baked into every segment vertex, so the default four-corner color fast path would
    // leave it stale; regenerate instead (same as the framework's Path).
    protected override void UpdateDrawColor() => UpdateTransforms();

    protected override void GenerateVertices()
    {
        int segments = (int)MathF.Ceiling(full_segments * progress);
        if (segments == 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        SetVertexCount(segments * 6);

        var color = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha * (Color.A / 255f));

        float sweep = MathF.Tau * progress;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        Vector2 previousOuter = Vector2.Zero, previousInner = Vector2.Zero;

        for (int i = 0; i <= segments; i++)
        {
            // Start at 12 o'clock, going clockwise (screen y points down).
            float angle = -MathF.PI / 2 + sweep * i / segments;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            // Unit-square local space, mapped to screen by ModelMatrix.
            Vector2 outer = Vector2.Transform(new Vector2(0.5f + 0.5f * cos, 0.5f + 0.5f * sin), ModelMatrix);
            Vector2 inner = Vector2.Transform(new Vector2(0.5f + 0.5f * innerRadius * cos, 0.5f + 0.5f * innerRadius * sin), ModelMatrix);

            minX = Math.Min(minX, Math.Min(outer.X, inner.X));
            minY = Math.Min(minY, Math.Min(outer.Y, inner.Y));
            maxX = Math.Max(maxX, Math.Max(outer.X, inner.X));
            maxY = Math.Max(maxY, Math.Max(outer.Y, inner.Y));

            if (i > 0)
            {
                int offset = (i - 1) * 6;
                Vertices[offset + 0] = new Vertex { Position = previousOuter, TexCoords = new Vector2(0, 0), Color = color };
                Vertices[offset + 1] = new Vertex { Position = outer, TexCoords = new Vector2(1, 0), Color = color };
                Vertices[offset + 2] = new Vertex { Position = inner, TexCoords = new Vector2(1, 1), Color = color };
                Vertices[offset + 3] = new Vertex { Position = inner, TexCoords = new Vector2(1, 1), Color = color };
                Vertices[offset + 4] = new Vertex { Position = previousInner, TexCoords = new Vector2(0, 1), Color = color };
                Vertices[offset + 5] = new Vertex { Position = previousOuter, TexCoords = new Vector2(0, 0), Color = color };
            }

            previousOuter = outer;
            previousInner = inner;
        }

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
}
