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
    /// Continuous on-screen time (ms) a slot must accumulate before its card is built. "On screen"
    /// reaches a little way past the carousel's ends, and further ahead of a moving list (see
    /// <see cref="PreloadSlots"/>), so a card is usually built before it comes into view.
    /// </summary>
    public double TimeBeforeLoad { get; set; } = 30;

    /// <summary>
    /// Extra on-screen time (ms) required per slot of distance from the centre before a card is built
    /// while the list moves faster than <see cref="BuildVelocity"/>, so the cards it passes aren't all
    /// built.
    /// </summary>
    /// <remarks>
    /// Building a card is cheap (it comes from a pool, and a card's cover waits for the list to slow; see
    /// <see cref="CarouselMotion.Speed"/>), so up to a fast scroll every card on the way is built. Past
    /// that (a long jump, the scrubber, a hard fling) a card is gone before it could be seen; scaling the
    /// wait by distance still lets the ones near the centre in, and the ones where the list is going (a
    /// jump's landing) don't wait at all. Eased in with the speed, up to twice <see cref="BuildVelocity"/>.
    /// Set to 0 to build at any speed.
    /// </remarks>
    public double LoadDelayPerSlot { get; set; } = 60;

    /// <summary>
    /// The fastest (items a second) the list can move and still build every card it passes: a held key
    /// or a spun wheel is well under it.
    /// </summary>
    public double BuildVelocity { get; set; } = 120;

    /// <summary>
    /// How many slots ahead of a moving list its cards are built, before they come into view: as many as
    /// it covers in <see cref="preload_time"/>, up to this. A built card is kept as far past the screen,
    /// either way (see <see cref="isKept"/>).
    /// </summary>
    public double PreloadSlots { get; set; } = 4;

    private const double preload_time = 250;

    /// <summary>
    /// Continuous time (ms) a built card must spend more than <see cref="PreloadSlots"/> past the screen
    /// before it's returned to the pool. None: past there it's out of sight, a card back from the pool
    /// comes back as it was, its cover kept (see <see cref="RecentCoverTextures"/>), and a fast scroll
    /// passes a lot of cards, which held on to for any time outnumber the pool (see <see cref="PoolSize"/>).
    /// </summary>
    public double TimeBeforeUnload { get; set; }

    /// <summary>
    /// How many cards the pool keeps to hand out again. Any more built at once are made new, and thrown
    /// away when they're let go.
    /// </summary>
    public const int PoolSize = 48;

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
    /// When true, a card eases in once it has loaded instead of popping: with the entrance while the
    /// region's own entrance is still playing (<see cref="AnimateEntrance"/>), and after that, when it's a
    /// card scrolled or jumped into view, with a quick fade in place (see <see cref="LoadFadeDuration"/>).
    /// Either way it waits for its cover first (see <see cref="RevealWait"/>). Toggled by the host via
    /// <see cref="AnimateEntrance"/> / <see cref="AnimateExit"/>.
    /// </summary>
    public bool PlayLoadEntrance { get; set; }

    /// <summary>
    /// Duration (ms) of the fade a card loaded while browsing comes in with. Short, and without the
    /// entrance's slide, so a list that's just jumped fills in place rather than flying in row by row.
    /// </summary>
    public double LoadFadeDuration { get; set; } = 240;

    /// <summary>
    /// Duration (ms) of the fade a card comes in with when it's built in view while the list moves (one
    /// built ahead of it comes in already there).
    /// </summary>
    public double MovingFadeDuration { get; set; } = 120;

    /// <summary>
    /// How far (px) a card loaded while browsing drifts out from the focus window as it fades in, so the
    /// list unfolds from the window as it fills in.
    /// </summary>
    public float RevealDrift { get; set; } = 10;

    /// <summary>
    /// How long (ms) a loaded card waits, hidden, for what it shows at a glance (its cover; see
    /// <see cref="CarouselCard{TModel}.IsReady"/>) before coming in without it. A row then comes in whole
    /// rather than with a placeholder its cover fades over a moment later.
    /// </summary>
    public double RevealWait { get; set; } = 150;

    /// <summary>
    /// Delay (ms) per slot further from the window between cards coming in together, so a list filling in
    /// after a jump ripples out from the window rather than appearing all at once.
    /// </summary>
    public double RevealStagger { get; set; } = 18;

    /// <summary>
    /// When (clock time) the ripple cards are coming in on would have been at the window: a card <c>d</c>
    /// slots out comes in at <c>rippleStart + d × RevealStagger</c>, or straight away once that's passed. A
    /// ripple runs on while cards keep coming in (see <see cref="ripple_gap"/>).
    /// </summary>
    private double rippleStart = double.MinValue;

    private double lastReveal = double.MinValue;

    /// <summary>
    /// How long (ms) after the last card came in another one still joins its ripple.
    /// </summary>
    private const double ripple_gap = 200;

    private readonly List<(CarouselCardWrapper Wrapper, bool InView)> revealing = new List<(CarouselCardWrapper, bool)>();

    /// <summary>
    /// Until when (clock time) a card that finishes loading joins the region's entrance rather than
    /// fading in place: the entrance's length, its last card's stagger included.
    /// </summary>
    private double entranceUntil = double.MinValue;

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

    /// <summary>
    /// Holds the cards, behind the focus window: the carousel's height, and wider than it by
    /// <see cref="row_bleed"/> each side, so a card sliding in from the side or its shadow isn't cut.
    /// Masked, fading the cards at its ends, when the layout asks for that (see
    /// <see cref="CarouselLayoutStrategy.MaskFade"/>).
    /// </summary>
    private Container rows = null!;

    private const float row_bleed = 120;

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

    // A trackpad's scroll so far towards the next card, in cards (+ on, - back).
    private double scrollRemainder;

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

    /// <summary>
    /// Items a second past which the list skims.
    /// </summary>
    private const double skim_velocity = 9;

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

    /// <summary>
    /// How far the card for the item at <paramref name="index"/> is from its slot right now (an
    /// entrance's slide, say), or null while it isn't built.
    /// </summary>
    public Vector2? CardOffset(int index) =>
        index >= 0 && index < wrappers.Count && wrappers[index].DelayedLoadCompleted
            ? new Vector2(wrappers[index].AnimX, wrappers[index].AnimY)
            : null;

    /// <summary>
    /// How opaque the card for the item at <paramref name="index"/> is drawn right now, or null while it
    /// isn't built.
    /// </summary>
    public float? CardAlpha(int index) =>
        index >= 0 && index < wrappers.Count && wrappers[index].DelayedLoadCompleted ? wrappers[index].Alpha : null;

    /// <summary>
    /// How many cards are built right now.
    /// </summary>
    public int CardsBuilt
    {
        get
        {
            int built = 0;

            foreach (var w in wrappers)
            {
                if (w.DelayedLoadCompleted)
                    built++;
            }

            return built;
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
        createRows();
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
    /// Builds the cards' layer (see <see cref="rows"/>). Recreated by <see cref="SetItems"/> because
    /// <c>Clear()</c> tears down all children.
    /// </summary>
    private void createRows() => Add(rows = new Container
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre
    });

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

        pool = new DrawablePool<TCard>(12, PoolSize);
        Add(pool);

        // Clear() above tore down the cards' layer, the previous focus window and the scrollbar, so
        // rebuild them.
        createRows();
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
            () => isKept(wrapper.Index),
            // None where the list is going: a long jump builds where it lands as it gets there.
            () => isAroundTarget(wrapper.Index, metrics()) ? 0 : wrapper.Index - position,
            () => LoadDelayPerSlot * rush,
            TimeBeforeLoad,
            TimeBeforeUnload)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = layout.SlotSize(metrics()),
            Model = item,
            Index = index
        };

        // Ease each card in once its content loads, so cards that stream in after the region is shown (or
        // are scrolled into view) don't just pop. It waits, hidden, for its cover first: see revealReadyCards.
        // Batch (re)shows drive this via AnimateEntrance.
        wrapper.DelayedLoadComplete += _ =>
        {
            if (PlayLoadEntrance)
                cardLoaded(wrapper);
        };
        wrapper.ContentUnloaded += () => wrapper.RevealSince = null;

        wrappers.Add(wrapper);
        rows.Add(wrapper);
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

        pool = new DrawablePool<TCard>(12, PoolSize);
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

            // Put there, not moved there: no speed to skim on or lead with, and the window shows it at once.
            motionFrom = clamped;
            updateLayout(instant: true);
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
    /// How fast the list is moving, 0 (settled) to 1 (as fast as skimming, or faster).
    /// </summary>
    private double flight => Math.Clamp(Math.Abs(smoothedVelocity) / skim_velocity, 0, 1);

    /// <summary>
    /// How far past <see cref="BuildVelocity"/> the list is moving, 0 to 1 (at twice it): how much of
    /// <see cref="LoadDelayPerSlot"/> a card waits.
    /// </summary>
    private double rush => Math.Clamp((Math.Abs(smoothedVelocity) - BuildVelocity) / BuildVelocity, 0, 1);

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
        entranceUntil = Clock.CurrentTime + EntranceDuration + max_stagger_steps * EntranceStagger;
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

            // This takes over from a card still waiting to come in.
            w.RevealSince = null;

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

        int steps = scrollSteps(e);
        if (steps == 0)
            return true;

        int before = currentIndex;
        for (int i = 0; i < Math.Abs(steps); i++)
            recordStep();
        SetIndex(currentIndex + steps);
        Scrolled?.Invoke(Math.Sign(steps), currentIndex != before);

        return true;
    }

    /// <summary>
    /// How many cards a scroll moves on (+) or back (-): one a wheel's notch, with a trackpad's fractions of one
    /// added up until they make a whole. A trackpad that says how far its fingers moved moves a card each time they
    /// cover one, as a drag would, rather than one an event (it sends many small ones).
    /// </summary>
    private int scrollSteps(ScrollEvent e)
    {
        double cards;

        if (e.AbsoluteDelta is Vector2 absolute)
        {
            Vector2 local = toLocalDelta(e.ScreenSpaceMousePosition, absolute);

            // Up and down move the strip on too, as a wheel does.
            if (Orientation.Value == CarouselOrientation.Horizontal && Math.Abs(local.Y) > Math.Abs(local.X))
                local = new Vector2(local.Y, 0);

            cards = layout.DragToPositionDelta(local, metrics());
        }
        else
            cards = -e.ScrollDelta.Y;

        if (cards == 0)
            return 0;

        // What's left over counts towards the next card in the same direction only.
        if (Math.Sign(cards) != Math.Sign(scrollRemainder))
            scrollRemainder = 0;

        scrollRemainder += cards;

        // Fractions that make a whole (ten tenths, say) can add up to a hair short of it.
        int steps = (int)Math.Truncate(scrollRemainder + Math.Sign(scrollRemainder) * 1e-6);
        scrollRemainder -= steps;
        return steps;
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

        position += layout.DragToPositionDelta(toLocalDelta(e.ScreenSpaceMousePosition, e.Delta), metrics());
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

    /// <summary>
    /// A screen-space distance at a point, in the carousel's own units: smaller on screen where the game is scaled
    /// down (a phone), so a card's pitch is as far as the finger has to go.
    /// </summary>
    private Vector2 toLocalDelta(Vector2 screenSpacePosition, Vector2 screenSpaceDelta) =>
        ToLocalSpace(screenSpacePosition) - ToLocalSpace(screenSpacePosition - screenSpaceDelta);

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

        revealReadyCards(now);

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
                        || Math.Abs(smoothedVelocity) > skim_velocity;

            if (fast)
                skimUntil = now + skim_hold;
        }

        // Quick in, gentler out.
        float skimTarget = now < skimUntil ? 1 : 0;
        float skim = Motion.Skim + (skimTarget - Motion.Skim) * (float)(1 - Math.Exp(-dt / (skimTarget > 0 ? 70 : 140)));
        Motion.Skim = skim < 0.002f ? 0 : skim > 0.998f ? 1 : skim;

        Motion.Speed = (float)Math.Abs(smoothedVelocity);

        float lead = Motion.Lead + ((float)Math.Clamp(smoothedVelocity / 12, -1, 1) - Motion.Lead) * (float)(1 - Math.Exp(-dt / 260));
        Motion.Lead = Math.Abs(lead) < 0.001f ? 0 : lead;
    }

    /// <summary>
    /// A card has loaded. Out of view (built ahead of the list, or past the fade at its ends), it comes in
    /// already there once its cover's in, and scrolls into view whole. In view while the list moves, it
    /// comes in at once rather than leave a gap. Otherwise it waits, hidden, for its cover (see
    /// <see cref="revealReadyCards"/>).
    /// </summary>
    /// <remarks>
    /// Decided straight away: this frame's layout is done, and would show the card as it laid it out (in
    /// full) until the next.
    /// </remarks>
    private void cardLoaded(CarouselCardWrapper wrapper)
    {
        double now = Clock.CurrentTime;
        var m = metrics();

        bool inView = isInView(wrapper.Index, m);
        bool moving = flight >= 0.5;

        if (inView ? moving : isReady(wrapper))
            comeIn(wrapper, inView, moving, now, now);
        else
            wrapper.RevealSince = now;

        wrapper.Alpha = wrapper.RevealSince != null ? 0 : layout.Compute((float)(wrapper.Index - position), m).Alpha * wrapper.AnimAlpha;
    }

    /// <summary>
    /// Brings in the cards waiting for their covers: once the cover's in; as one comes into view while the
    /// list moves; or, in view at rest, after <see cref="RevealWait"/> without it. Cards coming in together
    /// at rest ripple out from the window (see <see cref="RevealStagger"/>).
    /// </summary>
    private void revealReadyCards(double now)
    {
        revealing.Clear();

        var m = metrics();
        bool moving = flight >= 0.5;

        foreach (var w in wrappers)
        {
            if (w.RevealSince is not double since)
                continue;

            bool inView = isInView(w.Index, m);

            if (isReady(w) || (inView && (moving || now - since >= RevealWait)))
                revealing.Add((w, inView));
        }

        if (revealing.Count == 0)
            return;

        if (now - lastReveal > ripple_gap)
        {
            double nearest = double.MaxValue;

            foreach (var (w, _) in revealing)
                nearest = Math.Min(nearest, Math.Abs(w.Index - position));

            rippleStart = now - nearest * RevealStagger;
        }

        lastReveal = now;

        foreach (var (w, inView) in revealing)
            comeIn(w, inView, moving, now, Math.Max(now, rippleStart + Math.Min(max_stagger_steps, Math.Abs(w.Index - position)) * RevealStagger));
    }

    private bool isReady(CarouselCardWrapper wrapper) => wrapper.Content is not TCard card || card.IsReady;

    /// <summary>
    /// Brings a loaded card in: out of view, already there; with the entrance while the region's is playing;
    /// with a quick fade while the list moves; and otherwise in place at <paramref name="at"/>, drifting out
    /// from the window.
    /// </summary>
    private void comeIn(CarouselCardWrapper w, bool inView, bool moving, double now, double at)
    {
        w.RevealSince = null;

        // Nothing to see it come in.
        if (!inView)
        {
            w.AnimAlpha = 1f;
            return;
        }

        if (now < entranceUntil)
        {
            playEntrance(w, at);
            return;
        }

        w.AnimAlpha = 0f;

        if (moving)
        {
            w.AnimateTo(0f, 0f, 1f, MovingFadeDuration, now);
            return;
        }

        // A little nearer the window than its slot: whatever glide it's in the middle of carries on.
        var m = metrics();
        var away = layout.Compute((float)(w.Index - position), m).Position - layout.Compute(0, m).Position;
        float distance = MathF.Sqrt(away.X * away.X + away.Y * away.Y);

        if (distance > 1)
        {
            w.AnimX -= away.X / distance * RevealDrift;
            w.AnimY -= away.Y / distance * RevealDrift;
        }

        w.AnimateTo(0f, 0f, 1f, LoadFadeDuration, at);
    }

    /// <summary>
    /// Whether the card for the item at <paramref name="index"/> shows clearly right now: inside the
    /// carousel and past the fade at its ends, and not under the focus window.
    /// </summary>
    private bool isInView(int index, CarouselMetrics m)
    {
        var l = layout.Compute((float)(index - position), m);

        if (l.Alpha < 0.25f)
            return false;

        var size = layout.SlotSize(m);
        var fade = layout.MaskFade ?? default;

        return l.Position.X + size.X / 2 > -DrawWidth / 2 + fade.Left && l.Position.X - size.X / 2 < DrawWidth / 2 - fade.Right
               && l.Position.Y + size.Y / 2 > -DrawHeight / 2 + fade.Top && l.Position.Y - size.Y / 2 < DrawHeight / 2 - fade.Bottom;
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
                rows.Remove(w);
                exitingWrappers.RemoveAt(i);
            }
        }
    }

    private double clampWithOverscroll(double value)
    {
        const double overscroll = 0.6;
        return Math.Clamp(value, -overscroll, items.Count - 1 + overscroll);
    }

    /// <param name="instant">The list was put where it is rather than moved there: the focus window shows
    /// the item there at once.</param>
    private void updateLayout(bool instant = false)
    {
        var m = metrics();
        var slotSize = layout.SlotSize(m);

        var fade = layout.MaskFade;
        rows.Size = new Vector2(DrawWidth + row_bleed * 2, DrawHeight);
        rows.Masking = fade != null;
        rows.MaskingFade = fade ?? default;

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

            // Loaded, but waiting for its cover: hidden until it comes in.
            w.Alpha = w.RevealSince != null ? 0 : l.Alpha * w.AnimAlpha;

            w.Depth = -Math.Abs((float)(i - position));
        }

        var window = layout.FocusWindow(m);
        focusHost.Position = window.Location;
        focusHost.Size = window.Size;
        focusWindow?.Display(items, position, instant);

        updateScrollbar();
    }

    /// <summary>
    /// Whether the slot for the given index is close enough to the centre to be worth loading: on screen
    /// now, or where the list's speed takes it in <see cref="preload_time"/> (up to
    /// <see cref="PreloadSlots"/> on), so a moving list's cards are built before they come into view.
    /// </summary>
    private bool isOnScreen(int index)
    {
        var m = metrics();

        if (layout.IsOnScreen((float)(index - position), m))
            return true;

        double ahead = Math.Clamp(smoothedVelocity * preload_time / 1000, -PreloadSlots, PreloadSlots);
        return Math.Abs(ahead) > 0.01 && layout.IsOnScreen((float)(index - position - ahead), m);
    }

    /// <summary>
    /// Whether the built card for the given index is kept: while it's on screen, or would be with the list
    /// up to <see cref="PreloadSlots"/> nearer it, so a card built ahead of the list isn't let go as the
    /// list slows short of it, nor one just scrolled off as the list turns back. However fast the list
    /// moves, only the cards around the screen are built.
    /// </summary>
    private bool isKept(int index)
    {
        float offset = (float)(index - position);
        float nearer = Math.Clamp(offset, -(float)PreloadSlots, (float)PreloadSlots);
        return layout.IsOnScreen(offset - nearer, metrics());
    }

    /// <summary>
    /// Whether the slot for the given index is on screen where the list is going (the item it's moving to),
    /// which is known before it gets there, except while it's dragged or scrubbed.
    /// </summary>
    private bool isAroundTarget(int index, CarouselMetrics m) =>
        !isDragging && !scrollbarScrubbing && layout.IsOnScreen(index - currentIndex, m);

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

        /// <summary>
        /// When (clock time) the card loaded, while it waits hidden for its cover before coming in; null
        /// once it has come in, or when it isn't to ease in at all.
        /// </summary>
        public double? RevealSince;

        public bool IsAnimating { get; private set; }

        private double animStart;
        private double animDuration;
        private float xFrom, xTo, yFrom, yTo, aFrom, aTo;

        private readonly Func<bool> onScreen;
        private readonly Func<bool> kept;
        private readonly Func<double> slotsFromCentre;
        private readonly Func<double> loadDelayPerSlot;

        public CarouselCardWrapper(Func<Drawable> createContent, Func<bool> onScreen, Func<bool> kept, Func<double> slotsFromCentre, Func<double> loadDelayPerSlot, double timeBeforeLoad, double timeBeforeUnload)
            : base(createContent, timeBeforeLoad, timeBeforeUnload)
        {
            this.onScreen = onScreen;
            this.kept = kept;
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

        // Built, a card stays on screen a few slots further than it had to come to be built (see isKept).
        protected override bool IsOnScreen => DelayedLoadCompleted ? kept() : onScreen();

        /// <summary>
        /// Scales the required visible time by distance from the centre while the list moves too fast to
        /// build every card (see <see cref="Carousel{TModel,TCard}.LoadDelayPerSlot"/>). Re-evaluated per
        /// frame by the base container, so a card scrolling towards the centre gets progressively easier
        /// to trigger.
        /// </summary>
        protected override double LoadDelay
        {
            get
            {
                // Asked every frame of every card not built yet, so the distance only when it counts.
                double perSlot = loadDelayPerSlot();
                return perSlot > 0 ? TimeBeforeLoad + perSlot * Math.Abs(slotsFromCentre()) : TimeBeforeLoad;
            }
        }
    }
}
