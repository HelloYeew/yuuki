// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A small clock face: hour, minute and second hands that spring to each new second
/// </summary>
public partial class AnalogClockDisplay : ClockDisplay
{
    private const float hand_thickness = 2.4f;

    private readonly CircularContainer face;
    private readonly Hand hour;
    private readonly Hand minute;
    private readonly Hand second;
    private readonly Circle centreOuter;
    private readonly Circle centreInner;

    public AnalogClockDisplay()
    {
        Size = new Vector2(22);

        Children = new Drawable[]
        {
            face = new CircularContainer
            {
                RelativeSizeAxes = Axes.Both,
                BorderThickness = 2,
                BorderColor = Color.White,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    AlwaysPresent = true,
                    Alpha = 0
                }
            },
            hour = new Hand(0.34f, hand_thickness, hand_thickness / 2, outlined: true),
            minute = new Hand(0.48f, hand_thickness, hand_thickness / 2, outlined: true),
            second = new Hand(0.66f, hand_thickness * 0.7f, hand_thickness * 0.7f * 2, outlined: false),
            centreOuter = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(hand_thickness)
            },
            centreInner = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(hand_thickness * 0.7f)
            }
        };
    }

    public void SetColors(Color foreground, Color highlight, Color outline, double duration = 0)
    {
        // The ring fades with a transform of its own, so it's never a frame behind the hands, nor its
        // old color (white, at first) for a frame when the footer brings the clock back into view.
        face.FadeBorderColorTo(foreground, duration, Easing.OutQuint);
        hour.SetColors(foreground, outline, duration);
        minute.SetColors(foreground, outline, duration);
        second.SetColors(highlight, outline, duration);
        centreOuter.FadeToColor(foreground, duration, Easing.OutQuint);
        centreInner.FadeToColor(highlight, duration, Easing.OutQuint);
    }

    protected override void UpdateDisplay(DateTimeOffset now)
    {
        float secondFractional = now.Second / 60f;
        float minuteFractional = (now.Minute + secondFractional) / 60f;
        float hourFractional = ((minuteFractional + now.Hour) % 12) / 12f;

        updateRotation(hour, hourFractional);
        updateRotation(minute, minuteFractional);
        updateRotation(second, secondFractional);
    }

    private static void updateRotation(Drawable hand, float fraction)
    {
        const float duration = 320;

        float rotation = fraction * 360 - 90;

        // A hand completing a turn: animate on past 12 o'clock, then step back a full turn so the
        // rotation stays within 0–360.
        if (Math.Abs(hand.Rotation - rotation) > 180)
        {
            float animRotation = rotation;
            while (animRotation < hand.Rotation)
                animRotation += 360;

            hand.RotateTo(animRotation, duration, Easing.OutElastic)
                .Then()
                .RotateTo(rotation);
        }
        else
            hand.RotateTo(rotation, duration, Easing.OutElastic);
    }

    private partial class Hand : Container
    {
        private const float outline_thickness = 0.7f;

        private readonly Capsule? outline;
        private readonly Capsule fill;

        public Hand(float length, float thickness, float tail, bool outlined)
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            RelativeSizeAxes = Axes.Both;

            if (outlined)
            {
                Add(outline = new Capsule
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Width = length,
                    Height = thickness,
                    X = -tail
                });
            }

            float inset = outlined ? outline_thickness : 0;

            Add(fill = new Capsule
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                Width = length,
                Height = thickness - inset * 2,
                X = -tail,
                Margin = new MarginPadding { Left = inset, Right = inset }
            });
        }

        public void SetColors(Color color, Color outlineColor, double duration)
        {
            fill.FadeToColor(color, duration, Easing.OutQuint);
            outline?.FadeToColor(outlineColor, duration, Easing.OutQuint);
        }
    }

    /// <summary>
    /// A bar along this drawable's width with round ends, as thick as it is tall. It's drawn as triangles
    /// with a feathered edge a pixel wide: the framework smooths only the edges of masks, and a mask
    /// doesn't turn with its drawable, so a rounded, masked bar would lose its shape as the hand turned.
    /// </summary>
    private partial class Capsule : Drawable
    {
        /// <summary>
        /// Points around each round end.
        /// </summary>
        private const int cap_segments = 8;

        private const int outline_points = (cap_segments + 1) * 2;

        /// <summary>
        /// A fan over the solid part, then a strip around it for the feathered edge.
        /// </summary>
        private const int vertex_count = outline_points * 3 + outline_points * 6;

        private readonly Vector2[] inner = new Vector2[outline_points];
        private readonly Vector2[] outer = new Vector2[outline_points];

        protected override VertexTopology Topology => VertexTopology.Triangles;

        protected override void UpdateDrawColor() => UpdateTransforms();

        protected override void GenerateVertices()
        {
            float w = AppliedDrawSize.X;
            float h = AppliedDrawSize.Y;

            if (w <= 0 || h <= 0)
            {
                SetVertexCount(0);
                DrawRectangle = new RectangleF(0, 0, 0, 0);
                return;
            }

            SetVertexCount(vertex_count);

            // Worked in screen space, where the feather is measured: the centers of the two round ends,
            // half a thickness in from each end, and the radius on screen.
            float r = Math.Min(w, h) / 2;
            Vector2 start = Vector2.Transform(new Vector2(r / w, 0.5f), ModelMatrix);
            Vector2 end = Vector2.Transform(new Vector2(1 - r / w, 0.5f), ModelMatrix);
            float radius = Vector2.Distance(Vector2.Transform(Vector2.Zero, ModelMatrix), Vector2.Transform(new Vector2(0, 1), ModelMatrix)) / 2;

            Vector2 axis = Vector2.Transform(new Vector2(1, 0.5f), ModelMatrix) - Vector2.Transform(new Vector2(0, 0.5f), ModelMatrix);
            Vector2 along = axis.Length() > 0 ? Vector2.Normalize(axis) : new Vector2(1, 0);
            Vector2 across = new Vector2(-along.Y, along.X);

            float innerRadius = Math.Max(radius - 0.5f, 0);
            float outerRadius = radius + 0.5f;

            // Round the far end from one side to the other, then the near end back again.
            for (int i = 0; i <= cap_segments; i++)
            {
                float angle = -MathF.PI / 2 + MathF.PI * i / cap_segments;
                Vector2 direction = along * MathF.Cos(angle) + across * MathF.Sin(angle);

                inner[i] = end + direction * innerRadius;
                outer[i] = end + direction * outerRadius;
                inner[cap_segments + 1 + i] = start - direction * innerRadius;
                outer[cap_segments + 1 + i] = start - direction * outerRadius;
            }

            var solid = new Vector4(
                ColorExtensions.SrgbToLinear(Color.R),
                ColorExtensions.SrgbToLinear(Color.G),
                ColorExtensions.SrgbToLinear(Color.B),
                DrawAlpha * (Color.A / 255f));
            var clear = solid with { W = 0 };

            Vector2 centre = (start + end) / 2;
            int v = 0;

            for (int i = 0; i < outline_points; i++)
            {
                int next = (i + 1) % outline_points;

                Vertices[v++] = vertex(centre, solid);
                Vertices[v++] = vertex(inner[i], solid);
                Vertices[v++] = vertex(inner[next], solid);
            }

            for (int i = 0; i < outline_points; i++)
            {
                int next = (i + 1) % outline_points;

                Vertices[v++] = vertex(inner[i], solid);
                Vertices[v++] = vertex(outer[i], clear);
                Vertices[v++] = vertex(outer[next], clear);
                Vertices[v++] = vertex(outer[next], clear);
                Vertices[v++] = vertex(inner[next], solid);
                Vertices[v++] = vertex(inner[i], solid);
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            foreach (Vector2 point in outer)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }

            DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        private static Vertex vertex(Vector2 position, Vector4 color) => new Vertex { Position = position, TexCoords = Vector2.Zero, Color = color };
    }
}
