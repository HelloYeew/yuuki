// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Rendering;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// A Full Bloom round the results ring, as light. While the ring gathers itself (<see cref="Gather"/>), two white rings
/// close in on it over and over, brighter as it builds, and its middle fills with light. Then the bloom
/// (<see cref="Bloom"/>): a white-hot flash, and six rings in the heroes' colors bursting out one after another, past
/// the screen's edges.
/// </summary>
/// <remarks>
/// <para>It does each when told to, on its own clock, so a time already past shows it as it would be by now. Its box is
/// the ring's; the bloom reaches far outside it. It's light, added over what's behind it.</para>
/// <para>The game's <c>full-bloom.frag</c> shader draws it, so nothing shows where that can't draw (headless).</para>
/// </remarks>
public partial class FullBloomRings : Drawable
{
    /// <summary>
    /// How far out (as a multiple of half its box) the gather's rings start, and the bloom's go by the time they fade.
    /// </summary>
    private const float gather_reach = 3.6f;

    private const float bloom_reach = 8.2f;

    /// <summary>
    /// How long the bloom's light lasts, in seconds: the last ring starts 0.275 s in, and fades over one.
    /// </summary>
    private const double bloom_life = 1.3;

    /// <summary>
    /// The shader's time runs round every this many seconds, so it stays precise however long the game runs.
    /// </summary>
    private const double time_wrap = 600;

    private double gathersAt = double.PositiveInfinity;
    private double gatherLength = 560;
    private double bloomsAt = double.PositiveInfinity;

    public FullBloomRings()
    {
        Blending = BlendingMode.Additive;
    }

    public override bool HandlePositionalInput => false;

    protected override VertexTopology Topology => VertexTopology.Triangles;

    // The color is baked into every vertex (as RingArc's is).
    protected override void UpdateDrawColor() => UpdateTransforms();

    protected override DrawNode CreateDrawNode() => new GameShaderDrawNode("full-bloom.frag");

    /// <summary>
    /// The ring starts gathering itself at <paramref name="time"/> (now if not given), building over
    /// <paramref name="duration"/> ms; it stops when it blooms.
    /// </summary>
    public void Gather(double? time = null, double duration = 560)
    {
        gathersAt = time ?? Clock.CurrentTime;
        gatherLength = duration;
    }

    /// <summary>
    /// Everything blooms at <paramref name="time"/> (now if not given).
    /// </summary>
    public void Bloom(double? time = null) => bloomsAt = time ?? Clock.CurrentTime;

    public void Clear() => gathersAt = bloomsAt = double.PositiveInfinity;

    public override void Update()
    {
        // The light moves by itself.
        Invalidate(InvalidationFlags.DrawInfo);
        base.Update();
    }

    protected override void GenerateVertices()
    {
        double now = Clock.CurrentTime;
        double sinceBloom = (now - bloomsAt) / 1000;
        bool gathering = now >= gathersAt && now < bloomsAt;
        bool blooming = sinceBloom >= 0 && sinceBloom < bloom_life;

        float reach = blooming ? bloom_reach : gathering ? gather_reach : 0;
        Vector2 size = AppliedDrawSize;

        if (reach <= 0 || DrawAlpha <= 0 || size.X <= 0 || size.Y <= 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        var data = new Vector4(
            gathering ? (float)Math.Clamp((now - gathersAt) / gatherLength, 0, 1) : -1,
            blooming ? (float)sinceBloom : -1,
            (float)(now / 1000 % time_wrap),
            DrawAlpha * (Color.A / 255f));

        // A point the given distance from the middle, in halves of the box.
        Vertex vertex(float x, float y) => new Vertex
        {
            Position = Vector2.Transform(new Vector2(0.5f + x / 2, 0.5f + y / 2), ModelMatrix),
            TexCoords = new Vector2(x, y),
            Color = data,
            TexIndex = 0
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
