// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Rendering;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Light round the screen's edges, as an arcade cabinet's LEDs: glowing in from every edge in <see cref="GlowColor"/>,
/// deeper and brighter the higher <see cref="Intensity"/> goes. It falls away smoothly, and where two edges meet their
/// light adds up as light does, so a corner shows no seam and is no brighter than an edge. The results screen pulses
/// it with the reveal, then with the beat.
/// </summary>
/// <remarks>
/// <para>It fills its parent and never takes input. It's light, added over what's behind it.</para>
/// <para>The game's <c>edge-glow.frag</c> shader draws it, so nothing shows where that can't draw (headless).</para>
/// </remarks>
public partial class EdgeGlow : Drawable
{
    /// <summary>
    /// How fast the light falls away: it fades by e this many times over its reach.
    /// </summary>
    private const float falloff = 2.5f;

    private float intensity;
    private Color glowColor = Color.White;

    /// <summary>
    /// How strong the light is: 0 is off, 1 a rank landing; it can go past 1.
    /// </summary>
    public float Intensity
    {
        get => intensity;
        set
        {
            intensity = Math.Max(0, value);
            Alpha = intensity > 0 ? 1 : 0;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public Color GlowColor
    {
        get => glowColor;
        set
        {
            glowColor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public EdgeGlow()
    {
        RelativeSizeAxes = Axes.Both;
        Blending = BlendingMode.Additive;
        Alpha = 0;
    }

    public override bool HandlePositionalInput => false;

    protected override VertexTopology Topology => VertexTopology.Triangles;

    // The color is baked into every vertex (as RingArc's is).
    protected override void UpdateDrawColor() => UpdateTransforms();

    protected override DrawNode CreateDrawNode() => new GameShaderDrawNode("edge-glow.frag");

    protected override void GenerateVertices()
    {
        Vector2 size = AppliedDrawSize;

        if (intensity <= 0 || size.X <= 0 || size.Y <= 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        // Deeper and stronger the higher it goes, as deep and strong as a CSS inset shadow of blur 70 + 110i and
        // spread 6 + 26i at 85% of i: brightest at the edge, mostly gone by the spread and half the blur in.
        float blur = 70 + 110 * intensity;
        float spread = 6 + 26 * intensity;
        float strength = Math.Min(0.95f, intensity * 0.85f) * Math.Min(1, 0.5f + spread / blur);
        float reach = spread + blur / 2;

        var color = new Vector4(
            ColorExtensions.SrgbToLinear(glowColor.R),
            ColorExtensions.SrgbToLinear(glowColor.G),
            ColorExtensions.SrgbToLinear(glowColor.B),
            DrawAlpha * strength * (glowColor.A / 255f));

        // A quarter at a time, split down the middle both ways: in each, how far in from the nearest side and the
        // nearest top or bottom is even across it, so the shader gets it exact anywhere.
        float[] xs = { 0, size.X / 2, size.X };
        float[] ys = { 0, size.Y / 2, size.Y };

        Vertex vertex(int i, int j) => new Vertex
        {
            Position = Vector2.Transform(new Vector2(xs[i] / size.X, ys[j] / size.Y), ModelMatrix),
            TexCoords = new Vector2(Math.Min(xs[i], size.X - xs[i]) / reach, Math.Min(ys[j], size.Y - ys[j]) / reach),
            Color = color,
            TexIndex = falloff
        };

        SetVertexCount(4 * 6);
        int offset = 0;

        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                Vertex p0 = vertex(i, j), p1 = vertex(i + 1, j), p2 = vertex(i + 1, j + 1), p3 = vertex(i, j + 1);

                Vertices[offset++] = p0;
                Vertices[offset++] = p1;
                Vertices[offset++] = p2;
                Vertices[offset++] = p2;
                Vertices[offset++] = p3;
                Vertices[offset++] = p0;
            }
        }

        Vector2 topLeft = Vector2.Transform(Vector2.Zero, ModelMatrix);
        Vector2 bottomRight = Vector2.Transform(Vector2.One, ModelMatrix);
        DrawRectangle = new RectangleF(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }
}
