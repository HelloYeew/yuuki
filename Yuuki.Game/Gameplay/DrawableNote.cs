// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Pooling;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// A pooled, clock-driven note projected onto the perspective highway. Every frame it recomputes its
/// depth from the gameplay clock and asks the shared <see cref="LanePerspective"/> where to sit and
/// how big to be, so it slides down the lane and grows as it nears the judgment line — and stays
/// glued to the music through rate changes, seeks, and pauses.
/// <para>
/// The note itself fills the playfield and never moves; its pieces (head, hold body, tail marker) are
/// placed inside it in the playfield's pixel space. Only children are moved, and children update after
/// their parent, so everything is drawn where this frame put it. (A note that moved itself in
/// <see cref="Update"/> was drawn a frame late, its body a frame ahead of its head, and a reused one
/// flashed for a frame where its last note had been.)
/// </para>
/// </summary>
public partial class DrawableNote : PoolableDrawable
{
    /// <summary>
    /// The note bar's thickness at the judgment line (the receptors share it).
    /// </summary>
    public const float NOTE_HEIGHT = 48;

    /// <summary>
    /// Depth window over which a note fades in as it emerges from the far horizon.
    /// </summary>
    private const float fade_in_range = 0.2f;

    /// <summary>
    /// Depth past the judgment line over which a note fades out. Depth rather than time, so it's the
    /// same stretch of screen at any scroll speed, just below the line: nothing lingers under it.
    /// </summary>
    private const float fade_out_range = 0.05f;

    /// <summary>
    /// The hold body's width relative to the note head's width.
    /// </summary>
    private const float body_width_factor = 0.55f;

    private const float body_rest_alpha = 0.55f;
    private const float body_held_alpha = 0.9f;

    /// <summary>
    /// How far up the body (as depth) a held's glow reaches from its head.
    /// </summary>
    private const float glow_depth = 0.04f;

    /// <summary>
    /// The tail marker's size relative to a note bar at the same depth.
    /// </summary>
    private const float tail_width_factor = 0.8f;

    private const float tail_height_factor = 0.4f;

    private const double hit_duration = 120;

    // Time constants (ms) of a hold's light coming on and going off, and of a slide tick's flash.
    private const double glow_rise_time = 25;
    private const double glow_fall_time = 90;
    private const double tick_decay_time = 70;

    /// <summary>
    /// Arrows spread along a swipe bar, showing which swipe input it takes.
    /// </summary>
    private const int swipe_arrow_count = 3;

    private const float swipe_arrow_size = NOTE_HEIGHT * 0.85f;

    /// <summary>
    /// Touch notes are thin bars: you don't hit them, you have the lane held as they pass.
    /// </summary>
    private const float touch_height_factor = 0.45f;

    // The bar, ring and arrows, sized and placed as one; hit pops scale it.
    private readonly Container headPiece;
    private readonly Circle bar;
    private readonly HoldNoteBody body;
    private readonly Circle tailCap;
    private readonly IconSprite[] swipeArrows = new IconSprite[swipe_arrow_count];

    // A Pass note is a hollow ring in the lane (nothing to press) rather than a solid bar.
    private readonly RingArc passRing;

    // Reused every frame to avoid per-note allocations.
    private readonly HoldBodySample[] bodySamples = new HoldBodySample[HoldNoteBody.MAX_SAMPLES];
    private readonly double[] sampleDepths = new double[HoldNoteBody.MAX_SAMPLES];

    private NoteLayout layout;
    private Color noteColor;

    public BeatmapNote HitObject { get; private set; } = null!;

    // The gameplay time the head shows: the note's start, or (pinned) the current time once that's
    // later, which keeps it on the judgement line. Unpinned again, it keeps the moment it was let go
    // and scrolls on from the line instead of jumping to where its start time would be.
    private double headTime;
    private bool headPinned;

    // True while a hold is actively being held.
    private bool holding;

    // How lit a hold is: 1 while held, easing back to 0 once let go. tickGlow flashes on slide ticks.
    private float heldGlow;
    private float tickGlow;

    // When the hit animation started (NaN if it hasn't): a hold's leftover body is pulled into its head.
    private double hitTime = double.NaN;

    public DrawableNote()
    {
        RelativeSizeAxes = Axes.Both;

        Children = new Drawable[]
        {
            // The hold ribbon, following the lane from head to tail. Hidden for non-hold notes.
            body = new HoldNoteBody
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0
            },
            // Where a hold ends, so the release can be read coming.
            tailCap = new Circle
            {
                Origin = Anchor.Centre,
                Alpha = 0
            },
            headPiece = new Container
            {
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    bar = new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both
                    },
                    passRing = new RingArc
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        InnerRadius = 0.72f,
                        Alpha = 0
                    }
                }
            }
        };

        for (int i = 0; i < swipe_arrow_count; i++)
        {
            headPiece.Add(swipeArrows[i] = new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.X,
                X = (i + 1f) / (swipe_arrow_count + 1),
                IconSize = swipe_arrow_size,
                Color = Color.White,
                Alpha = 0
            });
        }
    }

    public void Apply(BeatmapNote note, NoteLayout noteLayout, Color color)
    {
        HitObject = note;
        layout = noteLayout;
        noteColor = color;

        headTime = note.StartTime;
        headPinned = false;
        holding = false;
        heldGlow = 0;
        tickGlow = 0;
        hitTime = double.NaN;

        // Pooled: drop whatever the note this drawable last showed left running.
        ClearTransforms();
        Alpha = 1;
        headPiece.ClearTransforms();
        headPiece.Scale = Vector2.One;
        bar.ClearTransforms();

        bool isHold = note.Type == NoteType.Hold;

        bar.Color = color;
        bar.Alpha = note.Type == NoteType.Pass ? 0 : 1;
        passRing.Color = color;
        passRing.Alpha = note.Type == NoteType.Pass ? 1 : 0;
        body.Color = color;
        body.Alpha = isHold ? body_rest_alpha : 0;
        body.ClearGeometry();
        tailCap.Color = color;
        tailCap.Alpha = 0;

        bool isSwipe = note.Type == NoteType.Swipe;
        IconUsage arrow = note.Direction switch
        {
            SwipeDirection.Left => IconUsage.KeyboardDoubleArrowLeft,
            SwipeDirection.Right => IconUsage.KeyboardDoubleArrowRight,
            _ => IconUsage.SwapHoriz
        };

        for (int i = 0; i < swipeArrows.Length; i++)
        {
            IconSprite icon = swipeArrows[i];
            icon.Alpha = isSwipe ? 1 : 0;
            if (isSwipe)
                icon.Icon = arrow;
        }

        // A skill note carries its skill's symbol in the middle of the bar.
        if (note.Type == NoteType.Skill)
        {
            IconSprite middle = swipeArrows[swipe_arrow_count / 2];
            middle.Icon = note.Skill == SkillType.Heal ? IconUsage.Favorite : IconUsage.AutoAwesome;
            middle.Alpha = 1;
        }
    }

    public override void Update()
    {
        base.Update();

        LanePerspective perspective = layout.Perspective;
        double time = Clock.CurrentTime;
        double preempt = layout.Preempt;

        if (headPinned)
            headTime = Math.Max(headTime, time);
        else if (time < headTime)
            headTime = Math.Max(HitObject.StartTime, time); // rewound past where it was let go

        double headZ = (headTime - time) / preempt;
        float persp = perspective.Perspective(headZ);
        float heightFactor = HitObject.Type == NoteType.Touch ? touch_height_factor : 1;

        // A lane-changing hold's head follows its path; everything else sits on its start lane.
        headPiece.Position = perspective.Project(headZ, laneOffsetAt(headTime));
        headPiece.Size = new Vector2(layout.NoteWidth * persp, NOTE_HEIGHT * persp * heightFactor);
        // Fades in as it emerges from the far vanishing point (z ≈ 1) so it doesn't pop in at the
        // horizon, and out just past the line. Hits and misses fade the whole note on top of this.
        headPiece.Alpha = depthAlpha(headZ);

        if (HitObject.Type == NoteType.Pass)
            passRing.Size = new Vector2(NOTE_HEIGHT * persp * 1.6f);

        // The icons are sized for a note at the judgement line; shrink them with the bar.
        if (HitObject.Type is NoteType.Swipe or NoteType.Skill)
        {
            foreach (IconSprite swipeArrow in swipeArrows)
                swipeArrow.Scale = new Vector2(persp);
        }

        if (HitObject.Type == NoteType.Hold)
            updateHold(perspective, time, preempt, headZ);
    }

    private void updateHold(LanePerspective perspective, double time, double preempt, double headZ)
    {
        // A seek or rate change can make this negative or huge; either way the light just settles.
        double elapsed = Math.Abs(Clock.ElapsedFrameTime);
        heldGlow = approach(heldGlow, holding ? 1 : 0, elapsed, holding ? glow_rise_time : glow_fall_time);
        tickGlow = approach(tickGlow, 0, elapsed, tick_decay_time);
        body.Alpha = body_rest_alpha + (body_held_alpha - body_rest_alpha) * heldGlow;

        double tailZ = (HitObject.EndTime - time) / preempt;

        // A hit release pulls whatever's left of the body down into the head as it pops.
        if (!double.IsNaN(hitTime))
        {
            double remaining = 1 - Math.Clamp((time - hitTime) / hit_duration, 0, 1);
            tailZ = headZ + (tailZ - headZ) * remaining * remaining * remaining;
        }

        updateTailCap(perspective, time, preempt, headZ, tailZ);
        updateBody(perspective, time, preempt, headZ, Math.Min(1, tailZ));
    }

    private void updateTailCap(LanePerspective perspective, double time, double preempt, double headZ, double tailZ)
    {
        // Gone once it reaches the head: a held hold's tail runs into the line, a hit one into the pop.
        if (tailZ <= headZ || tailZ >= 1)
        {
            tailCap.Alpha = 0;
            return;
        }

        float persp = perspective.Perspective(tailZ);
        tailCap.Position = perspective.Project(tailZ, laneOffsetAt(time + tailZ * preempt));
        tailCap.Size = new Vector2(layout.NoteWidth * persp * tail_width_factor, NOTE_HEIGHT * persp * tail_height_factor);
        tailCap.Alpha = depthAlpha(tailZ);
    }

    /// <summary>
    /// Rebuilds the hold ribbon from the head (or the point past the line where it has faded out) to
    /// the tail, clamped to the horizon (z = 1) so the ribbon emerges from the far point rather than
    /// projecting past the vanishing point.
    /// <para>
    /// On this projection the lane center-line is straight between depths: screen X and Y are both
    /// linear in the perspective factor, and so is a lane change's offset. So samples go only where
    /// the ribbon's color changes (the glow's reach, the fade-in at the horizon, the line), not
    /// along a curve. Past the line the projection follows its tangent, where a lane change bends a
    /// little; two more samples cover that.
    /// </para>
    /// </summary>
    private void updateBody(LanePerspective perspective, double time, double preempt, double headZ, double tailZ)
    {
        double fromZ = Math.Max(headZ, -fade_out_range);

        if (tailZ <= fromZ)
        {
            body.ClearGeometry();
            return;
        }

        float glow = 0.35f * heldGlow + 0.3f * tickGlow;

        int depthCount = 0;
        sampleDepths[depthCount++] = fromZ;
        sampleDepths[depthCount++] = tailZ;

        if (fromZ < 0 && tailZ > 0)
        {
            sampleDepths[depthCount++] = 0;

            if (layout.EndLaneOffset != layout.LaneOffset)
            {
                sampleDepths[depthCount++] = fromZ * 2 / 3;
                sampleDepths[depthCount++] = fromZ / 3;
            }
        }

        if (glow > 0)
            addDepthWithin(ref depthCount, headZ + glow_depth, fromZ, tailZ);

        addDepthWithin(ref depthCount, 1 - fade_in_range, fromZ, tailZ);

        Span<double> depths = sampleDepths.AsSpan(0, depthCount);
        depths.Sort();

        int count = 0;

        foreach (double z in depths)
        {
            if (count > 0 && z - sampleDepths[count - 1] < 1e-6)
                continue;

            sampleDepths[count] = z;

            float bodyGlow = glow * Math.Clamp(1 - (float)((z - headZ) / glow_depth), 0, 1);
            bodySamples[count++] = new HoldBodySample(
                perspective.Project(z, laneOffsetAt(time + z * preempt)),
                layout.NoteWidth * perspective.Perspective(z) * body_width_factor / 2,
                depthAlpha(z),
                bodyGlow);
        }

        body.SetGeometry(bodySamples.AsSpan(0, count));
    }

    private void addDepthWithin(ref int count, double z, double from, double to)
    {
        if (z > from && z < to)
            sampleDepths[count++] = z;
    }

    /// <summary>
    /// The lane offset along this note's path at gameplay <paramref name="time"/>: its lane, or for a
    /// lane-changing hold, partway from its start lane to its end lane.
    /// </summary>
    private float laneOffsetAt(double time)
    {
        if (layout.EndLaneOffset == layout.LaneOffset || HitObject.Duration <= 0)
            return layout.LaneOffset;

        float progress = (float)Math.Clamp((time - HitObject.StartTime) / HitObject.Duration, 0, 1);
        return layout.LaneOffset + (layout.EndLaneOffset - layout.LaneOffset) * progress;
    }

    /// <summary>How visible a piece at depth <paramref name="z"/> is: fading in at the horizon, out past the line.</summary>
    private static float depthAlpha(double z) => (float)(z >= 0
        ? Math.Clamp((1 - z) / fade_in_range, 0, 1)
        : Math.Clamp(1 + z / fade_out_range, 0, 1));

    /// <summary>Eases <paramref name="value"/> toward <paramref name="target"/>, frame-rate independent, landing exactly.</summary>
    private static float approach(float value, float target, double elapsed, double timeConstant)
    {
        float next = target + (value - target) * (float)Math.Exp(-elapsed / timeConstant);
        return Math.Abs(next - target) < 0.001f ? target : next;
    }

    /// <summary>
    /// This hold is being held: its head sits on the judgement line and the body lights up. Taking
    /// hold (the head, or the body again after dropping it) lands the head on the line with a pop,
    /// and lights a dimmed hold back up.
    /// </summary>
    public void Grab(bool animate = true)
    {
        if (holding)
            return;

        holding = true;
        headPinned = true;

        ClearTransforms();

        if (!animate)
        {
            Alpha = 1;
            return;
        }

        this.FadeTo(1, 80, Easing.OutQuint);
        popHead(1.15f, 200);

        bar.ClearTransforms();
        bar.Color = noteColor;
        bar.FlashColor(Color.White, 200, Easing.OutQuint);
    }

    /// <summary>
    /// This hold was let go at gameplay <paramref name="time"/>: the head leaves the line from that
    /// moment and scrolls on, and the light fades.
    /// </summary>
    public void LetGo(double time)
    {
        if (!holding)
            return;

        holding = false;
        headPinned = false;
        headTime = Math.Max(headTime, time);
    }

    /// <summary>A slide tick while held: the head and the near end of the body flash in time.</summary>
    public void PulseHold()
    {
        tickGlow = 1;
        popHead(1.06f, 120);
    }

    private void popHead(float scale, double duration)
    {
        // Cleared first: a new scale transform would otherwise take over the running one's timeline.
        headPiece.ClearTransforms();
        headPiece.Scale = new Vector2(scale);
        headPiece.ScaleTo(1f, duration, Easing.OutQuint);
    }

    /// <summary>
    /// The note lands: the head stops on the judgement line (one hit early glides on to it first),
    /// pops and fades, and what's left of a hold's body is pulled into it.
    /// </summary>
    public void AnimateHit()
    {
        headPinned = true;
        hitTime = Clock.CurrentTime;

        ClearTransforms();
        this.FadeOut(hit_duration, Easing.OutQuint);

        headPiece.ClearTransforms();
        headPiece.ScaleTo(new Vector2(1.3f), hit_duration, Easing.OutQuint);
    }

    public void AnimateMiss()
    {
        ClearTransforms();
        this.FadeTo(0.25f, 160, Easing.OutQuint);
    }
}
