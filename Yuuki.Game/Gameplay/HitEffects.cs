// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// The hit burst effect at a receptor
/// </summary>
public partial class HitBurst : Container
{
    private const double duration = 380;
    private const int spark_count = 10;

    private readonly Color color;
    private readonly bool small;
    private readonly bool critical;

    /// <summary>
    /// Whether the animation has played out and the burst can be removed.
    /// </summary>
    public bool IsFinished { get; private set; }

    /// <param name="color">The judgement's colour.</param>
    /// <param name="size">The burst's diameter before it expands.</param>
    /// <param name="critical">Adds the cross flare reserved for Criticals.</param>
    /// <param name="small">The hold-tick sparkle: no sparks or flare, smaller and fainter.</param>
    public HitBurst(Color color, float size, bool critical = false, bool small = false)
    {
        // TODO: pooling?
        this.color = color;
        this.small = small;
        this.critical = critical && !small;

        Origin = Anchor.Centre;
        Size = new Vector2(small ? size * 0.55f : size);
    }

    /// <summary>A hold-tick sparkle.</summary>
    public static HitBurst Small(Color color, float size) => new HitBurst(color, size, small: true);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        var glow = new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = color,
            Blending = BlendingMode.Additive,
            Alpha = small ? 0.35f : 0.6f,
            Scale = new Vector2(0.6f)
        };

        var core = new Circle
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.5f),
            Color = Color.White,
            Blending = BlendingMode.Additive,
            Alpha = small ? 0.5f : 1f,
            Scale = new Vector2(0.4f)
        };

        var ring = new RingArc
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            InnerRadius = 0.88f,
            Color = color,
            Blending = BlendingMode.Additive,
            Scale = new Vector2(0.6f)
        };

        AddRange(new Drawable[] { glow, ring, core });

        glow.ScaleTo(small ? 1.4f : 2.1f, duration, Easing.OutQuint).FadeOut(duration, Easing.OutQuint);
        core.ScaleTo(small ? 1f : 1.5f, duration * 0.4, Easing.OutQuint).FadeOut(duration * 0.45, Easing.OutQuint);
        ring.ScaleTo(small ? 1.7f : 2.7f, duration, Easing.OutQuint).FadeOut(duration, Easing.OutQuint);

        if (!small)
            addSparks();

        if (critical)
            addFlare();

        Scheduler.AddDelayed(() => IsFinished = true, duration);
    }

    /// <summary>
    /// Sparks flung out radially, alternating white and the judgment's color.
    /// </summary>
    private void addSparks()
    {
        float reach = Width * 1.2f;

        for (int i = 0; i < spark_count; i++)
        {
            // Evenly spread, with an alternating nudge so the pattern doesn't look stamped.
            float angle = MathF.Tau * i / spark_count + (i % 2 == 0 ? 0.12f : -0.08f);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            float distance = reach * (i % 3 == 0 ? 1f : 0.75f);

            var spark = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(Width * 0.22f, 4),
                Rotation = angle * 180 / MathF.PI,
                Position = direction * Width * 0.15f,
                Color = i % 2 == 0 ? Color.White : color,
                Blending = BlendingMode.Additive
            };

            Add(spark);
            spark.MoveTo(direction * distance, duration, Easing.OutQuint);
            spark.ScaleTo(new Vector2(0.3f, 1), duration, Easing.OutQuint);
            spark.FadeOut(duration, Easing.InQuad);
        }
    }

    /// <summary>
    /// A four-pointed cross-flare across the hit, for Criticals only.
    /// </summary>
    private void addFlare()
    {
        foreach (bool horizontal in new[] { true, false })
        {
            var beam = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = horizontal ? new Vector2(Width * 3.2f, 5) : new Vector2(5, Width * 2.2f),
                Color = Color.White,
                Blending = BlendingMode.Additive,
                Scale = horizontal ? new Vector2(0.2f, 1) : new Vector2(1, 0.2f)
            };

            Add(beam);
            beam.ScaleTo(Vector2.One, duration * 0.5, Easing.OutQuint);
            beam.FadeOut(duration * 0.7, Easing.OutQuad);
        }
    }
}

/// <summary>
/// The judgement word above the judgement line — CRITICAL / BREAK / HIT / MISS — with EARLY or LATE
/// under the near-misses. Each new judgement replaces the last with a pop, then it drifts and fades.
/// The owner places this drawable; only its inner layer moves, so the placement is never lost.
/// </summary>
public partial class JudgementPopup : Container
{
    private readonly Container body;
    private readonly SpriteText word;
    private readonly SpriteText timing;

    // Bumped per judgement so a newer one cancels the previous one's pending drift-out.
    private int showVersion;

    public JudgementPopup()
    {
        Origin = Anchor.Centre;
        AutoSizeAxes = Axes.Both;

        // EARLY/LATE centred under the word, tucked slightly under it.
        Child = body = new FlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, -timing_overlap),
            Alpha = 0,
            Children = new Drawable[]
            {
                word = new SpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 48, FontWeight.Bold)
                },
                timing = new SpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold)
                }
            }
        };
    }

    /// <summary>
    /// How far EARLY/LATE tucks up under the word.
    /// </summary>
    private const float timing_overlap = 4;

    public void Show(JudgementResult judgement)
    {
        Color color = JudgementColors.For(judgement.Result);

        word.Text = judgement.Result.ToString().ToUpperInvariant();
        word.Color = color;

        // Early/late only where it's useful: a Critical is on time, a Miss never had a press to time,
        // and Pass/Touch notes aren't pressed at all.
        bool showTiming = judgement.Result is HitResult.Break or HitResult.Hit
                          && judgement.Note.Type is not (Beatmaps.NoteType.Pass or Beatmaps.NoteType.Touch);
        timing.Text = showTiming ? (judgement.TimingError < 0 ? "EARLY" : "LATE") : "";
        timing.Color = color;
        // Out of the flow when there's nothing to say, so the word alone sets the size.
        timing.Alpha = showTiming ? 1 : 0;

        body.ClearTransforms();
        body.Alpha = 1;
        body.Y = 0;
        body.Scale = new Vector2(1.4f);
        body.ScaleTo(1f, 300, UserInterfaceConstant.SoftBounce);

        int version = ++showVersion;
        Scheduler.AddDelayed(() =>
        {
            if (version != showVersion)
                return;

            body.MoveToY(-14, 250, Easing.OutQuint);
            body.FadeOut(250, Easing.OutQuint);
        }, 350);
    }

    /// <summary>
    /// Clears whatever is showing (e.g. on restart, when the clock it runs on jumps back).
    /// </summary>
    public void ClearDisplay()
    {
        showVersion++;
        body.ClearTransforms();
        body.Alpha = 0;
        body.Y = 0;
    }
}
