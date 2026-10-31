// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Rendering;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Sunlight out from behind a rank that has just landed, as light through leaves and petals is: rays of every width,
/// bunched here and spaced there, each swelling, narrowing, splitting and fading in its own time rather than the whole
/// turning together, in a soft glow round the sun, the whole breathing slowly. White hot close in, in
/// <see cref="Drawable.Color"/> further out, or after a Full Bloom in the heroes' colors in turn round it
/// (<see cref="HeroColored"/>).
/// </summary>
/// <remarks>
/// <para>It fills its box, round its middle: give it a square one. It's light, added over what's behind it. It moves
/// by itself; <see cref="Drawable.Rotation"/> still turns it, if the screen wants it to.</para>
/// <para>The game's <c>rays.frag</c> shader draws it, so nothing shows where that can't draw (headless).</para>
/// </remarks>
public partial class LightRays : Drawable
{
    /// <summary>
    /// The rank's light: narrow rays.
    /// </summary>
    public const float THIN = 0.3f;

    /// <summary>
    /// A Full Bloom's: broader.
    /// </summary>
    public const float WIDE = 0.45f;

    /// <summary>
    /// The shader's time runs round every this many seconds, so it stays precise however long the game runs.
    /// </summary>
    private const double time_wrap = 600;

    private int spokes = 20;
    private float spokeWidth = THIN;
    private bool heroColored;
    private float peak = 0.6f;

    /// <summary>
    /// About how many rays go round. They're never evenly spaced, and come and go, so this is how busy the light is.
    /// </summary>
    public int Spokes
    {
        get => spokes;
        set
        {
            spokes = Math.Max(1, value);
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// How broad the rays are, 0–1 (<see cref="THIN"/>, <see cref="WIDE"/>): narrower rays leave more dark between.
    /// </summary>
    public float SpokeWidth
    {
        get => spokeWidth;
        set
        {
            spokeWidth = Math.Clamp(value, 0.05f, 1);
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Whether the light takes the heroes' colors in turn round it (a Full Bloom's) rather than <see cref="Drawable.Color"/>.
    /// </summary>
    public bool HeroColored
    {
        get => heroColored;
        set
        {
            heroColored = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// How bright it is, 0–1 (it can go past), before <see cref="Drawable.Alpha"/>.
    /// </summary>
    public float Peak
    {
        get => peak;
        set
        {
            peak = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    public LightRays()
    {
        Blending = BlendingMode.Additive;
    }

    public override bool HandlePositionalInput => false;

    protected override VertexTopology Topology => VertexTopology.Triangles;

    protected override DrawNode CreateDrawNode() => new CustomShaderDrawNode<RaysBlock>(GameShaders.STORE, "rays.frag", nameof(RaysBlock), block);

    private RaysBlock block() => new RaysBlock
    {
        // Narrower rays are the noise's peaks taken to a higher power.
        Narrowness = Math.Clamp(1f / spokeWidth, 1, 8),
        Density = spokes,
        Color = new Vector4(Color.R / 255f, Color.G / 255f, Color.B / 255f, DrawAlpha * peak * (Color.A / 255f)),
        Time = (float)(Clock.CurrentTime / 1000 % time_wrap),
        Heroes = heroColored ? 1 : 0
    };

    public override void Update()
    {
        // The light moves by itself.
        Invalidate(InvalidationFlags.DrawInfo);
        base.Update();
    }

    protected override void GenerateVertices()
    {
        if (peak <= 0)
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
            return;
        }

        Vertex vertex(float x, float y) => new Vertex
        {
            Position = Vector2.Transform(new Vector2(x, y), ModelMatrix),
            TexCoords = new Vector2(2 * x - 1, 2 * y - 1)
        };

        // One quad over the box: the shader lights every pixel of it.
        Vertex topLeft = vertex(0, 0), topRight = vertex(1, 0), bottomRight = vertex(1, 1), bottomLeft = vertex(0, 1);
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

    /// <summary>
    /// <c>RaysBlock</c> in <c>rays.frag</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private struct RaysBlock
    {
        public Vector4 Color;
        public float Density;
        public float Narrowness;
        public float Time;
        public float Heroes;
    }
}
