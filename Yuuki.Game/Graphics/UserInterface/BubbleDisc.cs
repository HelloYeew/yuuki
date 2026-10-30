// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Rendering;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A disc that's alive, like a soap bubble. Its edge never quite sits still; every so often it wobs (as if poked: it
/// wobbles out of round a few times and settles), and each wob sends a ripple of light out from its edge. Its film
/// swirls with the heroes' colors, faint over its face and bright at its rim, with glints of reflected light and a glow
/// around it.
/// </summary>
/// <remarks>
/// <para>It comes in two halves, so something can be inside it. This drawable is the back: the disc and the far side
/// of the film. <see cref="Front"/> is the near side (the rim, the glints, the glow and the ripples): add it over
/// whatever is inside, centred in the same parent. It keeps the disc's size.</para>
/// <para>It fills its box, round its middle: give it a square one. Its glow and ripples reach out past it, up to
/// <see cref="REACH"/> of its radius.</para>
/// <para>It wobs by itself every <see cref="IdleInterval"/>; a screen can make it wob on cue instead (a rank landing, the
/// beat) with <see cref="Wob"/>, which also holds off the next idle one.</para>
/// <para>The film is drawn by the game's bubble shaders (<c>bubble-back.frag</c>, <c>bubble-front.frag</c>). Where they
/// can't draw (headless, or they won't build) the bubble is the disc and its ripples alone.</para>
/// </remarks>
public partial class BubbleDisc : Container
{
    /// <summary>
    /// How far out past the edge the glow and ripples reach, as a multiple of the radius.
    /// </summary>
    public const float REACH = 1.3f;

    /// <summary>
    /// How far past the edge a ripple runs before it's gone, as a fraction of the radius.
    /// </summary>
    private const float ripple_reach = 0.18f;

    /// <summary>
    /// Points round the edge.
    /// </summary>
    private const int segments = 144;

    /// <summary>
    /// A wob's wobbling: how fast it dies down, and how long one wobble takes, in ms.
    /// </summary>
    private const double wob_decay = 380;

    private const double wob_period = 220;

    /// <summary>
    /// How long a ripple takes to run out and fade, and how long a wob is kept (by then it's settled), in ms.
    /// </summary>
    private const double ripple_life = 1500;

    private const double wob_life = 1600;

    /// <summary>
    /// How strong a wob is when the disc wobs by itself.
    /// </summary>
    private const float idle_strength = 0.7f;

    /// <summary>
    /// How far the disc's edge fades either side of where it is, in screen pixels.
    /// </summary>
    private const float edge_feather = 0.75f;

    /// <summary>
    /// The shader's time runs round every this many seconds, so it stays precise however long the game runs.
    /// </summary>
    private const double shader_time_wrap = 600;

    /// <summary>
    /// The edge's radius at each point round it, as a multiple of the disc's, for this frame.
    /// </summary>
    private readonly float[] edge = new float[segments];

    private readonly List<WobState> wobs = new List<WobState>();

    // Only which way each wob squashes the disc; the same every time.
    private readonly Random random = new Random(7);

    private double lastWob = double.NaN;
    private float flash;

    /// <summary>
    /// The disc's own color, and how solid it is.
    /// </summary>
    public Color DiscColor { get; set; } = Color.White;

    public float DiscAlpha { get; set; } = 1;

    /// <summary>
    /// The ripples' color.
    /// </summary>
    public Color LightColor { get; set; } = Color.White;

    /// <summary>
    /// How much the edge moves, by itself and on a wob: 0 keeps it round.
    /// </summary>
    public float Wobble { get; set; } = 1;

    /// <summary>
    /// How bright the ripples are: 0 sends none.
    /// </summary>
    public float Ripples { get; set; } = 1;

    /// <summary>
    /// How bright the film is (its colors, its rim and its glints): 0 leaves the disc plain.
    /// </summary>
    public float Sheen { get; set; } = 1;

    /// <summary>
    /// How bright the glow around it is.
    /// </summary>
    public float Glow { get; set; } = 1;

    /// <summary>
    /// The music's beat, 0–1 (1 on the beat): the disc breathes out with it.
    /// </summary>
    public float Beat { get; set; }

    /// <summary>
    /// How long the disc goes without a wob before it wobs by itself, in ms; 0 or less, never.
    /// </summary>
    public double IdleInterval { get; set; } = 2400;

    /// <summary>
    /// Greys the film, as on a failed run.
    /// </summary>
    public bool Greyed { get; set; }

    /// <summary>
    /// How many ripples are running out.
    /// </summary>
    public int ActiveRipples { get; private set; }

    /// <summary>
    /// How far the edge is from round this frame: the most it's out or in from its average, as a fraction of the radius.
    /// </summary>
    public float OutOfRound { get; private set; }

    /// <summary>
    /// The bubble's near side, over whatever is inside it: its rim, glints, glow and ripples. Add it centred in the same
    /// parent as the disc, over what's inside.
    /// </summary>
    public Container Front { get; } = new Container
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre
    };

    public BubbleDisc()
    {
        Children = new Drawable[]
        {
            new Body(this),
            new Membrane(this, false)
        };

        Front.Children = new Drawable[]
        {
            new Membrane(this, true),
            new RippleLight(this)
        };
    }

    public override Vector2 Size
    {
        get => base.Size;
        set
        {
            base.Size = value;
            Front.Size = value;
        }
    }

    public override bool HandlePositionalInput => false;

    /// <summary>
    /// Wobs the disc and sends a ripple out, at <paramref name="time"/> (now if not given). A time already past shows
    /// as it would be by now.
    /// </summary>
    /// <param name="time">When, on this disc's clock.</param>
    /// <param name="strength">How hard: 1 is a good poke; the disc wobs itself at 0.7.</param>
    public void Wob(double? time = null, float strength = 1)
    {
        double at = time ?? Clock.CurrentTime;

        wobs.Add(new WobState(at, strength, (float)(random.NextDouble() * MathF.PI)));
        lastWob = double.IsNaN(lastWob) ? at : Math.Max(lastWob, at);
    }

    public override void Update()
    {
        double now = Clock.CurrentTime;

        if (IdleInterval > 0)
        {
            // The first wob by itself comes soon after it's shown, not a whole interval on.
            if (double.IsNaN(lastWob))
                lastWob = now - IdleInterval + 600;

            if (now - lastWob >= IdleInterval)
                Wob(now, idle_strength);
        }

        wobs.RemoveAll(w => now - w.Time > wob_life);
        updateEdge(now);

        foreach (Drawable layer in InternalChildren)
            layer.Invalidate(InvalidationFlags.DrawInfo);

        foreach (Drawable layer in Front.InternalChildren)
            layer.Invalidate(InvalidationFlags.DrawInfo);

        base.Update();
    }

    private void updateEdge(double now)
    {
        float seconds = (float)(now / 1000);
        float breath = 0.012f * Beat;

        // Each wob squashes the disc along its own direction and back (a few wobbles, dying down), with a little of a
        // three-cornered wobble on top, and puffs it out a little as it's poked.
        Span<(float Squash, float Corners, float Direction)> terms = stackalloc (float, float, float)[wobs.Count];
        float puff = 0;
        int ripples = 0;
        flash = 0;

        for (int i = 0; i < wobs.Count; i++)
        {
            WobState wob = wobs[i];
            float d = (float)(now - wob.Time);

            if (d < 0)
                continue;

            float envelope = wob.Strength * MathF.Exp(-d / (float)wob_decay);
            terms[i] = (envelope * 0.024f * MathF.Sin(MathF.Tau * d / (float)wob_period),
                envelope * 0.011f * MathF.Sin(MathF.Tau * d / 150), wob.Direction);
            puff += wob.Strength * 0.016f * MathF.Exp(-d / 140) * (1 - MathF.Exp(-d / 30));
            flash += wob.Strength * MathF.Exp(-d / 280) * (1 - MathF.Exp(-d / 25));

            if (d < ripple_life)
                ripples++;
        }

        ActiveRipples = Ripples > 0 ? ripples : 0;

        float min = float.MaxValue, max = float.MinValue;

        for (int i = 0; i < segments; i++)
        {
            float angle = MathF.Tau * i / segments;

            // Never quite still: three slow wobbles drifting round, each its own way.
            float r = Wobble * (0.006f * MathF.Sin(2 * angle + 0.9f * seconds + 0.3f)
                                + 0.005f * MathF.Sin(3 * angle - 0.7f * seconds + 1.9f)
                                + 0.003f * MathF.Sin(5 * angle + 1.3f * seconds + 4.1f));

            foreach ((float squash, float corners, float direction) in terms)
                r += Wobble * (squash * MathF.Cos(2 * (angle - direction)) + corners * MathF.Cos(3 * (angle - direction) + 0.5f));

            edge[i] = 1 + breath + Wobble * puff + r;
            min = Math.Min(min, r);
            max = Math.Max(max, r);
        }

        OutOfRound = Math.Max(Math.Abs(min), Math.Abs(max));
    }

    private static float outCubic(float t) => (float)EasingFunctions.Apply(Easing.OutCubic, t);

    /// <param name="Time">When it was poked, on the disc's clock.</param>
    /// <param name="Strength">How hard.</param>
    /// <param name="Direction">Which way it squashes, in radians.</param>
    private readonly record struct WobState(double Time, float Strength, float Direction);

    /// <summary>
    /// One layer of the disc, filling it: points on it, from its middle, as screen positions.
    /// </summary>
    private abstract partial class Layer : Drawable
    {
        protected readonly BubbleDisc Disc;

        protected Layer(BubbleDisc disc)
        {
            Disc = disc;
            RelativeSizeAxes = Axes.Both;
        }

        public override bool HandlePositionalInput => false;

        protected override VertexTopology Topology => VertexTopology.Triangles;

        // Every color is in its own vertices (as RingArc's is).
        protected override void UpdateDrawColor() => UpdateTransforms();

        /// <summary>
        /// The point at an angle (radians clockwise from 3 o'clock) and a radius (a multiple of the disc's).
        /// </summary>
        protected Vector2 Point(float angle, float radius) =>
            Vector2.Transform(new Vector2(0.5f + 0.5f * radius * MathF.Cos(angle), 0.5f + 0.5f * radius * MathF.Sin(angle)), ModelMatrix);

        protected Vector4 VertexColor(Color color, float alpha) => new Vector4(
            ColorExtensions.SrgbToLinear(color.R),
            ColorExtensions.SrgbToLinear(color.G),
            ColorExtensions.SrgbToLinear(color.B),
            Math.Clamp(DrawAlpha * alpha * (color.A / 255f), 0, 1));

        // Two triangles: p0 p1 p2 and p2 p3 p0.
        protected int Quad(int offset, in Vertex p0, in Vertex p1, in Vertex p2, in Vertex p3)
        {
            Vertices[offset + 0] = p0;
            Vertices[offset + 1] = p1;
            Vertices[offset + 2] = p2;
            Vertices[offset + 3] = p2;
            Vertices[offset + 4] = p3;
            Vertices[offset + 5] = p0;
            return offset + 6;
        }

        protected int Quad(int offset, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3) =>
            Quad(offset, new Vertex { Position = p0, Color = c0 }, new Vertex { Position = p1, Color = c1 }, new Vertex { Position = p2, Color = c2 },
                new Vertex { Position = p3, Color = c3 });

        /// <summary>
        /// Bounds holding everything out to <paramref name="radius"/> (a multiple of the disc's).
        /// </summary>
        protected void SetBounds(float radius)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            foreach (Vector2 corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
            {
                Vector2 p = Vector2.Transform(new Vector2(0.5f) + 0.5f * radius * corner, ModelMatrix);
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        protected void DrawNothing()
        {
            SetVertexCount(0);
            DrawRectangle = new RectangleF(0, 0, 0, 0);
        }
    }

    /// <summary>
    /// The disc itself, out to its moving edge, which fades over a screen pixel as a mask's does.
    /// </summary>
    private partial class Body : Layer
    {
        public Body(BubbleDisc disc)
            : base(disc)
        {
        }

        protected override void GenerateVertices()
        {
            if (Disc.DiscAlpha <= 0)
            {
                DrawNothing();
                return;
            }

            // A fan from the middle, then the fading edge round it.
            SetVertexCount(segments * 9);

            Vector4 solid = VertexColor(Disc.DiscColor, Disc.DiscAlpha);
            Vector4 clear = solid with { W = 0 };
            Vector2 middle = Point(0, 0);

            (Vector2 Inner, Vector2 Outer) at(int i)
            {
                Vector2 point = Point(MathF.Tau * i / segments, Disc.edge[i % segments]);
                Vector2 outward = point - middle;
                outward = outward.LengthSquared() > 0 ? Vector2.Normalize(outward) * edge_feather : Vector2.Zero;
                return (point - outward, point + outward);
            }

            int offset = 0;
            (Vector2 Inner, Vector2 Outer) previous = at(0);
            float largest = 0;

            for (int i = 1; i <= segments; i++)
            {
                (Vector2 Inner, Vector2 Outer) next = at(i);

                Vertices[offset++] = new Vertex { Position = middle, Color = solid };
                Vertices[offset++] = new Vertex { Position = previous.Inner, Color = solid };
                Vertices[offset++] = new Vertex { Position = next.Inner, Color = solid };
                offset = Quad(offset, previous.Inner, previous.Outer, next.Outer, next.Inner, solid, clear, clear, solid);

                largest = Math.Max(largest, Disc.edge[i % segments]);
                previous = next;
            }

            SetBounds(largest + 0.02f);
        }
    }

    /// <summary>
    /// One side of the film, added: the far side over the disc, or the near side over what's inside and out past it
    /// for the glow. Drawn by <c>bubble-back.frag</c> or <c>bubble-front.frag</c>, which color each pixel by where on
    /// the bubble it is.
    /// </summary>
    /// <remarks>
    /// Every vertex carries where it is on the bubble in its own shape (its texture coordinates: 1 at the edge, however
    /// the edge wobbles) and, the same on each, what a uniform would: the time, how bright, a wob's flash, the glow
    /// and the sheen (see the shader).
    /// </remarks>
    private partial class Membrane : Layer
    {
        private readonly string shader;

        public Membrane(BubbleDisc disc, bool front)
            : base(disc)
        {
            shader = front ? "bubble-front.frag" : "bubble-back.frag";
            Blending = BlendingMode.Additive;
        }

        protected override DrawNode CreateDrawNode() => new GameShaderDrawNode(shader);

        protected override void GenerateVertices()
        {
            BubbleDisc disc = Disc;

            if (DrawAlpha <= 0 || (disc.Sheen <= 0 && disc.Glow <= 0))
            {
                DrawNothing();
                return;
            }

            // A fan from the middle to the edge, then a band from the edge out to where the glow has faded.
            SetVertexCount(segments * 9);

            float time = (float)(disc.Clock.CurrentTime / 1000 % shader_time_wrap);
            var data = new Vector4(time, disc.Greyed ? -DrawAlpha : DrawAlpha, disc.flash, disc.Glow);

            Vertex vertex(float angle, float s, float r) => new Vertex
            {
                Position = Point(angle, r),
                TexCoords = new Vector2(s * MathF.Cos(angle), s * MathF.Sin(angle)),
                Color = data,
                TexIndex = disc.Sheen
            };

            Vertex middle = vertex(0, 0, 0);
            int offset = 0;
            float largest = 0;

            for (int i = 0; i < segments; i++)
            {
                float from = MathF.Tau * i / segments;
                float to = MathF.Tau * (i + 1) / segments;
                float fromEdge = disc.edge[i];
                float toEdge = disc.edge[(i + 1) % segments];

                Vertex fromInner = vertex(from, 1, fromEdge);
                Vertex toInner = vertex(to, 1, toEdge);

                Vertices[offset++] = middle;
                Vertices[offset++] = fromInner;
                Vertices[offset++] = toInner;
                offset = Quad(offset, fromInner, vertex(from, REACH, REACH * fromEdge), vertex(to, REACH, REACH * toEdge), toInner);

                largest = Math.Max(largest, fromEdge);
            }

            SetBounds(REACH * largest + 0.02f);
        }
    }

    /// <summary>
    /// The ripples, added: rings of light running out from the edge.
    /// </summary>
    private partial class RippleLight : Layer
    {
        public RippleLight(BubbleDisc disc)
            : base(disc)
        {
            Blending = BlendingMode.Additive;
        }

        protected override void GenerateVertices()
        {
            BubbleDisc disc = Disc;
            double now = disc.Clock.CurrentTime;

            // Counted here rather than taken from the disc's update: this can be built at another point in the frame.
            int ripples = 0;

            foreach (WobState wob in disc.wobs)
            {
                if (isRippling(wob, now))
                    ripples++;
            }

            if (ripples == 0)
            {
                DrawNothing();
                return;
            }

            SetVertexCount(ripples * segments * 12);
            int offset = 0;

            foreach (WobState wob in disc.wobs)
            {
                if (isRippling(wob, now))
                    offset = writeRipple(offset, wob, (float)(now - wob.Time));
            }

            SetBounds(1 + 0.03f + ripple_reach + 0.06f);
        }

        private bool isRippling(WobState wob, double now) => Disc.Ripples > 0 && now >= wob.Time && now - wob.Time < ripple_life;

        /// <summary>
        /// A ripple <paramref name="d"/> ms after its wob: a ring of light running out from the edge, its front sharp and
        /// its back soft, widening and fading as it goes. It starts in the edge's shape and grows round as it runs.
        /// </summary>
        private int writeRipple(int offset, WobState wob, float d)
        {
            BubbleDisc disc = Disc;
            float p = d / (float)ripple_life;
            float reach = 0.012f + ripple_reach * outCubic(p);
            float back = 0.012f + 0.045f * p;
            float front = 0.4f * back;
            float alpha = disc.Ripples * wob.Strength * 0.5f * Math.Min(1, d / 60) * (1 - p) * (1 - p);
            Vector4 color = VertexColor(disc.LightColor, alpha);
            Vector4 clear = color with { W = 0 };

            (Vector2 Back, Vector2 Middle, Vector2 Front) at(int i)
            {
                float angle = MathF.Tau * i / segments;
                float r = 1 + (disc.edge[i % segments] - 1) * (1 - p) + reach;
                return (Point(angle, r - back), Point(angle, r), Point(angle, r + front));
            }

            var previous = at(0);

            for (int i = 1; i <= segments; i++)
            {
                var next = at(i);
                offset = Quad(offset, previous.Back, previous.Middle, next.Middle, next.Back, clear, color, color, clear);
                offset = Quad(offset, previous.Middle, previous.Front, next.Front, next.Middle, color, clear, clear, color);
                previous = next;
            }

            return offset;
        }
    }
}
