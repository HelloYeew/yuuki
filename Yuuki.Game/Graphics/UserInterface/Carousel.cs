// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Pooling;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

public abstract partial class Carousel<TModel, TCard> : Container
    where TModel : class
    where TCard : CarouselCard<TModel>, new()
{
    /// <summary>
    /// How quickly the carousel eases toward its snap target. Higher is snappier.
    /// </summary>
    public double SnapStiffness { get; set; } = 0.018;

    /// <summary>
    /// Multiplier converting the release velocity (index units / ms) into a projected fling distance.
    /// </summary>
    public double FlingProjection { get; set; } = 180;

    /// <summary>
    /// Continuous on-screen time (ms) a slot must accumulate before its card is built. This is the
    /// floor, paid by the card at the centre of the carousel; cards further out additionally wait
    /// <see cref="LoadDelayPerSlot"/> per slot of distance.
    /// </summary>
    public double TimeBeforeLoad { get; set; } = 100;

    /// <summary>
    /// Extra on-screen time (ms) required per slot of distance from the centre before a card is built,
    /// so the selection loads first and the periphery follows.
    /// </summary>
    /// <remarks>
    /// This replaces a single flat delay tuned for the worst case. A flat delay has to be large enough
    /// to stop a fast scroll from building every card it flies past, which also makes the card you are
    /// actually looking at slow to appear. Scaling by distance separates the two: the centre can load
    /// promptly while distant slots become progressively harder to trigger. Set to 0 for a flat delay.
    /// </remarks>
    public double LoadDelayPerSlot { get; set; } = 60;

    /// <summary>
    /// Continuous off-screen time (ms) a slot must accumulate before its card is returned to the pool.
    /// </summary>
    public double TimeBeforeUnload { get; set; } = 3000;

    /// <summary>
    /// Maximum pointer travel (screen px) for a press to still count as a click rather than a drag.
    /// </summary>
    public float TapThreshold { get; set; } = 10f;

    /// <summary>
    /// How far below its resting spot a newly <see cref="AddItem"/>ed card starts before rising into place.
    /// </summary>
    public float AddAnimationOffset { get; set; } = 60f;

    /// <summary>
    /// How far a removed card slides downward as it fades out.
    /// </summary>
    public float RemoveAnimationOffset { get; set; } = 80f;

    /// <summary>
    /// Duration (ms) of the add (rise + fade in) animation.
    /// </summary>
    public double AddAnimationDuration { get; set; } = 300;

    /// <summary>
    /// Duration (ms) of the remove (drop + fade out) animation and the reflow of the remaining cards.
    /// </summary>
    public double RemoveAnimationDuration { get; set; } = 250;

    /// <summary>
    /// Duration (ms) of a single card's staggered screen entrance / exit (rise-or-slide + fade).
    /// </summary>
    public double EntranceDuration { get; set; } = 460;

    /// <summary>
    /// Per-card delay (ms) added by distance from the selection, so a batch entrance / exit cascades out
    /// from the centred card rather than moving as one flat block.
    /// </summary>
    public double EntranceStagger { get; set; } = 34;

    /// <summary>
    /// When true, a card plays its entrance the moment it finishes loading (so cards streamed in after
    /// the region is shown, or scrolled into view, ease in instead of popping). Toggled by the host via
    /// <see cref="AnimateEntrance"/> / <see cref="AnimateExit"/>.
    /// </summary>
    public bool PlayLoadEntrance { get; set; }

    /// <summary>
    /// Cards further than this many slots from the centre all share the maximum stagger delay.
    /// </summary>
    private const int max_stagger_steps = 9;

    /// <summary>
    /// Whether to show the position scrollbar (hidden automatically when there's 0/1 item).
    /// </summary>
    public bool ShowScrollbar { get; set; } = true;

    /// <summary>
    /// Resting thickness (px) of the scrollbar (its short axis).
    /// </summary>
    public float ScrollbarThickness { get; set; } = 10f;

    /// <summary>
    /// Thickness (px) the scrollbar expands to while hovered or dragged.
    /// </summary>
    public float ScrollbarHoverThickness { get; set; } = 18f;

    /// <summary>
    /// Gap (px) between the scrollbar and the edge it hugs.
    /// </summary>
    public float ScrollbarEdgeGap { get; set; } = 8f;

    /// <summary>
    /// Room kept clear at the start of the list (px), for something placed over it there (see
    /// <see cref="CarouselMetrics.StartInset"/>).
    /// </summary>
    public float StartInset { get; set; }

    /// <summary>
    /// Inset (px) at each end of the scrollbar track along its long axis.
    /// </summary>
    public float ScrollbarMargin { get; set; } = 14f;

    /// <summary>
    /// Minimum length (px) of the scrollbar thumb along its long axis.
    /// </summary>
    public float ScrollbarMinThumb { get; set; } = 30f;

    /// <summary>
    /// How far (px) the thumb sits in from the track's edges, all round: 0 fills the track's width, more
    /// makes it a rounded bar inside it.
    /// </summary>
    public float ScrollbarThumbInset { get; set; }

    /// <summary>
    /// The scrollbar's track colour. White by default; a carousel on a light surface sets ink instead.
    /// </summary>
    public Color ScrollbarTrackColor { get; set; } = Color.White.WithAlpha(0.12f);

    /// <summary>
    /// The colour of the labels along the scrollbar (see <see cref="SetScrollbarLabels"/>).
    /// </summary>
    public Color ScrollbarLabelColor { get; set; } = Color.White.WithAlpha(0.7f);

    /// <summary>
    /// The scrollbar thumb's colour at rest; it's drawn fully opaque while hovered or dragged.
    /// </summary>
    public Color ScrollbarThumbColor { get; set; } = Color.White.WithAlpha(0.55f);

    /// <summary>
    /// Fires (with old and new value) whenever the committed/selected item changes.
    /// </summary>
    public readonly Reactive<TModel?> CurrentItem = new Reactive<TModel?>(null);

    /// <summary>
    /// The direction cards are laid out in. Changing this value live re-lays-out and reshapes every card
    /// (strip tiles vs rail rows) without rebuilding the pool.
    /// </summary>
    public readonly Reactive<CarouselOrientation> Orientation = new Reactive<CarouselOrientation>(CarouselOrientation.Horizontal);

    /// <summary>
    /// The current orientation-dependent geometry, kept in sync with <see cref="Orientation"/>.
    /// </summary>
    private CarouselLayoutStrategy layout;

    // The focus window sits in a host the carousel moves to the strategy's window rectangle every frame;
    // the window itself is free to animate inside it (entrance and exit).
    private Container focusHost = null!;
    private CarouselFocusWindow<TModel>? focusWindow;

    private readonly List<CarouselCardWrapper> wrappers = new List<CarouselCardWrapper>();
    private readonly List<TModel> items = new List<TModel>();

    private readonly List<CarouselCardWrapper> exitingWrappers = new List<CarouselCardWrapper>();

    private DrawablePool<TCard> pool = null!;

    private CarouselScrollbar scrollbar = null!;

    private bool scrollbarScrubbing;

    private double position;
    private int currentIndex;

    private bool isDragging;
    private double dragVelocity;
    private double lastFramePosition;
    private float pressTravel;

    /// <summary>
    /// Whether moving fast through the list compacts it (skim mode). Song lists do; a difficulty list,
    /// a handful of items, doesn't.
    /// </summary>
    public bool AllowSkim { get; set; }

    /// <summary>
    /// How the list is moving: skim mode and the look-ahead. Shared with the cards and the focus window.
    /// </summary>
    public CarouselMotion Motion { get; } = new CarouselMotion();

    /// <summary>
    /// Steps faster than this (items a second, over the last <see cref="step_window"/>) skim.
    /// </summary>
    private const double skim_steps_per_second = 7;

    private const double step_window = 400;

    /// <summary>
    /// How long skim mode outlasts the movement that set it off (ms), so it doesn't flicker between steps.
    /// </summary>
    private const double skim_hold = 220;

    // When the user stepped (keys, wheel), the scroll velocity (items/s, smoothed), and the position it
    // was last measured from.
    private readonly Queue<double> stepTimes = new Queue<double>();
    private double skimUntil = double.MinValue;
    private double smoothedVelocity;
    private double motionFrom = double.NaN;

    // The labels along the scrollbar, kept to hand to each scrollbar SetItems builds.
    private IReadOnlyList<(int Index, string Label)> scrollbarLabels = Array.Empty<(int, string)>();

    public int CurrentIndex => currentIndex;

    public int Count => items.Count;

    /// <summary>
    /// Whether any add/remove animation (entry rise, reflow glide, or exit drop) is currently running.
    /// </summary>
    public bool IsAnimating
    {
        get
        {
            if (exitingWrappers.Count > 0)
                return true;

            foreach (var w in wrappers)
            {
                if (w.IsAnimating)
                    return true;
            }

            return false;
        }
    }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    /// <summary>
    /// Whether the game is in its phone layout, where the subclasses' rows, tiles and windows are bigger
    /// (see <see cref="CreateLayout"/>).
    /// </summary>
    protected bool IsPhone => gameLayout?.IsPhone ?? false;

    protected Carousel()
    {
        RelativeSizeAxes = Axes.Both;
        layout = CreateLayout(Orientation.Value);
        Orientation.BindValueChanged(_ => rebuildLayout());
        createFocusWindow();
        createScrollbar();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // The geometry made in the constructor couldn't know the layout yet.
        if (gameLayout != null)
            BindValueChanged(gameLayout.Current, _ => rebuildLayout(), true);
    }

    /// <summary>
    /// The geometry for <paramref name="orientation"/>. Subclasses override it to size their rows, tiles
    /// and focus window.
    /// </summary>
    protected virtual CarouselLayoutStrategy CreateLayout(CarouselOrientation orientation) => orientation switch
    {
        CarouselOrientation.Vertical => new RailCarouselLayout(),
        _ => new StripCarouselLayout()
    };

    /// <summary>
    /// The window that shows the item at the scroll position in its large layout, or null for none.
    /// </summary>
    protected virtual CarouselFocusWindow<TModel>? CreateFocusWindow() => null;

    /// <summary>
    /// The focus window, if this carousel has one.
    /// </summary>
    public CarouselFocusWindow<TModel>? FocusWindow => focusWindow;

    /// <summary>
    /// Where the focus window sits, in local space.
    /// </summary>
    public RectangleF FocusWindowRect => layout.FocusWindow(metrics());

    /// <summary>
    /// The continuous scroll position, in index units: <see cref="CurrentIndex"/> once the snap settles.
    /// </summary>
    public double ScrollPosition => position;

    /// <summary>
    /// Where the item at <paramref name="index"/> is drawn right now, in local space (its resting slot,
    /// without entrance animation).
    /// </summary>
    public Vector2 SlotCentre(int index) => DrawSize / 2f + layout.Compute((float)(index - position), metrics()).Position;

    /// <summary>
    /// Builds the focus window (if any) in front of the cards. Recreated by <see cref="SetItems"/>
    /// because <c>Clear()</c> tears down all children.
    /// </summary>
    private void createFocusWindow()
    {
        focusWindow = CreateFocusWindow();

        // In front of every card (the nearest is depth 0) and behind the scrollbar.
        Add(focusHost = new Container { Depth = 500f });

        if (focusWindow == null)
            return;

        focusWindow.Motion = Motion;
        focusWindow.SetOrientation(Orientation.Value);
        focusWindow.SetPhone(IsPhone);
        focusHost.Add(focusWindow);
    }

    /// <summary>
    /// Builds the position scrollbar and adds it in front of the cards. Recreated by
    /// <see cref="SetItems"/> because <c>Clear()</c> tears down all children.
    /// </summary>
    private void createScrollbar()
    {
        scrollbar = new CarouselScrollbar(this)
        {
            // Higher depth = front in this framework, so this sits over the cards (selected card is depth 0).
            Depth = 1000f
        };

        applyScrollbarOrientation();
        scrollbar.SetLabels(scrollbarLabels);
        Add(scrollbar);
    }

    /// <summary>
    /// Pins the scrollbar to the edge that matches the current orientation (bottom vs. right).
    /// </summary>
    private void applyScrollbarOrientation()
    {
        if (scrollbar is null)
            return;

        scrollbar.SetOrientation(Orientation.Value);

        if (Orientation.Value == CarouselOrientation.Vertical)
        {
            scrollbar.Anchor = scrollbar.Origin = Anchor.CentreRight;
            scrollbar.Position = new Vector2(-ScrollbarEdgeGap, 0);
        }
        else
        {
            scrollbar.Anchor = scrollbar.Origin = Anchor.BottomCentre;
            scrollbar.Position = new Vector2(0, -ScrollbarEdgeGap);
        }
    }

    /// <summary>
    /// Feeds the scrollbar the current scroll progress each frame; it draws + handles input itself.
    /// </summary>
    private void updateScrollbar()
    {
        if (scrollbar is null)
            return;

        if (!ShowScrollbar || items.Count <= 1)
        {
            scrollbar.Adjust(false, 1f, 0f, 1f);
            return;
        }

        bool vertical = Orientation.Value == CarouselOrientation.Vertical;
        float trackLen = Math.Max(1f, (vertical ? DrawSize.Y : DrawSize.X) - 2 * ScrollbarMargin);
        float progress = (float)Math.Clamp(position / (items.Count - 1), 0, 1);
        float thumbFraction = 1f / items.Count;

        scrollbar.Adjust(true, trackLen, progress, thumbFraction);
    }

    #region Scrollbar scrubbing

    /// <summary>
    /// Begins a scrollbar drag: the scroll position follows the thumb directly until released.
    /// </summary>
    private void beginScrollbarScrub()
    {
        scrollbarScrubbing = true;
        dragVelocity = 0;
    }

    /// <summary>
    /// Sets the scroll position directly from a 0..1 scrollbar progress (live, while dragging).
    /// </summary>
    private void scrollbarScrubTo(float progress)
    {
        if (items.Count == 0)
            return;

        position = Math.Clamp(progress, 0, 1) * (items.Count - 1);
        lastFramePosition = position;

        int index = Math.Clamp((int)Math.Round(position), 0, items.Count - 1);
        if (index != currentIndex)
        {
            currentIndex = index;
            CurrentItem.Value = items[index];
        }

        updateLayout();
    }

    /// <summary>Ends a scrollbar drag and snaps to the nearest card.</summary>
    private void endScrollbarScrub()
    {
        scrollbarScrubbing = false;
        SetIndex((int)Math.Round(position));
    }

    /// <summary>
    /// Jumps (animated) to the card nearest a 0..1 scrollbar progress (click on the track).
    /// </summary>
    private void scrollbarJumpTo(float progress)
    {
        if (items.Count == 0)
            return;

        SetIndex((int)Math.Round(Math.Clamp(progress, 0, 1) * (items.Count - 1)));
    }

    #endregion

    private CarouselMetrics metrics() => new CarouselMetrics(DrawSize, Motion.Skim, Motion.Lead, StartInset);

    /// <summary>
    /// The labels along the scrollbar (diagnostics/tests).
    /// </summary>
    public IReadOnlyList<(int Index, string Label)> ScrollbarLabels => scrollbarLabels;

    /// <summary>
    /// Labels along the scrollbar: each at its item's place on the track (the start of a group in the
    /// current sort, say), so a drag across the track can aim. Kept across <see cref="SetItems"/>.
    /// </summary>
    public void SetScrollbarLabels(IReadOnlyList<(int Index, string Label)> labels)
    {
        scrollbarLabels = labels;
        scrollbar?.SetLabels(labels);
    }

    /// <summary>
    /// Swaps the geometry strategy and reshapes every live slot to the new orientation, then relays out.
    /// The pool is untouched, so cards keep their loaded covers across the switch.
    /// </summary>
    /// <summary>
    /// Remakes the geometry for the orientation and the game's layout, and reshapes every live slot to it.
    /// </summary>
    private void rebuildLayout()
    {
        layout = CreateLayout(Orientation.Value);
        focusWindow?.SetOrientation(Orientation.Value);
        focusWindow?.SetPhone(IsPhone);

        var slotSize = layout.SlotSize(metrics());
        foreach (var w in wrappers)
            w.Size = slotSize;

        applyScrollbarOrientation();
        updateLayout();
    }

    /// <summary>
    /// Replaces the contents of the carousel with the given items.
    /// </summary>
    public void SetItems(IReadOnlyList<TModel> newItems)
    {
        Clear();
        wrappers.Clear();
        items.Clear();
        exitingWrappers.Clear();

        pool = new DrawablePool<TCard>(12, 48);
        Add(pool);

        // Clear() above tore down the previous focus window and scrollbar, so rebuild them.
        createFocusWindow();
        createScrollbar();

        for (int i = 0; i < newItems.Count; i++)
            addWrapperFor(newItems[i], i);

        currentIndex = 0;
        position = 0;
        lastFramePosition = 0;
        dragVelocity = 0;

        // A new list isn't the old one moving.
        motionFrom = double.NaN;

        CurrentItem.Value = items.Count > 0 ? items[0] : null;
        updateLayout();
    }

    /// <summary>
    /// Appends a single item to the end of the carousel without disturbing the current selection or
    /// scroll position (unlike <see cref="SetItems"/> which rebuilds everything). Handy for live
    /// updates such as a beatmap being imported while the carousel is on screen. If this is the first
    /// item, it becomes the selected one.
    /// </summary>
    public void AddItem(TModel item)
    {
        ensurePool();

        var wrapper = addWrapperFor(item, items.Count);

        if (items.Count == 1)
        {
            currentIndex = 0;
            position = 0;
            lastFramePosition = 0;
            dragVelocity = 0;
            CurrentItem.Value = item;
        }

        wrapper.AnimY = AddAnimationOffset;
        wrapper.AnimAlpha = 0f;
        wrapper.AnimateTo(0f, 0f, 1f, AddAnimationDuration, Clock.CurrentTime);

        updateLayout();
    }

    /// <summary>
    /// Removes <paramref name="item"/> if present, returning whether anything was removed. The
    /// selection is kept on the same logical item where possible (shifting down when an earlier item is
    /// removed, clamping when the last / current item is removed).
    /// </summary>
    public bool RemoveItem(TModel item)
    {
        int removeIndex = items.IndexOf(item);
        if (removeIndex < 0)
            return false;

        double now = Clock.CurrentTime;
        var exiting = wrappers[removeIndex];

        // Remember where every surviving card is on screen right now, so it can glide from there to its
        // new slot rather than snapping when the indices shift.
        var previousScreenPositions = new Dictionary<CarouselCardWrapper, Vector2>();
        foreach (var w in wrappers)
        {
            if (w != exiting)
                previousScreenPositions[w] = w.Position;
        }

        items.RemoveAt(removeIndex);
        wrappers.RemoveAt(removeIndex);
        reindex();

        // Keep the selection on the same logical item where possible.
        if (items.Count == 0)
        {
            currentIndex = 0;
            CurrentItem.Value = null;
        }
        else
        {
            int newCurrent = currentIndex;
            if (removeIndex < currentIndex)
                newCurrent--;
            currentIndex = Math.Clamp(newCurrent, 0, items.Count - 1);
            CurrentItem.Value = items[currentIndex];
        }

        // Snap the logical scroll to the new selection; the visual continuity is carried by each card's
        // animated offset, so nothing appears to jump.
        position = currentIndex;
        lastFramePosition = position;

        var m = metrics();
        for (int i = 0; i < wrappers.Count; i++)
        {
            var w = wrappers[i];
            var target = layout.Compute((float)(i - position), m).Position;

            if (previousScreenPositions.TryGetValue(w, out var oldPos))
            {
                w.AnimX = oldPos.X - target.X;
                w.AnimY = oldPos.Y - target.Y;
            }

            w.AnimateTo(0f, 0f, 1f, RemoveAnimationDuration, now);
        }

        // The removed card detaches from the index-based layout and drops down + fades before disposal.
        exiting.FrozenPosition = exiting.Position;
        exiting.AnimX = 0f;
        exiting.AnimY = 0f;
        exiting.AnimAlpha = 1f;
        exiting.AnimateTo(0f, RemoveAnimationOffset, 0f, RemoveAnimationDuration, now);
        exitingWrappers.Add(exiting);

        updateLayout();
        return true;
    }

    /// <summary>
    /// Creates a pooled-card wrapper for <paramref name="item"/> at <paramref name="index"/>, registers
    /// it, and adds it as a child. The card reads its model and (mutable) index live, so the on-screen
    /// test and per-card colour stay correct after <see cref="reindex"/>.
    /// </summary>
    private CarouselCardWrapper addWrapperFor(TModel item, int index)
    {
        items.Add(item);

        CarouselCardWrapper wrapper = null!;
        wrapper = new CarouselCardWrapper(
            () => pool.Get(c =>
            {
                c.Orientation = Orientation;
                c.Motion = Motion;
                c.Apply(wrapper.Model, wrapper.Index);
            }),
            () => isOnScreen(wrapper.Index),
            () => wrapper.Index - position,
            () => LoadDelayPerSlot,
            TimeBeforeLoad,
            TimeBeforeUnload)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = layout.SlotSize(metrics()),
            Model = item,
            Index = index
        };

        // Ease each card in the moment its content loads, so cards that stream in after the region is
        // shown (or are scrolled into view) don't just pop. Batch (re)shows drive this via AnimateEntrance.
        wrapper.DelayedLoadComplete += _ =>
        {
            if (PlayLoadEntrance)
                playEntrance(wrapper, Clock.CurrentTime);
        };

        wrappers.Add(wrapper);
        Add(wrapper);
        return wrapper;
    }

    /// <summary>
    /// Re-syncs each wrapper's stored index with its position in <see cref="wrappers"/> after a
    /// structural change (used by <see cref="RemoveItem"/>).
    /// </summary>
    private void reindex()
    {
        for (int i = 0; i < wrappers.Count; i++)
            wrappers[i].Index = i;
    }

    /// <summary>
    /// Ensures a card pool exists. <see cref="AddItem"/> may be called before any <see cref="SetItems"/>.
    /// </summary>
    private void ensurePool()
    {
        if (pool != null)
            return;

        pool = new DrawablePool<TCard>(12, 48);
        Add(pool);
    }

    /// <summary>
    /// Snaps the selection to a specific index (clamped to the valid range).
    /// </summary>
    public void SetIndex(int index, bool animate = true)
    {
        if (items.Count == 0)
            return;

        int clamped = Math.Clamp(index, 0, items.Count - 1);

        if (clamped != currentIndex)
        {
            currentIndex = clamped;
            CurrentItem.Value = items[clamped];
        }

        if (!animate)
        {
            position = clamped;
            lastFramePosition = clamped;

            // Put there, not moved there: no speed to skim on or lead with.
            motionFrom = clamped;
            updateLayout();
        }
    }

    /// <summary>
    /// One step on, as a key press takes: counted towards skim mode.
    /// </summary>
    public void Next()
    {
        recordStep();
        SetIndex(currentIndex + 1);
    }

    /// <summary>
    /// One step back, as a key press takes: counted towards skim mode.
    /// </summary>
    public void Previous()
    {
        recordStep();
        SetIndex(currentIndex - 1);
    }

    private void recordStep() => stepTimes.Enqueue(Clock.CurrentTime);

    /// <summary>
    /// The index of <paramref name="item"/> in the carousel, or -1 if it is not currently present.
    /// </summary>
    public int IndexOf(TModel item) => items.IndexOf(item);

    /// <summary>
    /// Selects <paramref name="item"/> if it is currently in the carousel, animating the scroll to it
    /// by default. No-op if the item is not present. Used to drive the carousel from an external
    /// source of truth (e.g. the working beatmap) without the caller needing to know the item's index.
    /// </summary>
    /// <summary>Whether <paramref name="item"/> is one of the carousel's items.</summary>
    public bool Contains(TModel item) => items.Contains(item);

    /// <summary>
    /// The item at <paramref name="index"/>, or null past either end.
    /// </summary>
    public TModel? ItemAt(int index) => index >= 0 && index < items.Count ? items[index] : null;

    public void SelectItem(TModel item, bool animate = true)
    {
        int index = items.IndexOf(item);
        if (index >= 0)
            SetIndex(index, animate);
    }

    /// <summary>
    /// Plays a staggered entrance for the currently-loaded cards: each rises (strip) or slides in
    /// (list) from <see cref="CarouselLayoutStrategy.EntranceOffset"/> and fades up, delayed by its
    /// distance from the selection so the cascade emanates from the centre. Also arms
    /// <see cref="PlayLoadEntrance"/> so cards that finish loading afterwards ease in the same way. Call
    /// when the carousel is (re)shown.
    /// </summary>
    public void AnimateEntrance()
    {
        PlayLoadEntrance = true;
        animateCards(entering: true);
    }

    /// <summary>
    /// Reverse of <see cref="AnimateEntrance"/>: loaded cards slide/drop out and fade away, cascading
    /// from the centre. Also disarms <see cref="PlayLoadEntrance"/>. Call when the carousel is hidden.
    /// </summary>
    public void AnimateExit()
    {
        PlayLoadEntrance = false;
        animateCards(entering: false);
    }

    private void animateCards(bool entering)
    {
        double now = Clock.CurrentTime;
        var offset = layout.EntranceOffset(metrics());

        for (int i = 0; i < wrappers.Count; i++)
        {
            var w = wrappers[i];

            // Only animate cards that are actually built: an unbuilt wrapper is empty, and will play its
            // own entrance the moment its content finishes loading (see PlayLoadEntrance).
            if (!w.DelayedLoadCompleted)
                continue;

            int steps = Math.Min(max_stagger_steps, (int)Math.Round(Math.Abs(i - position)));
            double at = now + steps * EntranceStagger;

            if (entering)
                playEntrance(w, at, offset);
            else
                w.AnimateTo(offset.X, offset.Y, 0f, EntranceDuration, at);
        }

        // The window is the centre of the cascade, so it moves with no delay.
        if (focusWindow == null)
            return;

        focusWindow.ClearTransforms();

        if (entering)
        {
            focusWindow.MoveTo(offset).FadeOut()
                       .MoveTo(Vector2.Zero, EntranceDuration, Easing.OutQuint)
                       .FadeIn(EntranceDuration, Easing.OutQuint);
        }
        else
        {
            focusWindow.MoveTo(offset, EntranceDuration, Easing.OutQuint)
                       .FadeOut(EntranceDuration, Easing.OutQuint);
        }
    }

    /// <summary>
    /// Snaps a single card to its entrance start (offset + transparent) and animates it to rest. Used
    /// both by the batch <see cref="AnimateEntrance"/> and per-card on load completion.
    /// </summary>
    private void playEntrance(CarouselCardWrapper wrapper, double at) => playEntrance(wrapper, at, layout.EntranceOffset(metrics()));

    private void playEntrance(CarouselCardWrapper wrapper, double at, Vector2 offset)
    {
        wrapper.AnimX = offset.X;
        wrapper.AnimY = offset.Y;
        wrapper.AnimAlpha = 0f;
        wrapper.AnimateTo(0f, 0f, 1f, EntranceDuration, at);
    }

    #region Input

    /// <summary>
    /// Raised when the mouse wheel steps the carousel itself (it takes the wheel while hovered), with
    /// the direction (+1 next, -1 previous) and whether the selection actually moved (false at either
    /// end). Lets the owner give it the same sound as the rest of its navigation.
    /// </summary>
    public event Action<int, bool>? Scrolled;

    /// <summary>
    /// A tap (click) anywhere but on the selected card: whether that moved the selection (it doesn't
    /// past either end, where the selection is already the nearest card).
    /// </summary>
    public event Action<bool>? Tapped;

    /// <summary>
    /// A tap (click) on the card that was already selected.
    /// </summary>
    public event Action? SelectedTapped;

    public override bool OnScroll(ScrollEvent e)
    {
        if (items.Count == 0)
            return false;

        int direction = e.ScrollDelta.Y < 0 ? 1 : e.ScrollDelta.Y > 0 ? -1 : 0;
        if (direction == 0)
            return true;

        int before = currentIndex;
        recordStep();
        SetIndex(currentIndex + direction);
        Scrolled?.Invoke(direction, currentIndex != before);

        return true;
    }

    public override bool OnDragStart(MouseButtonEvent e)
    {
        if (items.Count == 0)
            return false;

        isDragging = true;
        dragVelocity = 0;
        lastFramePosition = position;
        pressTravel = 0;
        return true;
    }

    public override bool OnDrag(MouseEvent e)
    {
        if (!isDragging)
            return false;

        pressTravel += Math.Abs(e.Delta.X) + Math.Abs(e.Delta.Y);

        position += layout.DragToPositionDelta(e.Delta, metrics());
        position = clampWithOverscroll(position);
        updateLayout();
        return true;
    }

    public override bool OnDragEnd(MouseButtonEvent e)
    {
        if (!isDragging)
            return false;

        isDragging = false;

        if (pressTravel < TapThreshold)
        {
            int before = currentIndex;
            int tapped = slotAt(e.ScreenSpaceMousePosition);

            // Compared unclamped: a tap past the last card lands on no card, even with the last selected.
            if (tapped == before)
            {
                SelectedTapped?.Invoke();
                return true;
            }

            SetIndex(tapped);
            Tapped?.Invoke(currentIndex != before);
            return true;
        }

        double projected = position + dragVelocity * FlingProjection;
        SetIndex((int)Math.Round(projected));
        return true;
    }

    /// <summary>
    /// The slot nearest to a screen-space position (used for click-to-select), unclamped: past either
    /// end it is a slot with no card.
    /// </summary>
    private int slotAt(Vector2 screenSpacePosition) => layout.IndexAtLocal(ToLocalSpace(screenSpacePosition), position, metrics());

    #endregion

    public override void Update()
    {
        base.Update();

        double now = Clock.CurrentTime;

        // Removed cards animate out independently of the layout, so advance them even when the carousel
        // is otherwise empty.
        updateExitingWrappers(now);

        if (items.Count == 0)
            return;

        double dt = Clock.ElapsedFrameTime;
        if (dt <= 0)
            dt = 1;

        if (isDragging)
        {
            double instantaneous = (position - lastFramePosition) / dt;
            dragVelocity = dragVelocity * 0.6 + instantaneous * 0.4;
            lastFramePosition = position;
        }
        else if (scrollbarScrubbing)
        {
            // Position is driven directly by the scrollbar thumb; don't let the snap fight it.
            lastFramePosition = position;
        }
        else
        {
            double diff = currentIndex - position;

            if (Math.Abs(diff) < 0.0005)
                position = currentIndex;
            else
                position += diff * (1 - Math.Exp(-SnapStiffness * dt));
        }

        for (int i = 0; i < wrappers.Count; i++)
            wrappers[i].StepAnimation(now);

        updateMotion(now, dt);
        updateLayout();
    }

    /// <summary>
    /// Follows how fast the list moves: into skim mode past about seven items a second (stepping, a fling,
    /// a long jump or the scrubber) and back out a moment after it slows, and the look-ahead leaning with
    /// the direction of travel.
    /// </summary>
    private void updateMotion(double now, double dt)
    {
        double velocity = double.IsNaN(motionFrom) ? 0 : (position - motionFrom) / dt * 1000;
        motionFrom = position;
        smoothedVelocity += (velocity - smoothedVelocity) * (1 - Math.Exp(-dt / 120));

        while (stepTimes.Count > 0 && now - stepTimes.Peek() > step_window)
            stepTimes.Dequeue();

        if (AllowSkim)
        {
            bool fast = stepTimes.Count / (step_window / 1000) > skim_steps_per_second
                        || Math.Abs(currentIndex - position) > 3
                        || Math.Abs(smoothedVelocity) > 9;

            if (fast)
                skimUntil = now + skim_hold;
        }

        // Quick in, gentler out.
        float skimTarget = now < skimUntil ? 1 : 0;
        float skim = Motion.Skim + (skimTarget - Motion.Skim) * (float)(1 - Math.Exp(-dt / (skimTarget > 0 ? 70 : 140)));
        Motion.Skim = skim < 0.002f ? 0 : skim > 0.998f ? 1 : skim;

        float lead = Motion.Lead + ((float)Math.Clamp(smoothedVelocity / 12, -1, 1) - Motion.Lead) * (float)(1 - Math.Exp(-dt / 260));
        Motion.Lead = Math.Abs(lead) < 0.001f ? 0 : lead;
    }

    private void updateExitingWrappers(double now)
    {
        for (int i = exitingWrappers.Count - 1; i >= 0; i--)
        {
            var w = exitingWrappers[i];
            w.StepAnimation(now);

            // Held at the spot it left from, plus the (animated) drop offset.
            w.Position = new Vector2(w.FrozenPosition.X + w.AnimX, w.FrozenPosition.Y + w.AnimY);
            w.Alpha = w.AnimAlpha;

            if (!w.IsAnimating)
            {
                // Detach the finished wrapper, which disposes it. Its pooled card is not disposed with
                // it — pooled drawables opt out of removal disposal — so the cascade returns the card to
                // the pool, which owns its lifetime.
                Remove(w);
                exitingWrappers.RemoveAt(i);
            }
        }
    }

    private double clampWithOverscroll(double value)
    {
        const double overscroll = 0.6;
        return Math.Clamp(value, -overscroll, items.Count - 1 + overscroll);
    }

    private void updateLayout()
    {
        var m = metrics();
        var slotSize = layout.SlotSize(m);

        for (int i = 0; i < wrappers.Count; i++)
        {
            var w = wrappers[i];

            // Slot size can depend on the (late-measured, resizable) draw size, so keep it in sync.
            if (w.Size != slotSize)
                w.Size = slotSize;

            var l = layout.Compute((float)(i - position), m);

            // The per-card animation offsets (entry rise, reflow glide) ride on top of the computed
            // resting layout so add/remove animations survive this every-frame reposition.
            w.Position = new Vector2(l.Position.X + w.AnimX, l.Position.Y + w.AnimY);
            w.Scale = l.Scale;
            w.Alpha = l.Alpha * w.AnimAlpha;

            w.Depth = -Math.Abs((float)(i - position));
        }

        var window = layout.FocusWindow(m);
        focusHost.Position = window.Location;
        focusHost.Size = window.Size;
        focusWindow?.Display(items, position);

        updateScrollbar();
    }

    /// <summary>
    /// Whether the slot for the given index is close enough to the centre to be worth loading.
    /// </summary>
    private bool isOnScreen(int index) => layout.IsOnScreen((float)(index - position), metrics());

    /// <summary>
    /// The interactive position scrollbar: a rounded track with a thumb that reflects the scroll
    /// position, expands on hover, and can be dragged (scrub) or clicked (jump). The carousel feeds it
    /// progress every frame via <see cref="Adjust"/>; it owns its own geometry and input.
    /// </summary>
    private partial class CarouselScrollbar : Container
    {
        private const float thickness_lerp = 0.28f;

        private readonly Carousel<TModel, TCard> carousel;
        private readonly Box track;
        private readonly Container labelLayer;
        private readonly Container thumb;
        private readonly Box thumbFill;

        private IReadOnlyList<(int Index, string Label)> labels = Array.Empty<(int, string)>();

        private CarouselOrientation orientation = CarouselOrientation.Horizontal;
        private bool visible;
        private float trackLength = 1f;
        private float progress;
        private float thumbFraction = 0.2f;
        private float currentThickness;
        private bool dragging;

        public CarouselScrollbar(Carousel<TModel, TCard> carousel)
        {
            this.carousel = carousel;
            currentThickness = carousel.ScrollbarThickness;

            Alpha = 0;
            Masking = true;
            CornerRadius = currentThickness / 2f;
            Children = new Drawable[]
            {
                track = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = carousel.ScrollbarTrackColor
                },
                // Under the thumb, which passes over them.
                labelLayer = new Container
                {
                    RelativeSizeAxes = Axes.Both
                },
                thumb = new Container
                {
                    Masking = true,
                    Child = thumbFill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = carousel.ScrollbarThumbColor
                    }
                }
            };
        }

        /// <summary>
        /// Puts <paramref name="value"/> along the track, each where its item's thumb would be.
        /// </summary>
        public void SetLabels(IReadOnlyList<(int Index, string Label)> value)
        {
            labels = value;
            labelLayer.Clear();

            foreach (var (_, label) in value)
            {
                labelLayer.Add(new SpriteText
                {
                    Origin = Anchor.Centre,
                    Text = label,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 10, FontWeight.ExtraBold)
                });
            }
        }

        public void SetOrientation(CarouselOrientation value)
        {
            orientation = value;
            thumb.Anchor = thumb.Origin = value == CarouselOrientation.Vertical ? Anchor.TopCentre : Anchor.CentreLeft;
        }

        /// <summary>
        /// Called each frame by the carousel with the current scroll state.
        /// </summary>
        public void Adjust(bool show, float trackLength, float progress, float thumbFraction)
        {
            visible = show;
            this.trackLength = Math.Max(1f, trackLength);
            this.progress = Math.Clamp(progress, 0, 1);
            this.thumbFraction = Math.Clamp(thumbFraction, 0.02f, 1f);
        }

        public override void Update()
        {
            base.Update();

            Alpha = visible ? 1f : 0f;

            bool active = IsHovered || dragging;
            float targetThickness = active ? carousel.ScrollbarHoverThickness : carousel.ScrollbarThickness;
            currentThickness += (targetThickness - currentThickness) * thickness_lerp;
            CornerRadius = currentThickness / 2f;

            float inset = carousel.ScrollbarThumbInset;
            float thumbLen = thumbLength();
            float thumbPos = inset + progress * travel(thumbLen);
            float thumbWidth = Math.Max(0, currentThickness - inset * 2);

            if (orientation == CarouselOrientation.Vertical)
            {
                Size = new Vector2(currentThickness, trackLength);
                thumb.Size = new Vector2(thumbWidth, thumbLen);
                thumb.Position = new Vector2(0, thumbPos);
            }
            else
            {
                Size = new Vector2(trackLength, currentThickness);
                thumb.Size = new Vector2(thumbLen, thumbWidth);
                thumb.Position = new Vector2(thumbPos, 0);
            }

            // Rounded at its ends like the track; inset, it's a bar of its own inside it.
            thumb.CornerRadius = thumbWidth / 2f;

            track.Color = carousel.ScrollbarTrackColor;
            thumbFill.Color = active ? carousel.ScrollbarThumbColor.WithAlpha(1f) : carousel.ScrollbarThumbColor;

            updateLabels(thumbLen);
        }

        private void updateLabels(float thumbLen)
        {
            int count = carousel.items.Count;

            for (int i = 0; i < labelLayer.Children.Count && i < labels.Count; i++)
            {
                var text = labelLayer.Children[i];
                float fraction = count > 1 ? Math.Clamp(labels[i].Index / (float)(count - 1), 0, 1) : 0;

                // Where the thumb's middle is when that item is selected.
                float along = carousel.ScrollbarThumbInset + thumbLen / 2f + fraction * travel(thumbLen);

                text.Anchor = orientation == CarouselOrientation.Vertical ? Anchor.TopCentre : Anchor.CentreLeft;
                text.Position = orientation == CarouselOrientation.Vertical ? new Vector2(0, along) : new Vector2(along, 0);
                text.Color = carousel.ScrollbarLabelColor;
            }
        }

        // Thumb spans its share of the track (inside its inset ends), floored at the minimum but never
        // longer than the track.
        private float thumbLength()
        {
            float room = Math.Max(1f, trackLength - carousel.ScrollbarThumbInset * 2);
            return Math.Min(room, Math.Max(carousel.ScrollbarMinThumb, thumbFraction * room));
        }

        // How far the thumb moves from one end to the other.
        private float travel(float thumbLen) => Math.Max(1f, trackLength - carousel.ScrollbarThumbInset * 2 - thumbLen);

        private float progressFromPointer(Vector2 screenSpacePosition)
        {
            var local = ToLocalSpace(screenSpacePosition);
            float thumbLen = thumbLength();
            float pos = orientation == CarouselOrientation.Vertical ? local.Y : local.X;
            // Centre the thumb on the cursor.
            return Math.Clamp((pos - carousel.ScrollbarThumbInset - thumbLen / 2f) / travel(thumbLen), 0, 1);
        }

        public override bool OnHover(MouseEvent e) => visible;

        public override bool OnDragStart(MouseButtonEvent e)
        {
            if (!visible)
                return false;

            dragging = true;
            carousel.beginScrollbarScrub();
            carousel.scrollbarScrubTo(progressFromPointer(e.ScreenSpaceMousePosition));
            return true;
        }

        public override bool OnDrag(MouseEvent e)
        {
            if (!dragging)
                return false;

            carousel.scrollbarScrubTo(progressFromPointer(e.ScreenSpaceMousePosition));
            return true;
        }

        public override bool OnDragEnd(MouseButtonEvent e)
        {
            if (!dragging)
                return false;

            dragging = false;
            carousel.endScrollbarScrub();
            return true;
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            if (!visible)
                return false;

            carousel.scrollbarJumpTo(progressFromPointer(e.ScreenSpaceMousePosition));
            return true;
        }
    }

    /// <summary>
    /// A delayed-load slot whose "on screen" test is driven by the carousel's scroll position rather
    /// than masking, so the pop-out selected card is never clipped.
    /// </summary>
    private partial class CarouselCardWrapper : DelayedLoadUnloadWrapperContainer
    {
        public TModel Model = null!;

        /// <summary>
        /// This card's live index in the carousel, kept in sync via <see cref="reindex"/>.
        /// </summary>
        public int Index;

        // Animation offsets layered on top of the resting layout: extra X/Y (px) and an alpha multiplier.
        public float AnimX;
        public float AnimY;
        public float AnimAlpha = 1f;

        /// <summary>
        /// Screen position captured when the card starts exiting, from which it drops away.
        /// </summary>
        public Vector2 FrozenPosition;

        public bool IsAnimating { get; private set; }

        private double animStart;
        private double animDuration;
        private float xFrom, xTo, yFrom, yTo, aFrom, aTo;

        private readonly Func<bool> onScreen;
        private readonly Func<double> slotsFromCentre;
        private readonly Func<double> loadDelayPerSlot;

        public CarouselCardWrapper(Func<Drawable> createContent, Func<bool> onScreen, Func<double> slotsFromCentre, Func<double> loadDelayPerSlot, double timeBeforeLoad, double timeBeforeUnload)
            : base(createContent, timeBeforeLoad, timeBeforeUnload)
        {
            this.onScreen = onScreen;
            this.slotsFromCentre = slotsFromCentre;
            this.loadDelayPerSlot = loadDelayPerSlot;
        }

        /// <summary>
        /// Tweens the animation offsets from their current values to the given targets over
        /// <paramref name="duration"/> ms, easing out.
        /// </summary>
        public void AnimateTo(float animX, float animY, float animAlpha, double duration, double now)
        {
            xFrom = AnimX;
            yFrom = AnimY;
            aFrom = AnimAlpha;
            xTo = animX;
            yTo = animY;
            aTo = animAlpha;
            animStart = now;
            animDuration = Math.Max(1, duration);
            IsAnimating = true;
        }

        /// <summary>
        /// Advances the current tween (if any) to time <paramref name="now"/>.
        /// </summary>
        public void StepAnimation(double now)
        {
            if (!IsAnimating)
                return;

            float p = (float)Math.Clamp((now - animStart) / animDuration, 0, 1);
            float e = 1f - (float)Math.Pow(1f - p, 5); // ease-out quint

            AnimX = xFrom + (xTo - xFrom) * e;
            AnimY = yFrom + (yTo - yFrom) * e;
            AnimAlpha = aFrom + (aTo - aFrom) * e;

            if (p >= 1f)
                IsAnimating = false;
        }

        protected override bool IsOnScreen => onScreen();

        /// <summary>
        /// Scales the required visible time by distance from the centre, so the selected card loads
        /// first and the periphery follows. Re-evaluated per frame by the base container, so a card
        /// scrolling towards the centre gets progressively easier to trigger.
        /// </summary>
        protected override double LoadDelay => TimeBeforeLoad + loadDelayPerSlot() * Math.Abs(slotsFromCentre());
    }
}
