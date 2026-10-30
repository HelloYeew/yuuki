// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Rendering;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// Wave effect on rank land
/// </summary>
/// <remarks>
/// <para>It does each when told to (<see cref="Land"/>, <see cref="Crack"/>), on its own clock, so a time already past
/// shows it as it would be by now. Its box is the ring's: the bloom covers its middle, and the rings reach out to over
/// twice its size. It's light, added over what's behind it.</para>
/// <para>The game's <c>landing-waves.frag</c> shader draws it, so nothing shows where that can't draw (headless).</para>
/// </remarks>
public partial class LandingWaves : Drawable
{
    /// <summary>
    /// How long a landing's light lasts, and how far out (as a multiple of half its box) it reaches by then: the first
    /// ring's crest at its widest, with its fringe and glow.
    /// </summary>
    private const double landing_life = 1.05;

    private const float landing_reach = 2.4f;

    /// <summary>
    /// How long a break's light lasts, and how far out it reaches.
    /// </summary>
    private const double break_life = 0.6;

    private const float break_reach = 1.5f;

    /// <summary>
    /// The shader's time runs round every this many seconds, so it stays precise however long the game runs.
    /// </summary>
    private const double time_wrap = 600;

    private Color landingColor = Color.White;
    private double landsAt = double.PositiveInfinity;
    private double cracksAt = double.PositiveInfinity;

    public LandingWaves()
    {
        Blending = BlendingMode.Additive;
    }

    public override bool HandlePositionalInput => false;

    protected override VertexTopology Topology => VertexTopology.Triangles;

    // The color is baked into every vertex (as RingArc's is).
    protected override void UpdateDrawColor() => UpdateTransforms();

    protected override DrawNode CreateDrawNode() => new GameShaderDrawNode("landing-waves.frag");

    /// <summary>
    /// A rank lands in <paramref name="color"/> at <paramref name="time"/> (now if not given).
    /// </summary>
    public void Land(Color color, double? time = null)
    {
        landingColor = color;
        landsAt = time ?? Clock.CurrentTime;
    }

    /// <summary>
    /// A rank breaks at <paramref name="time"/> (now if not given).
    /// </summary>
    public void Crack(double? time = null) => cracksAt = time ?? Clock.CurrentTime;

    public void Clear() => landsAt = cracksAt = double.PositiveInfinity;

    public override void Update()
    {
        // The light moves by itself.
        Invalidate(InvalidationFlags.DrawInfo);
        base.Update();
    }

    protected override void GenerateVertices()
    {
        double now = Clock.CurrentTime;
        double sinceLanding = (now - landsAt) / 1000;
        double sinceBreak = (now - cracksAt) / 1000;

        // Only as far out as there's light this moment.
        float reach = 0;
        if (sinceLanding >= 0 && sinceLanding < landing_life)
            reach = landing_reach;
        if (sinceBreak >= 0 && sinceBreak < break_life)
            reach = Math.Max(reach, break_reach);

        Vector2 size = AppliedDrawSize;

        if (reach <= 0 || DrawAlpha <= 0 || size.X <= 0 || size.Y <= 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        // Not yet is far in the past, to the shader: nothing.
        var data = new Vector4(
            (float)(sinceLanding >= 0 ? sinceLanding : -1),
            (float)(sinceBreak >= 0 ? sinceBreak : -1),
            (float)(now / 1000 % time_wrap),
            DrawAlpha * (Color.A / 255f));
        float color = GameShaderDrawNode.PackColor(landingColor);

        // A point the given distance from the middle, in halves of the box.
        Vertex vertex(float x, float y) => new Vertex
        {
            Position = Vector2.Transform(new Vector2(0.5f + x / 2, 0.5f + y / 2), ModelMatrix),
            TexCoords = new Vector2(x, y),
            Color = data,
            TexIndex = color
        };

        Vertex topLeft = vertex(-reach, -reach), topRight = vertex(reach, -reach), bottomRight = vertex(reach, reach), bottomLeft = vertex(-reach, reach);
        SetVertexCount(6);
        Vertices[0] = topLeft;
        Vertices[1] = topRight;
        Vertices[2] = bottomRight;
        Vertices[3] = bottomRight;
        Vertices[4] = bottomLeft;
        Vertices[5] = topLeft;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        foreach (Vertex corner in new[] { topLeft, topRight, bottomRight, bottomLeft })
        {
            minX = Math.Min(minX, corner.Position.X);
            minY = Math.Min(minY, corner.Position.Y);
            maxX = Math.Max(maxX, corner.Position.X);
            maxY = Math.Max(maxY, corner.Position.Y);
        }

        DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }
}
