// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// One point along a <see cref="HoldNoteBody"/>'s centre-line: where it is, how wide the ribbon is
/// there, how visible (fading in at the horizon, out past the judgement line), and how far its colour
/// is lifted toward white (the glow while held).
/// </summary>
public readonly struct HoldBodySample
{
    public readonly Vector2 Centre;
    public readonly float HalfWidth;
    public readonly float Alpha;
    public readonly float Glow;

    public HoldBodySample(Vector2 centre, float halfWidth, float alpha, float glow)
    {
        Centre = centre;
        HalfWidth = halfWidth;
        Alpha = alpha;
        Glow = glow;
    }

    public bool SameAs(in HoldBodySample other) =>
        Centre.X == other.Centre.X && Centre.Y == other.Centre.Y && HalfWidth == other.HalfWidth && Alpha == other.Alpha && Glow == other.Glow;
}

/// <summary>
/// The tapered ribbon connecting a hold note's head to its tail on the perspective highway.
/// <para>
/// Like <see cref="LaneBeam"/>, it fills its parent and takes its geometry in the parent's pixel space,
/// so it never has to be moved or resized itself. Each sample gives a centre, a half-width and a
/// colour; the ribbon is one quad per pair of samples, with its edges offset horizontally, as lane
/// edges converge on screen. On this projection a lane (even one a hold changes along) is a straight
/// line between depths, so the owner only needs samples where the colour changes, not along a curve.
/// </para>
/// </summary>
public partial class HoldNoteBody : Drawable
{
    /// <summary>
    /// The most samples a ribbon takes.
    /// </summary>
    public const int MAX_SAMPLES = 16;

    private readonly HoldBodySample[] samples = new HoldBodySample[MAX_SAMPLES];
    private int sampleCount;

    /// <summary>
    /// Removes all geometry, rendering nothing.
    /// </summary>
    public void ClearGeometry()
    {
        if (sampleCount == 0)
            return;

        sampleCount = 0;
        Invalidate(InvalidationFlags.DrawInfo);
    }

    /// <summary>
    /// Replaces the ribbon. Samples run head -> tail in the parent's pixel space. Nothing is rebuilt
    /// when they match the current ones (a paused field).
    /// </summary>
    public void SetGeometry(ReadOnlySpan<HoldBodySample> newSamples)
    {
        if (newSamples.Length > MAX_SAMPLES)
            throw new ArgumentException($"A hold body takes at most {MAX_SAMPLES} samples.", nameof(newSamples));

        if (newSamples.Length == sampleCount)
        {
            bool same = true;

            for (int i = 0; i < sampleCount && same; i++)
                same = samples[i].SameAs(newSamples[i]);

            if (same)
                return;
        }

        newSamples.CopyTo(samples);
        sampleCount = newSamples.Length;
        Invalidate(InvalidationFlags.DrawInfo);
    }

    protected override VertexTopology Topology => VertexTopology.Quads;

    // Each sample has its own colour, so the default corner-colour fast path used by fades would
    // only recolour the first quad; regenerate instead.
    protected override void UpdateDrawColor() => UpdateTransforms();

    protected override void GenerateVertices()
    {
        if (sampleCount < 2)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        // Grow-only: the sample count changes as the hold moves, and the storage is reused.
        SetVertexCount((sampleCount - 1) * 4);

        var baseColor = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha * (Color.A / 255f));

        // Like LaneBeam: parent pixels are normalized to the unit square, then ModelMatrix maps them
        // to screen space.
        float w = AppliedDrawSize.X > 0 ? AppliedDrawSize.X : 1;
        float h = AppliedDrawSize.Y > 0 ? AppliedDrawSize.Y : 1;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        Vector2 nearLeft = default, nearRight = default;
        Vector4 nearColor = default;

        for (int i = 0; i < sampleCount; i++)
        {
            HoldBodySample sample = samples[i];

            // Each edge point is transformed once and shared by the quads either side of it.
            Vector2 left = Vector2.Transform(new Vector2((sample.Centre.X - sample.HalfWidth) / w, sample.Centre.Y / h), ModelMatrix);
            Vector2 right = Vector2.Transform(new Vector2((sample.Centre.X + sample.HalfWidth) / w, sample.Centre.Y / h), ModelMatrix);
            var color = new Vector4(
                baseColor.X + (1 - baseColor.X) * sample.Glow,
                baseColor.Y + (1 - baseColor.Y) * sample.Glow,
                baseColor.Z + (1 - baseColor.Z) * sample.Glow,
                baseColor.W * sample.Alpha);

            minX = Math.Min(minX, Math.Min(left.X, right.X));
            maxX = Math.Max(maxX, Math.Max(left.X, right.X));
            minY = Math.Min(minY, Math.Min(left.Y, right.Y));
            maxY = Math.Max(maxY, Math.Max(left.Y, right.Y));

            if (i > 0)
            {
                // Quads topology: top-left, top-right, bottom-right, bottom-left. The far sample is the top.
                int offset = (i - 1) * 4;
                Vertices[offset + 0] = new Vertex { Position = left, TexCoords = new Vector2(0, 0), Color = color };
                Vertices[offset + 1] = new Vertex { Position = right, TexCoords = new Vector2(1, 0), Color = color };
                Vertices[offset + 2] = new Vertex { Position = nearRight, TexCoords = new Vector2(1, 1), Color = nearColor };
                Vertices[offset + 3] = new Vertex { Position = nearLeft, TexCoords = new Vector2(0, 1), Color = nearColor };
            }

            nearLeft = left;
            nearRight = right;
            nearColor = color;
        }

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
}
