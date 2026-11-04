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
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// A sakura twig round the blossom ring, wavering in and out a little as a real one would, grown clockwise from
/// 12 o'clock as far as <see cref="Progress"/>: the ring's fill draws it out. It's a dark stroke with a light edge on
/// top and a soft shadow under it, as the flowers on it have.
/// </summary>
/// <remarks>
/// <para>It's in a breeze: it sways a little all the time, and every few seconds a gust crosses it from one side, the
/// side it reaches first bowing in and the far side out, then springing back in a few dying wobbles, a glint of light
/// running along the bark with it. Whatever sits on it follows with <see cref="SwayAt"/> and <see cref="NodAt"/>.</para>
/// <para>It fills its parent and is drawn round its middle, in the parent's units: <see cref="RadiusAt"/> is where it is
/// at rest.</para>
/// </remarks>
public partial class SakuraBranch : Container
{
    /// <summary>
    /// The twig's mean distance from the middle.
    /// </summary>
    public const float RADIUS = 252;

    public const float THICKNESS = 6;

    private static readonly Color bark = ColorExtensions.FromHex("#6E5262");
    private static readonly Color bark_light = ColorExtensions.FromHex("#9A7C8C");
    private static readonly Color shadow = ColorExtensions.FromHex("#1B1622");

    /// <summary>
    /// A gust: how far it pushes the twig at full strength, in the parent's units; how long one wobble back and forth
    /// takes and how fast they die down, in ms; and how long it takes to cross from one side to the other.
    /// </summary>
    private const float gust_push = 6;

    private const double gust_wobble = 900;
    private const double gust_decay = 700;
    private const double gust_crossing = 1100;

    /// <summary>
    /// How long a gust is kept (by then it has crossed and settled), in ms.
    /// </summary>
    private const double gust_life = gust_crossing + 4000;

    /// <summary>
    /// How far a flower nods for each unit the twig under it is pushed, in degrees.
    /// </summary>
    private const float nod_per_unit = 1.2f;

    private readonly BranchStroke[] strokes;
    private readonly List<GustState> gusts = new List<GustState>();

    // Only where the gusts come from and how far apart they are; the same every time.
    private readonly Random random = new Random(5);

    private double nextGust = double.NaN;
    private double now;

    /// <summary>
    /// How strongly the breeze moves it, and the flowers on it: 0 keeps it still.
    /// </summary>
    public float Breeze { get; set; } = 1;

    /// <summary>
    /// About how long between gusts when nothing calls <see cref="Gust"/>, in ms (each a little more or less); 0 or less,
    /// none.
    /// </summary>
    public double GustInterval { get; set; } = 4200;

    /// <summary>
    /// How far round it has grown, 0–1 of a turn.
    /// </summary>
    public float Progress
    {
        get => strokes[0].Progress;
        set
        {
            foreach (BranchStroke stroke in strokes)
                stroke.Progress = value;
        }
    }

    public SakuraBranch()
    {
        RelativeSizeAxes = Axes.Both;

        Children = strokes = new[]
        {
            // The shadow, as the flowers' own: down and right, blurred.
            new BranchStroke(this) { Thickness = THICKNESS, Softness = 2.7f, Offset = new Vector2(1.2f, 2.6f), Color = shadow, Alpha = 0.38f },
            new BranchStroke(this) { Thickness = THICKNESS, Color = bark },
            // The light edge catches the light as a gust passes.
            new BranchStroke(this) { Thickness = 1.8f, Offset = new Vector2(-0.8f), Color = bark_light, Glints = true }
        };
    }

    /// <summary>
    /// A gust crosses the twig from <paramref name="time"/> (now if not given), from a side of its own. A time already
    /// past shows as it would be by now.
    /// </summary>
    /// <param name="time">When it reaches the twig, on this twig's clock.</param>
    /// <param name="strength">How hard: 1 is a good gust; past 1, harder.</param>
    public void Gust(double? time = null, float strength = 1)
    {
        double at = time ?? Clock.CurrentTime;
        gusts.Add(new GustState(at, strength, (float)(random.NextDouble() * MathF.Tau)));
        nextGust = Math.Max(double.IsNaN(nextGust) ? 0 : nextGust, at + nextInterval());
    }

    /// <summary>
    /// How far the breeze has moved the twig at <paramref name="turn"/> this frame, out from <see cref="RadiusAt"/>
    /// (in is negative), in the parent's units.
    /// </summary>
    public float SwayAt(float turn)
    {
        if (Breeze <= 0)
            return 0;

        float angle = MathF.Tau * turn;
        float seconds = (float)(now / 1000);

        // Never quite still: a slow sway along it, and a slower one the other way.
        float sway = 1.2f * MathF.Sin(2 * angle - 0.9f * seconds) + 0.7f * MathF.Sin(5 * angle + 1.3f * seconds);

        foreach (GustState gust in gusts)
        {
            // A point facing where the gust comes from is reached first and pushed in; the far side, last and out.
            float facing = MathF.Cos(angle - gust.From);
            double d = now - arrival(gust, facing);

            if (d <= 0)
                continue;

            float wobble = MathF.Sin((float)(MathF.Tau * d / gust_wobble)) * MathF.Exp((float)(-d / gust_decay));
            sway -= gust.Strength * gust_push * facing * wobble;
        }

        return Breeze * sway;
    }

    /// <summary>
    /// How far something sitting on the twig at <paramref name="turn"/> nods with it this frame, in degrees.
    /// </summary>
    public float NodAt(float turn) => nod_per_unit * SwayAt(turn);

    public override void Update()
    {
        now = Clock.CurrentTime;

        if (GustInterval > 0)
        {
            // The first gust by itself comes soon after it's shown, not a whole interval on.
            if (double.IsNaN(nextGust))
                nextGust = now + 1500;

            if (now >= nextGust)
                Gust(now, 0.6f + 0.6f * (float)random.NextDouble());
        }

        gusts.RemoveAll(g => now - g.Time > gust_life);

        if (Breeze > 0)
        {
            foreach (BranchStroke stroke in strokes)
                stroke.Invalidate(InvalidationFlags.DrawInfo);
        }

        base.Update();
    }

    private double nextInterval() => GustInterval * (0.75 + 0.5 * random.NextDouble());

    /// <summary>
    /// When a gust reaches the point facing it this much (1 straight at where it comes from, -1 away).
    /// </summary>
    private static double arrival(GustState gust, float facing) => gust.Time + (1 - facing) / 2 * gust_crossing;

    /// <summary>
    /// How much light is on the bark at <paramref name="turn"/>, 0–1: a glint running along with each gust's front.
    /// </summary>
    private float glintAt(float turn)
    {
        float angle = MathF.Tau * turn;
        float glint = 0;

        foreach (GustState gust in gusts)
        {
            double d = (now - arrival(gust, MathF.Cos(angle - gust.From))) / 160;
            glint += Math.Min(1, gust.Strength) * MathF.Exp((float)(-d * d));
        }

        return Math.Min(1, glint) * Math.Min(1, Breeze);
    }

    /// <param name="Time">When it reaches the twig.</param>
    /// <param name="Strength">How hard.</param>
    /// <param name="From">Which way it comes from, in radians clockwise from 12 o'clock.</param>
    private readonly record struct GustState(double Time, float Strength, float From);

    /// <summary>
    /// How far from the middle the twig is at <paramref name="turn"/> (0–1 clockwise from 12 o'clock): a few units in or
    /// out of <see cref="RADIUS"/>, in two waves that never quite line up.
    /// </summary>
    public static float RadiusAt(float turn)
    {
        float angle = MathF.Tau * turn;
        return RADIUS + 5 * MathF.Sin(3 * angle + 0.6f) + 2.5f * MathF.Sin(7 * angle);
    }

    /// <summary>
    /// One stroke of the twig: a band along <see cref="RadiusAt"/>, its edges feathered.
    /// </summary>
    private partial class BranchStroke : Drawable
    {
        private const int full_segments = 180;

        private readonly SakuraBranch branch;
        private float progress;

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

        public float Thickness { get; init; }

        /// <summary>
        /// How far its edges fade, in the parent's units; at 0 they fade over a screen pixel.
        /// </summary>
        public float Softness { get; init; }

        /// <summary>
        /// Where it's drawn from the twig's line, in the parent's units.
        /// </summary>
        public Vector2 Offset { get; init; }

        /// <summary>
        /// Whether it catches the light as a gust passes (the light edge).
        /// </summary>
        public bool Glints { get; init; }

        public BranchStroke(SakuraBranch branch)
        {
            this.branch = branch;
            RelativeSizeAxes = Axes.Both;
        }

        protected override VertexTopology Topology => VertexTopology.Triangles;

        // The color is baked into every vertex (as RingArc's is).
        protected override void UpdateDrawColor() => UpdateTransforms();

        protected override void GenerateVertices()
        {
            Vector2 size = AppliedDrawSize;
            int segments = (int)MathF.Ceiling(full_segments * progress);

            if (segments == 0 || size.X <= 0 || size.Y <= 0)
            {
                SetVertexCount(0);
                DrawRectangle = new RectangleF(0, 0, 0, 0);
                return;
            }

            SetVertexCount(segments * FeatheredStrip.VerticesPerPiece(true));

            var color = new Vector4(
                ColorExtensions.SrgbToLinear(Color.R),
                ColorExtensions.SrgbToLinear(Color.G),
                ColorExtensions.SrgbToLinear(Color.B),
                DrawAlpha * (Color.A / 255f));

            // A unit of the parent's on screen, for the softness.
            float unit = Vector2.TransformNormal(new Vector2(1 / size.X, 0), ModelMatrix).Length();
            float feather = Math.Max(FeatheredStrip.EDGE_FEATHER, Softness * unit);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            StripSection previous = default;
            Vector4 previousColor = color;
            int offset = 0;

            for (int i = 0; i <= segments; i++)
            {
                float turn = progress * i / segments;
                float angle = -MathF.PI / 2 + MathF.Tau * turn;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                float radius = RadiusAt(turn) + branch.SwayAt(turn);
                Vector4 sectionColor = Glints ? Vector4.Lerp(color, new Vector4(1, 1, 1, color.W), 0.6f * branch.glintAt(turn)) : color;

                Vector2 inner = toScreen(size, direction * (radius - Thickness / 2) + Offset);
                Vector2 outer = toScreen(size, direction * (radius + Thickness / 2) + Offset);
                var section = new StripSection(inner, outer, feather);

                minX = Math.Min(minX, Math.Min(section.AOuter.X, section.BOuter.X));
                minY = Math.Min(minY, Math.Min(section.AOuter.Y, section.BOuter.Y));
                maxX = Math.Max(maxX, Math.Max(section.AOuter.X, section.BOuter.X));
                maxY = Math.Max(maxY, Math.Max(section.AOuter.Y, section.BOuter.Y));

                if (i > 0)
                    offset = FeatheredStrip.Write(Vertices, offset, previous, section, previousColor, sectionColor, true);

                previous = section;
                previousColor = sectionColor;
            }

            DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        // From the middle, in the parent's units, to the screen.
        private Vector2 toScreen(Vector2 size, Vector2 fromMiddle) =>
            Vector2.Transform(new Vector2(0.5f + fromMiddle.X / size.X, 0.5f + fromMiddle.Y / size.Y), ModelMatrix);
    }
}
