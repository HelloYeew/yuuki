// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Containers;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// One item drawn in a <see cref="CarouselFocusWindow{TModel}"/>'s large layout.
/// </summary>
public abstract partial class CarouselFocusFace<TModel> : Container
    where TModel : class
{
    public TModel? Model { get; private set; }

    protected CarouselFocusFace()
    {
        RelativeSizeAxes = Axes.Both;
    }

    public void Apply(TModel model)
    {
        Model = model;

        // A list filled during its owner's load is laid out (and its window shown) before it has loaded
        // itself. Until this face loads, nothing it resolves is there yet, and its cover can't start
        // loading, so it fills itself in on load instead.
        if (IsLoaded)
            OnApply(model);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (Model != null)
            OnApply(Model);
    }

    protected abstract void OnApply(TModel model);

    /// <summary>
    /// Reshapes the face for the carousel's orientation: a wide card in the rail, a hero tile in the strip.
    /// </summary>
    public virtual void SetOrientation(CarouselOrientation orientation)
    {
    }

    /// <summary>
    /// Reprints the face in its model's current colours, which may have become known after it was applied.
    /// </summary>
    public virtual void RefreshColors()
    {
    }

    /// <summary>
    /// Compacts the face as the list skims: <paramref name="amount"/> is 0 at rest, 1 skimming. Called
    /// every frame the window lays out; a face that compacts keeps the last amount and does nothing when
    /// it hasn't changed.
    /// </summary>
    public virtual void SetSkim(float amount)
    {
    }

    /// <summary>
    /// Sizes the face for a phone (bigger, for reading at arm's length) or the desktop.
    /// </summary>
    public virtual void SetPhone(bool phone)
    {
    }
}

/// <summary>
/// The fixed window on a carousel's cue, and the only solid thing in the list. The cards slide behind it,
/// and it shows whichever item is at the scroll position in a large layout. Between two items it doesn't
/// grow or slide continuously: it holds still until the list is halfway to the next one, then clicks over.
/// The item shown slides out and is all but gone before the next slides in from the side the list is
/// coming from, so a fast scroll reads as a flip counter and the window never shows two items at once. The
/// items either side are made ready out of sight, so their covers are in by the time they come in. When
/// the list comes to rest on an item, a streak of light crosses the window at the blade's angle: a wink.
/// And while it shows what's playing, it flashes on the beat.
/// </summary>
public abstract partial class CarouselFocusWindow<TModel> : Container
    where TModel : class
{
    /// <summary>
    /// How far (as a share of the window's length on the scroll axis) a face slides as it comes in, and as
    /// it leaves.
    /// </summary>
    private const float enter_travel = 0.3f;

    private const float leave_travel = 0.18f;

    /// <summary>
    /// How long (ms) a face takes to leave and to come in, and how long after the one leaving starts the
    /// next comes in: by then the one leaving is nearly gone.
    /// </summary>
    private const double leave_duration = 110;

    private const double enter_duration = 280;
    private const double enter_delay = 50;

    /// <summary>
    /// A swap coming sooner than this (ms) after the last is quicker, in proportion, so the window keeps up
    /// with the list: clicked through quickly, it clicks over quickly.
    /// </summary>
    private const double relaxed_swap_interval = 400;

    /// <summary>
    /// A swap this soon (ms) after the last cuts straight to the next item: the list is flying past (a long
    /// jump, a fling), faster than any face could come in, and the window shows each item it passes rather
    /// than faces that never got the time to appear.
    /// </summary>
    private const double cut_swap_interval = 60;

    private const double fill_fade_duration = 180;

    /// <summary>
    /// How bright the window flashes on each beat while it shows what's playing.
    /// </summary>
    private const float beat_flash_alpha = 0.3f;

    private const float sweep_width = 90;

    /// <summary>
    /// How white the streak is at its brightest, down the middle.
    /// </summary>
    private const float sweep_peak = 0.5f;
    private const double sweep_duration = 420;

    /// <summary>
    /// Quick to cross and slow to leave, so the streak lands on the item and then lingers off its far edge.
    /// </summary>
    private static readonly EasingFunction sweep_easing = new EasingFunction(new CubicBezierEasing(0.2, 0.6, 0.2, 1));

    // The visible card. The window itself carries the entrance and exit transforms, so the per-frame
    // work below never touches its alpha.
    private readonly Container body;
    private readonly Box fill;
    private readonly Container faceLayer;
    private readonly Box beatFlash;
    private readonly Container sweep;

    private readonly List<LiveFace> live = new List<LiveFace>();

    // Faces not in use. Each still holds the item it last showed (and its cover), for when that comes back.
    private readonly List<CarouselFocusFace<TModel>> spare = new List<CarouselFocusFace<TModel>>();

    private LiveFace? shown;

    private double lastSwap = double.MinValue;

    private CarouselOrientation orientation = CarouselOrientation.Horizontal;
    private TModel? colouredFor;
    private Color fillColor = Color.Gray;

    // The scroll position last frame, to tell a list at rest from one passing through an item, and the
    // item the window last winked at.
    private double lastPosition = double.NaN;
    private TModel? sweptFor;

    protected CarouselFocusWindow()
    {
        RelativeSizeAxes = Axes.Both;

        Child = body = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = Color.Black.WithAlpha(0.35f),
                Radius = 18f,
                Offset = new Vector2(0f, 10f)
            },
            Children = new Drawable[]
            {
                fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Gray
                },
                faceLayer = new Container
                {
                    RelativeSizeAxes = Axes.Both
                },
                // Washes over the face, cover and all, on each beat (see FlashesOnBeat).
                beatFlash = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.White,
                    Alpha = 0,
                    Blending = BlendingMode.Additive
                },
                // Taller than the window, so its slanted ends are cut off by the window's edges.
                sweep = new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Height = 1.2f,
                    Width = sweep_width,
                    Shear = UserInterfaceConstant.Shear,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.5f,
                            ColorInfo = ColorInfo.GradientHorizontal(Color.White.WithAlpha(0), Color.White.WithAlpha(sweep_peak))
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativePositionAxes = Axes.X,
                            X = 0.5f,
                            Width = 0.5f,
                            ColorInfo = ColorInfo.GradientHorizontal(Color.White.WithAlpha(sweep_peak), Color.White.WithAlpha(0))
                        }
                    }
                }
            }
        };

        // Ticks off the song's BPM (or a steady default beat while nothing plays) and drives the flash.
        var beatSync = new BeatSyncContainer
        {
            AlwaysPresent = true,
            AllowBeatSyncWhenStopped = true,
            UseDefaultTempoWhenStopped = true
        };
        beatSync.NewBeat += onNewBeat;
        Add(beatSync);

        SetOrientation(orientation);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // The fill may have been picked before anything it reads was resolved; see CarouselFocusFace.Apply.
        RefreshColors();
    }

    protected abstract CarouselFocusFace<TModel> CreateFace();

    /// <summary>
    /// The window's fill behind <paramref name="model"/>. The face prints its text in a darker shade of it.
    /// </summary>
    protected abstract Color FillFor(TModel model);

    /// <summary>
    /// Whether the window flashes on the beat while it shows <paramref name="model"/>: when it's what's
    /// playing, so the window is still while you scroll past others, and keeps time once you settle.
    /// </summary>
    protected abstract bool FlashesOnBeat(TModel model);

    /// <summary>
    /// How many times the window has flashed on the beat (diagnostics/tests).
    /// </summary>
    public int BeatFlashCount { get; private set; }

    /// <summary>
    /// The item the window shows (the one at the rounded scroll position), if any.
    /// </summary>
    public TModel? ShownItem => colouredFor;

    /// <summary>
    /// How the carousel is moving: the faces compact while it skims.
    /// </summary>
    public CarouselMotion? Motion { get; set; }

    private bool phone;

    /// <summary>
    /// Sizes the faces for a phone or the desktop, the ones on show and the ones waiting to be.
    /// </summary>
    public void SetPhone(bool value)
    {
        phone = value;

        foreach (var f in live)
            f.Face.SetPhone(value);
        foreach (var face in spare)
            face.SetPhone(value);
    }

    /// <summary>
    /// The fill the window wears (or is fading to) for <see cref="ShownItem"/>.
    /// </summary>
    public Color FillColor => fillColor;

    /// <summary>
    /// How many times the window has winked (diagnostics/tests).
    /// </summary>
    public int WinkCount { get; private set; }

    public void SetOrientation(CarouselOrientation value)
    {
        orientation = value;
        body.CornerRadius = value == CarouselOrientation.Vertical ? 16f : 20f;

        foreach (var f in live)
            f.Face.SetOrientation(value);
        foreach (var face in spare)
            face.SetOrientation(value);
    }

    /// <summary>
    /// Lays the window out for this frame: <paramref name="position"/> is the carousel's (continuous)
    /// scroll position over <paramref name="items"/>. <paramref name="instant"/> for a list put somewhere
    /// rather than moved there: the item there shows at once.
    /// </summary>
    public void Display(IReadOnlyList<TModel> items, double position, bool instant = false)
    {
        if (items.Count == 0)
        {
            body.Alpha = 0;

            for (int i = live.Count - 1; i >= 0; i--)
                release(i);

            colouredFor = null;
            sweptFor = null;
            lastPosition = double.NaN;
            return;
        }

        body.Alpha = 1;

        double now = IsLoaded ? Clock.CurrentTime : 0;
        int index = Math.Clamp((int)Math.Round(position), 0, items.Count - 1);
        var current = items[index];

        if (shown == null || shown.Index != index || !ReferenceEquals(shown.Face.Model, current))
            swapTo(index, current, now, instant || shown == null || !IsLoaded);

        // Not while the list flies past: those it passes are gone again a frame later.
        if (now - lastSwap >= cut_swap_interval)
        {
            prepare(items, index - 1);
            prepare(items, index + 1);
        }

        float skim = Motion?.Skim ?? 0;

        for (int i = live.Count - 1; i >= 0; i--)
        {
            var f = live[i];
            f.Step(now);

            bool neighbour = Math.Abs(f.Index - index) == 1 && f.Index < items.Count && ReferenceEquals(items[f.Index], f.Face.Model);

            // Gone. The one just left is next door, so it usually stays on hand, ready to come back.
            if (f.Role == FaceRole.Leaving && !f.Moving)
            {
                if (!neighbour)
                {
                    release(i);
                    continue;
                }

                f.Role = FaceRole.Ready;
                f.Set(0, 0);
            }
            else if (f.Role == FaceRole.Ready && !neighbour)
            {
                release(i);
                continue;
            }

            f.Face.Position = orientation == CarouselOrientation.Vertical ? new Vector2(0f, f.Offset) : new Vector2(f.Offset, 0f);

            // Snapped at the ends: the alpha setter ignores the last tiny step of an ease, which would leave a
            // face that's gone still drawn, faintly.
            f.Face.Alpha = f.Alpha < 0.002f ? 0 : f.Alpha > 0.998f ? 1 : f.Alpha;
            f.Face.SetSkim(skim);
        }

        if (!ReferenceEquals(current, colouredFor))
        {
            fadeFill(FillFor(current), colouredFor == null ? 0 : fill_fade_duration);
            colouredFor = current;
        }

        // At rest on an item it hasn't winked at yet: not merely passing through one mid-scroll.
        bool still = Math.Abs(position - lastPosition) < 0.001;
        lastPosition = position;

        if (still && Math.Abs(position - Math.Round(position)) < 0.02 && !ReferenceEquals(current, sweptFor))
        {
            sweptFor = current;
            wink();
        }
    }

    /// <summary>
    /// Clicks over to the item at <paramref name="index"/>: the face shown leaves against the direction of
    /// travel, and the item's comes in from it. One already leaving (the list turned back) comes straight
    /// back from where it is.
    /// </summary>
    private void swapTo(int index, TModel model, double now, bool instant)
    {
        var previous = shown;
        int direction = previous == null || previous.Index == index ? 1 : Math.Sign(index - previous.Index);
        float length = orientation == CarouselOrientation.Vertical ? DrawHeight : DrawWidth;

        double sinceLast = now - lastSwap;
        double pace = Math.Clamp(sinceLast / relaxed_swap_interval, 0, 1);
        lastSwap = now;

        var next = find(index, model);
        bool turningBack = next?.Role == FaceRole.Leaving;

        if (next == null)
            live.Add(next = new LiveFace(take(model), index));

        next.Role = FaceRole.Shown;
        shown = next;

        // Put there rather than moved there, or flying past: nothing on its way out stays.
        if (instant || sinceLast < cut_swap_interval)
        {
            next.Set(0, 1);

            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i] != next && (live[i] == previous || live[i].Role == FaceRole.Leaving))
                    release(i);
            }

            return;
        }

        if (previous != null && previous != next)
        {
            previous.Role = FaceRole.Leaving;
            previous.MoveTo(-direction * leave_travel * length, 0, now, leave_duration * pace, Easing.OutCubic);
        }

        if (turningBack)
            next.MoveTo(0, 1, now, enter_duration * pace, Easing.OutQuint);
        else
        {
            next.Set(direction * enter_travel * length, 0);
            next.MoveTo(0, 1, now + enter_delay * pace, enter_duration * pace, Easing.OutQuint);
        }
    }

    /// <summary>
    /// Makes the item at <paramref name="index"/> ready, out of sight, if it isn't on hand already: its face
    /// filled in and its cover loading, for when it comes in.
    /// </summary>
    private void prepare(IReadOnlyList<TModel> items, int index)
    {
        if (index < 0 || index >= items.Count || find(index, items[index]) != null)
            return;

        var face = new LiveFace(take(items[index]), index) { Role = FaceRole.Ready };
        face.Set(0, 0);
        live.Add(face);
    }

    private LiveFace? find(int index, TModel model)
    {
        foreach (var f in live)
        {
            if (f.Index == index && ReferenceEquals(f.Face.Model, model))
                return f;
        }

        return null;
    }

    private void onNewBeat(int beat, double beatLength, ChannelAmplitudes amplitudes)
    {
        if (colouredFor == null || !FlashesOnBeat(colouredFor))
            return;

        BeatFlashCount++;

        // The same flash every beat: the beat sets it, not how loud the song is there.
        beatFlash.Alpha = beat_flash_alpha;
        beatFlash.FadeTo(0, beatLength, Easing.OutQuint);
    }

    /// <summary>
    /// Sends the streak of light across the window, from beyond one edge to beyond the other.
    /// </summary>
    private void wink()
    {
        if (!IsLoaded)
            return;

        WinkCount++;

        // How far the streak reaches either side of its centre, its slant included.
        float reach = sweep_width / 2 + Math.Abs(UserInterfaceConstant.Shear.X) * sweep.DrawHeight / 2;

        sweep.ClearTransforms();
        sweep.X = -reach;
        sweep.Alpha = 0;
        sweep.MoveToX(DrawWidth + reach, sweep_duration, sweep_easing);
        sweep.FadeTo(1, sweep_duration * 0.2).Then().FadeOut(sweep_duration * 0.8);
    }

    /// <summary>
    /// Recolours the window and the faces on it, for when an item's colours became known after it was shown.
    /// </summary>
    protected void RefreshColors()
    {
        if (colouredFor != null)
        {
            var color = FillFor(colouredFor);

            if (color != fillColor)
                fadeFill(color, fill_fade_duration);
        }

        foreach (var f in live)
            f.Face.RefreshColors();
    }

    private void fadeFill(Color color, double duration)
    {
        fill.FadeToColor(color, duration, Easing.OutQuint);
        fillColor = color;
    }

    /// <summary>
    /// A face for <paramref name="model"/>: a spare one that last showed it if there is one (its cover is
    /// still held, so it's back at once), or else any spare one, or else a new one.
    /// </summary>
    private CarouselFocusFace<TModel> take(TModel model)
    {
        int at = spare.FindIndex(f => ReferenceEquals(f.Model, model));

        if (at < 0)
            at = spare.Count - 1;

        CarouselFocusFace<TModel> face;

        if (at >= 0)
        {
            face = spare[at];
            spare.RemoveAt(at);
        }
        else
            face = createFace();

        face.Apply(model);
        return face;
    }

    private CarouselFocusFace<TModel> createFace()
    {
        var face = CreateFace();
        face.SetOrientation(orientation);
        face.SetPhone(phone);
        face.Alpha = 0;
        faceLayer.Add(face);
        return face;
    }

    private void release(int liveIndex)
    {
        var f = live[liveIndex];

        if (f == shown)
            shown = null;

        f.Face.Alpha = 0;
        spare.Add(f.Face);
        live.RemoveAt(liveIndex);
    }

    private enum FaceRole
    {
        /// <summary>
        /// The item at the scroll position, on show or on its way in.
        /// </summary>
        Shown,

        /// <summary>
        /// On its way out.
        /// </summary>
        Leaving,

        /// <summary>
        /// Next door to the one shown, out of sight, ready to come in.
        /// </summary>
        Ready
    }

    /// <summary>
    /// A face in use, with where it is along the scroll axis (px) and how opaque, eased between the two.
    /// </summary>
    private sealed class LiveFace
    {
        public readonly CarouselFocusFace<TModel> Face;
        public readonly int Index;
        public FaceRole Role;

        public float Offset { get; private set; }
        public float Alpha { get; private set; }
        public bool Moving { get; private set; }

        private float offsetFrom, offsetTo, alphaFrom, alphaTo;
        private double start, duration;
        private Easing easing;

        public LiveFace(CarouselFocusFace<TModel> face, int index)
        {
            Face = face;
            Index = index;
        }

        public void Set(float offset, float alpha)
        {
            Offset = offset;
            Alpha = alpha;
            Moving = false;
        }

        /// <summary>
        /// Eases from where the face is to <paramref name="offset"/> and <paramref name="alpha"/>, starting
        /// at <paramref name="at"/> (it holds still until then).
        /// </summary>
        public void MoveTo(float offset, float alpha, double at, double length, Easing easing)
        {
            offsetFrom = Offset;
            alphaFrom = Alpha;
            offsetTo = offset;
            alphaTo = alpha;
            start = at;
            duration = Math.Max(1, length);
            this.easing = easing;
            Moving = true;
        }

        public void Step(double now)
        {
            if (!Moving)
                return;

            double p = Math.Clamp((now - start) / duration, 0, 1);

            if (p >= 1)
            {
                Set(offsetTo, alphaTo);
                return;
            }

            float e = (float)EasingFunctions.Apply(easing, p);

            Offset = offsetFrom + (offsetTo - offsetFrom) * e;
            Alpha = alphaFrom + (alphaTo - alphaFrom) * e;
        }
    }
}
