// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The reusable song-selection cluster: a beatmap-set carousel plus a difficulty carousel (with their
/// frosted band and the docked song), the two-stage set->difficulty selection state machine, the
/// debounced commit to the working beatmap, and the show/hide of the whole region. Hosting screens
/// (solo, and later multiplayer) supply their own chrome — song info panel, back/go buttons, screen
/// transitions — and drive this via its small public API + <see cref="SetChanged"/> /
/// <see cref="BeatmapChanged"/> / <see cref="StateChanged"/> events.
/// </summary>
public partial class BeatmapSelector : Container
{
    public enum SelectionState
    {
        SelectingSet,
        SelectingBeatmap
    }

    /// <summary>
    /// The current stage of selection.
    /// </summary>
    public SelectionState State { get; private set; } = SelectionState.SelectingSet;

    /// <summary>
    /// The current carousel orientation (so hosts can map input to the right axis).
    /// </summary>
    public CarouselOrientation Orientation => currentOrientation;

    /// <summary>
    /// Fired when the displayed/committed beatmap set changes.
    /// </summary>
    public event Action<BeatmapSet?>? SetChanged;

    /// <summary>
    /// Fired when the selected difficulty changes.
    /// </summary>
    public event Action<Beatmap?>? BeatmapChanged;

    /// <summary>
    /// Fired when <see cref="State"/> changes.
    /// </summary>
    public event Action<SelectionState>? StateChanged;

    /// <summary>
    /// Fired when the song list is rebuilt (the filter changed), e.g. to or from showing nothing.
    /// </summary>
    public event Action? ListChanged;

    /// <summary>
    /// Fired when the card already selected in the current stage's carousel is clicked again, which
    /// the host treats as moving on, as its "next" does.
    /// </summary>
    public event Action? SelectedCardClicked;

    /// <summary>
    /// Whether the song list shows anything; with nothing shown there's nothing to pick.
    /// </summary>
    public bool HasSongsListed => carousel != null && carousel.Count > 0;

    /// <summary>
    /// Whether the "no matching songs" placeholder is up (diagnostics/tests).
    /// </summary>
    public bool ShowingNoResults => noResults?.IsPresented ?? false;

    /// <summary>
    /// The set currently displayed (the committed working set, else the carousel's selection).
    /// </summary>
    public BeatmapSet? CurrentSet => workingBeatmap?.BeatmapSet ?? carousel?.CurrentItem.Value;

    /// <summary>
    /// The difficulty currently displayed (the working beatmap, else the difficulty carousel's selection).
    /// </summary>
    public Beatmap? CurrentBeatmap => workingBeatmap?.Beatmap ?? difficultyCarousel?.CurrentItem.Value;

    /// <summary>
    /// The lists end this far above the screen's bottom, leaving the back and go buttons below them.
    /// </summary>
    private const float region_bottom_gap = 98f;

    /// <summary>
    /// The horizontal strip's glass height, and how far above it the hero tile rises.
    /// </summary>
    private const float strip_height = 250f;

    private const float strip_rise = 100f;

    private const double region_slide_duration = 500;

    /// <summary>
    /// Fraction of the screen width the carousel region occupies in vertical mode.
    /// </summary>
    private const float vertical_region_fraction = 0.5f;

    /// <summary>
    /// Hard cap (px) on the vertical region width, so panels don't grow huge on ultrawide windows.
    /// </summary>
    private const float vertical_region_max_width = 640f;

    private const double selection_commit_delay = 300;

    // The frosted glass both lists sit on: the rail (vertical) or the strip (horizontal).
    private FrostedBand band = null!;
    private Container carouselContainer = null!;
    private BeatmapSetCarousel carousel = null!;
    private Container difficultyContainer = null!;

    // The song being picked from, docked at the start of the difficulty list.
    private DockedSong dock = null!;

    // Shown where the song list would be when the filter lets nothing through.
    private NoResultsPlaceholder noResults = null!;

    // A song picked from outside (the music player) that the filter would hide: listed anyway
    // so the list holds what's playing and Go picks what's shown. Dropped when the filter changes.
    private BeatmapSet? forcedSet;
    private BeatmapCarousel difficultyCarousel = null!;

    private CarouselOrientation currentOrientation = CarouselOrientation.Horizontal;
    private bool regionShown;
    private Reactive<CarouselOrientation>? orientationSetting;

    private BeatmapSet? pendingBeatmapSet;
    private double lastSelectionChangeTime;
    private bool selectionCommitted = true;

    private NavigationSoundPlayer? navigationSounds;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    private BeatmapFilterControl filterControl = null!;
    private Reactive<BeatmapSort>? sortSetting;

    // The filter, shared with what's listed: a song's level bars dim the difficulties it leaves out.
    [Cached]
    private readonly SongListFilter listFilter = new SongListFilter();

    /// <summary>The search and filters in effect.</summary>
    public BeatmapFilter Filter => filterControl.Filter;

    /// <summary>How many songs the filter lets through (what the carousel holds).</summary>
    public int MatchCount { get; private set; }

    /// <summary>Whether the search box has the keyboard (song select then leaves Space and letters to it).</summary>
    public bool IsSearchFocused => filterControl.IsSearchFocused;

    /// <summary>Puts the cursor in the search box.</summary>
    public void FocusSearch() => filterControl.FocusSearch();

    /// <summary>
    /// Where the horizontal strip begins, in screen space (the top of the hero tile rising out of it) —
    /// what song select's info panel has to stay above. Infinite in the vertical layout, where the list is
    /// off to the side.
    /// </summary>
    public float StripTop => currentOrientation == CarouselOrientation.Horizontal
        ? carouselContainer.DrawRectangle.Y + carousel.FocusWindowRect.Y
        : float.PositiveInfinity;

    /// <summary>
    /// Where the selector's side area begins, in screen space: the list's column (vertical) or the
    /// filter bar (horizontal). Song select keeps its info panel left of it.
    /// </summary>
    public float SideAreaLeft => currentOrientation == CarouselOrientation.Vertical
        ? carouselContainer.DrawRectangle.X
        : filterControl.DrawRectangle.X;

    /// <summary>The search and filter panel (diagnostics/tests).</summary>
    public Drawable FilterArea => filterControl;

    /// <summary>The song list (diagnostics/tests).</summary>
    public Drawable ListArea => carousel;

    /// <summary>Searches for <paramref name="text"/>, as typing it in the search box would.</summary>
    public void Search(string text) => filterControl.SetSearch(text);

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    public BeatmapSelector()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette, [CanBeNull] IAudioStore<ISample> sampleStore)
    {
        navigationSounds = new NavigationSoundPlayer(sampleStore?.Get("UI/scroll-previous.wav"), sampleStore?.Get("UI/scroll-next.wav"));

        var palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        Add(band = new FrostedBand { Alpha = 0 });

        Add(carouselContainer = new Container
        {
            Alpha = 0,
            Children = new Drawable[]
            {
                carousel = new BeatmapSetCarousel(),
                noResults = new NoResultsPlaceholder(palette)
                {
                    RequestClearSearch = () => filterControl.SetSearch(string.Empty),
                    RequestClearLevelRange = () => filterControl.ClearLevelRange(),
                    RequestClearLanes = () => filterControl.ClearLanes()
                }
            }
        });

        Add(difficultyContainer = new Container
        {
            Alpha = 0,
            Child = difficultyCarousel = new BeatmapCarousel()
        });

        Add(dock = new DockedSong
        {
            Alpha = 0,
            Action = () =>
            {
                if (State == SelectionState.SelectingBeatmap)
                    ExitToSets();
            }
        });

        // On the light glass the scrollbars are ink, not white.
        carousel.ScrollbarTrackColor = difficultyCarousel.ScrollbarTrackColor = palette.Text.WithAlpha(0.07f);
        carousel.ScrollbarThumbColor = difficultyCarousel.ScrollbarThumbColor = palette.Control.WithAlpha(0.5f);

        carousel.CurrentItem.BindValueChanged(onCarouselSetChanged);
        difficultyCarousel.CurrentItem.BindValueChanged(onCarouselBeatmapChanged);

        // Over a carousel the wheel goes to the carousel rather than the screen, so it bypasses
        // NavigateNext/Previous; give it their tick too.
        carousel.Scrolled += playNavigationSound;
        difficultyCarousel.Scrolled += playNavigationSound;

        // Clicking a card selects it; clicking the selected one again moves on. The moving on has its
        // own sound, so it doesn't get the select one too.
        carousel.Tapped += _ => uiSounds?.Play(UISound.Select);
        difficultyCarousel.Tapped += _ => uiSounds?.Play(UISound.Select);
        carousel.SelectedTapped += () => onSelectedCardTapped(SelectionState.SelectingSet);
        difficultyCarousel.SelectedTapped += () => onSelectedCardTapped(SelectionState.SelectingBeatmap);

        // The set cards carry cover art, so a moderate scroll should decode only the cards you land
        // near, not every one you pass over. Rather than one flat delay large enough for the worst case
        // (which also made the card under the cursor slow to appear), the floor is short and the cost
        // scales with distance from the centre: the selection loads promptly, cards several slots out
        // need to stay visible far longer than a scroll allows. The difficulty carousel's cards are
        // text-only, so it keeps the snappy flat default.
        carousel.TimeBeforeLoad = 80;
        carousel.LoadDelayPerSlot = 60;

        Add(filterControl = new BeatmapFilterControl { Alpha = 0 });

        if (config != null)
        {
            sortSetting = config.Get<BeatmapSort>(YuukiSetting.SongSelectSort);
            filterControl.SetSort(sortSetting.Value);
        }

        listFilter.Set(filterControl.Filter);
        filterControl.FilterChanged += _ => applyFilter();

        var library = getDisplayBeatmapSets();
        refreshLanes(library);

        var sets = Filter.Apply(library, difficultiesOf, forcedSet);
        carousel.SetItems(sets);
        updateMatchCount(sets.Count, library);

        if (workingBeatmap != null)
        {
            if (workingBeatmap.BeatmapSet == null || (workingBeatmap.BeatmapSet.Hide && beatmapsCollection.IsNotNull()))
            {
                var initial = beatmapsCollection?.GetRandomBeatmapSet() ?? sets.FirstOrDefault();
                if (initial != null)
                    workingBeatmap.BeatmapSet = initial;
            }

            if (workingBeatmap.BeatmapSet != null)
            {
                int index = sets.FindIndex(s => s.ID == workingBeatmap.BeatmapSet.ID);
                if (index >= 0)
                    carousel.SetIndex(index, animate: false);
            }

            selectionCommitted = true;

            // Drawable.BindValueChanged, not the reactive's own: these are DI singletons that outlive every
            // song-select visit, and a handler subscribed directly would keep this selector — and its
            // carousels, cards and pools — alive for the process lifetime.
            BindValueChanged(workingBeatmap.ReactiveWorkingBeatmapSet, onWorkingBeatmapSetChanged, true);
            BindValueChanged(workingBeatmap.ReactiveWorkingBeatmap, onWorkingBeatmapChanged, true);
        }

        if (config != null)
        {
            orientationSetting = config.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation);
            BindValueChanged(orientationSetting, onOrientationSettingChanged, true);
        }
        else
        {
            applyOrientation(CarouselOrientation.Horizontal);
        }
    }

    private void onOrientationSettingChanged(ValueChangedEvent<CarouselOrientation> e) => applyOrientation(e.NewValue);

    /// <summary>
    /// Reshapes the whole region for a new orientation: forwards it to both carousels and the dock, and
    /// swaps the region between the rail (right) and the strip (bottom). Keeps the region's shown/hidden
    /// state so a live switch while on screen stays visible.
    /// </summary>
    private void applyOrientation(CarouselOrientation orientation)
    {
        currentOrientation = orientation;
        carousel.Orientation.Value = orientation;
        difficultyCarousel.Orientation.Value = orientation;
        dock.SetOrientation(orientation);

        bool vertical = orientation == CarouselOrientation.Vertical;

        band.RimEdge = vertical ? Edges.Left : Edges.Top;

        foreach (var region in new Drawable[] { band, carouselContainer, difficultyContainer })
            region.Anchor = region.Origin = vertical ? Anchor.TopRight : Anchor.BottomCentre;

        dock.Anchor = dock.Origin = vertical ? Anchor.TopRight : Anchor.BottomLeft;

        layoutRegions();

        band.Position = carouselContainer.Position = difficultyContainer.Position = regionShown ? shownPosition : hiddenPosition;
        dock.Position = regionShown ? dockShownPosition : dockHiddenPosition;

        noResults.SetLayout(orientation);

        // The vertical song list starts below the filters rather than running under them, and the
        // difficulty list below the docked song. The strip's lists take the whole region: the hero tile
        // rises into its top part.
        carouselContainer.Padding = new MarginPadding { Top = vertical ? BeatmapFilterControl.HEIGHT + filter_gap : 0 };
        difficultyContainer.Padding = new MarginPadding { Top = vertical ? DockedSong.CARD_HEIGHT + dock_margin * 2 : 0 };

        // The filters head the vertical rail, on its glass, or sit in a card in the top-right corner.
        filterControl.Anchor = filterControl.Origin = Anchor.TopRight;
        filterControl.SetLayout(orientation, filterWidth);
        filterControl.Position = regionShown ? filterShownPosition : filterHiddenPosition;
    }

    /// <summary>
    /// Sizes the band and both lists for the current orientation and window: the rail is a capped share of
    /// the width, its glass full height and its lists down to the buttons; the strip spans the width, with
    /// room above it for the hero.
    /// </summary>
    private void layoutRegions()
    {
        if (currentOrientation == CarouselOrientation.Vertical)
        {
            // The glass runs the screen's full height; the lists on it stop above the buttons.
            band.Size = new Vector2(verticalRegionWidth, DrawHeight);
            carouselContainer.Size = difficultyContainer.Size = new Vector2(verticalRegionWidth, Math.Max(0, DrawHeight - region_bottom_gap));
            dock.Size = new Vector2(Math.Max(0, verticalRegionWidth - dock_margin * 2), DockedSong.CARD_HEIGHT);
        }
        else
        {
            band.Size = new Vector2(DrawWidth, strip_height);
            carouselContainer.Size = difficultyContainer.Size = new Vector2(DrawWidth, strip_height + strip_rise);
            dock.Size = DockedSong.TileSize;
        }
    }

    /// <summary>The docked song's distance from the band's edges.</summary>
    private const float dock_margin = 16;

    private Vector2 dockShownPosition => currentOrientation == CarouselOrientation.Vertical
        ? new Vector2(-dock_margin, dock_margin)
        : new Vector2(24, -(region_bottom_gap + 40));

    private Vector2 dockHiddenPosition => currentOrientation == CarouselOrientation.Vertical
        ? dockShownPosition + new Vector2(verticalRegionWidth, 0)
        : dockShownPosition + new Vector2(0, strip_height + region_bottom_gap);

    /// <summary>The space between the filters and the vertical list below them.</summary>
    private const float filter_gap = 8;

    private float filterWidth => currentOrientation == CarouselOrientation.Vertical
        ? verticalRegionWidth
        : BeatmapFilterControl.CARD_WIDTH;

    /// <summary>The horizontal card's distance from the screen's top and right edges.</summary>
    private const float card_margin = 24;

    // Vertical: across the top of the rail. Horizontal: the top-right corner, framing the screen with the
    // info panel (top left) and the strip (bottom).
    private Vector2 filterShownPosition => currentOrientation == CarouselOrientation.Vertical
        ? Vector2.Zero
        : new Vector2(-card_margin, card_margin);

    private Vector2 filterHiddenPosition => currentOrientation == CarouselOrientation.Vertical
        ? filterShownPosition + new Vector2(verticalRegionWidth, 0)
        : filterShownPosition - new Vector2(0, BeatmapFilterControl.HEIGHT + card_margin);

    private Vector2 shownPosition => currentOrientation == CarouselOrientation.Vertical ? Vector2.Zero : new Vector2(0, -region_bottom_gap);

    private Vector2 hiddenPosition => currentOrientation == CarouselOrientation.Vertical
        ? new Vector2(verticalRegionWidth, 0)
        : new Vector2(0, strip_height + strip_rise);

    /// <summary>Vertical region width: half the screen, capped so it never gets absurdly wide.</summary>
    private float verticalRegionWidth => Math.Min(DrawWidth * vertical_region_fraction, vertical_region_max_width);

    private List<BeatmapSet> getDisplayBeatmapSets()
    {
        if (beatmapsCollection != null)
        {
            var visible = beatmapsCollection.BeatmapSets.Where(s => !s.Hide).ToList();
            if (visible.Count > 0)
                return visible;
        }

        return new List<BeatmapSet>();
    }

    /// <summary>
    /// Replaces the set of beatmaps shown (e.g. for a filtered list or a multiplayer-provided pool).
    /// </summary>
    public void SetBeatmapSets(IReadOnlyList<BeatmapSet> sets) => carousel.SetItems(sets);

    /// <summary>How many of <paramref name="sets"/> actually pass: all but a forced song that doesn't.</summary>
    private int countMatches(List<BeatmapSet> sets) =>
        forcedSet != null && sets.Contains(forcedSet) && !Filter.Passes(forcedSet, difficultiesOf(forcedSet))
            ? sets.Count - 1
            : sets.Count;

    private IReadOnlyList<Beatmap> difficultiesOf(BeatmapSet set) =>
        beatmapsCollection?.GetBeatmapsFromBeatmapSet(set) ?? Array.Empty<Beatmap>();

    /// <summary>
    /// Re-runs the search and filters: the carousel holds what passes, in the chosen order, keeping the
    /// current song selected when it still passes.
    /// </summary>
    private void applyFilter()
    {
        if (sortSetting != null)
            sortSetting.Value = Filter.Sort;

        // A new search or range is the player choosing what to see: a song only listed because the
        // music player picked it goes back to being judged like the rest.
        forcedSet = null;
        listFilter.Changed();

        showSets();
    }

    /// <summary>Lists what passes (plus a forced song), keeping the current song selected if it's listed.</summary>
    private void showSets()
    {
        List<BeatmapSet> library = getDisplayBeatmapSets();
        List<BeatmapSet> sets = Filter.Apply(library, difficultiesOf, forcedSet);
        BeatmapSet? current = workingBeatmap?.BeatmapSet ?? carousel.CurrentItem.Value;

        carousel.SetItems(sets);
        updateMatchCount(countMatches(sets), library);

        int index = current == null ? -1 : sets.FindIndex(s => s.ID == current.ID);
        if (index >= 0)
            carousel.SetIndex(index, animate: false);
    }

    /// <summary>
    /// Updates everything that counts what's listed: the header's count and level histogram, and the
    /// placeholder for an empty list. <paramref name="library"/> is every song that could be listed.
    /// </summary>
    private void updateMatchCount(int count, List<BeatmapSet> library)
    {
        MatchCount = count;
        filterControl.SetCounts(count, library.Count);
        filterControl.SetLevelCounts(Filter.LevelCounts(library, difficultiesOf));
        updateNoResults();
        ListChanged?.Invoke();
    }

    /// <summary>
    /// Gives the header a chip for each lane count in <paramref name="library"/>.
    /// </summary>
    private void refreshLanes(List<BeatmapSet> library) =>
        filterControl.SetLanes(library.SelectMany(difficultiesOf).Select(b => b.LaneCount));

    /// <summary>
    /// Shows the placeholder when nothing is listed: the music muffles for a moment as it
    /// appears, and in the vertical layout a shade comes up behind it so it reads over the background.
    /// </summary>
    private void updateNoResults()
    {
        if (carousel.Count > 0)
        {
            noResults.Dismiss();
            return;
        }

        if (!noResults.IsPresented)
        {
            audioManager?.DuckMomentarily(400, new DuckParameters
            {
                DuckCutoffTo = 500,
                DuckDuration = 250,
                RestoreDuration = 2000
            });
        }

        bool libraryEmpty = beatmapsCollection == null || !beatmapsCollection.BeatmapSets.Any(s => !s.Hide);
        noResults.Present(Filter, libraryEmpty);
    }

    private void onCarouselSetChanged(ValueChangedEvent<BeatmapSet?> e)
    {
        var set = e.NewValue;
        if (set == null)
            return;

        if (workingBeatmap == null)
        {
            SetChanged?.Invoke(set);
            return;
        }

        pendingBeatmapSet = set;
        lastSelectionChangeTime = Clock.CurrentTime;
        selectionCommitted = false;
    }

    private void onWorkingBeatmapSetChanged(ValueChangedEvent<BeatmapSet> e)
    {
        var set = e.NewValue;
        if (set == null)
            return;

        if (State == SelectionState.SelectingBeatmap && difficultyCarousel != null && !populateDifficulties(set))
        {
            difficultyCarousel.AnimateExit();
            carousel.AnimateEntrance();
            setState(SelectionState.SelectingSet);
        }

        SetChanged?.Invoke(set);

        if (carousel != null && !Equals(carousel.CurrentItem.Value, set))
        {
            // Picked from outside but hidden by the filter (or the list is empty): list it anyway.
            if (!carousel.Contains(set) && !set.Hide)
            {
                forcedSet = set;
                showSets();
            }

            carousel.SelectItem(set, true);
        }
    }

    private void onCarouselBeatmapChanged(ValueChangedEvent<Beatmap?> e)
    {
        var beatmap = e.NewValue;
        if (beatmap == null)
            return;

        if (workingBeatmap == null)
        {
            BeatmapChanged?.Invoke(beatmap);
            return;
        }

        if (!Equals(workingBeatmap.Beatmap, beatmap))
            workingBeatmap.Beatmap = beatmap;
    }

    private void onWorkingBeatmapChanged(ValueChangedEvent<Beatmap> e)
    {
        var beatmap = e.NewValue;
        if (beatmap == null)
            return;

        BeatmapChanged?.Invoke(beatmap);

        if (difficultyCarousel != null && !Equals(difficultyCarousel.CurrentItem.Value, beatmap))
            difficultyCarousel.SelectItem(beatmap, animate: true);
    }

    public override void Update()
    {
        base.Update();

        if (!selectionCommitted && Clock.CurrentTime - lastSelectionChangeTime >= selection_commit_delay)
        {
            selectionCommitted = true;
            commitSelection(pendingBeatmapSet);
        }

        // Keep the (screen-relative) region sizes in sync as the window resizes.
        float railWidth = carouselContainer.Width;
        layoutRegions();

        if (currentOrientation == CarouselOrientation.Vertical && Math.Abs(carouselContainer.Width - railWidth) > 0.5f)
            filterControl.SetLayout(CarouselOrientation.Vertical, filterWidth);
    }

    private void commitSelection(BeatmapSet? set)
    {
        if (set == null || workingBeatmap == null)
            return;

        if (!Equals(workingBeatmap.BeatmapSet, set))
            workingBeatmap.BeatmapSet = set;
    }

    /// <summary>
    /// Selects a random visible set and returns to set selection. No-op without a beatmap collection.
    /// </summary>
    public void SelectRandom()
    {
        setState(SelectionState.SelectingSet);

        if (beatmapsCollection == null)
            return;

        var set = beatmapsCollection.GetRandomBeatmapSet();
        if (set == null)
            return;

        if (workingBeatmap != null)
            workingBeatmap.BeatmapSet = set;
        else
            carousel.SelectItem(set, animate: true);
    }

    public void NavigateNext()
    {
        int before = activeIndex;

        if (State == SelectionState.SelectingSet)
            carousel.Next();
        else
            difficultyCarousel.Next();

        playNavigationSound(1, activeIndex != before);
    }

    public void NavigatePrevious()
    {
        int before = activeIndex;

        if (State == SelectionState.SelectingSet)
            carousel.Previous();
        else
            difficultyCarousel.Previous();

        playNavigationSound(-1, activeIndex != before);
    }

    private int activeIndex => State == SelectionState.SelectingSet ? carousel.CurrentIndex : difficultyCarousel.CurrentIndex;

    /// <summary>
    /// Only the carousel of the stage in play moves on. The other one is fading out (it still takes
    /// clicks until it's gone), and a click there is just a click.
    /// </summary>
    private void onSelectedCardTapped(SelectionState carouselStage)
    {
        if (State == carouselStage)
            SelectedCardClicked?.Invoke();
        else
            uiSounds?.Play(UISound.Select);
    }

    private void playNavigationSound(int direction, bool moved) => navigationSounds?.Play(direction, moved, Clock.CurrentTime);

    /// <summary>
    /// Commits any pending set selection and advances to difficulty selection. Returns false (and stays
    /// on set selection) if the current set has no playable beatmaps.
    /// </summary>
    public bool EnterDifficultySelection()
    {
        // Nothing listed (the search matched nothing): the song still playing isn't one to pick.
        if (carousel.Count == 0)
        {
            uiSounds?.Play(UISound.Denied);
            return false;
        }

        if (!selectionCommitted)
        {
            selectionCommitted = true;
            commitSelection(pendingBeatmapSet);
        }

        if (workingBeatmap?.BeatmapSet == null || !populateDifficulties(workingBeatmap.BeatmapSet))
            return false;

        uiSounds?.Play(UISound.Select);
        dockSong(workingBeatmap.BeatmapSet);
        carousel.AnimateExit();
        difficultyCarousel.AnimateEntrance();
        setState(SelectionState.SelectingBeatmap);
        return true;
    }

    /// <summary>
    /// Fills the difficulty list with <paramref name="set"/>'s difficulties, which also makes its first
    /// one the working beatmap. False, leaving the list as it was, when the set has none.
    /// </summary>
    private bool populateDifficulties(BeatmapSet set)
    {
        if (beatmapsCollection == null)
            return false;

        var beatmaps = beatmapsCollection.GetBeatmapsFromBeatmapSet(set);
        if (beatmaps.Length == 0)
            return false;

        // Only the difficulties the filter lets through, all of them if none do (the
        // song itself was picked another way, e.g. random).
        var passing = beatmaps.Where(Filter.Matches).ToArray();
        if (passing.Length > 0)
            beatmaps = passing;

        difficultyCarousel.SetItems(beatmaps);
        return true;
    }

    /// <summary>
    /// Lists the library again after it changed underneath (a beatmap saved in the editor puts new
    /// objects in it): the songs, and the difficulties when one is being picked, keeping the selection
    /// by ID.
    /// </summary>
    public void RefreshFromLibrary()
    {
        refreshLanes(getDisplayBeatmapSets());
        showSets();

        if (State != SelectionState.SelectingBeatmap || workingBeatmap?.BeatmapSet is not BeatmapSet set || !populateDifficulties(set))
            return;

        var beatmap = workingBeatmap.Beatmap == null ? null : beatmapsCollection?.GetBeatmapById(workingBeatmap.Beatmap.ID);

        if (beatmap != null)
            difficultyCarousel.SelectItem(beatmap, animate: false);
    }

    /// <summary>
    /// Returns to set selection from difficulty selection.
    /// </summary>
    public void ExitToSets()
    {
        uiSounds?.Play(UISound.Back);
        difficultyCarousel.AnimateExit();
        carousel.AnimateEntrance();
        setState(SelectionState.SelectingSet);
    }

    /// <summary>
    /// Resets to the set-selection stage without animating (used when (re)entering a screen).
    /// </summary>
    public void ResetToSets() => State = SelectionState.SelectingSet;

    private void setState(SelectionState newState)
    {
        if (State == newState)
        {
            updateStateVisibility(true);
            return;
        }

        State = newState;
        updateStateVisibility(true);
        StateChanged?.Invoke(newState);
    }

    private void updateStateVisibility(bool animate)
    {
        double duration = animate ? 400 : 0;

        if (State == SelectionState.SelectingSet)
        {
            carouselContainer.FadeIn(duration, Easing.OutQuint);
            difficultyContainer.FadeOut(duration, Easing.OutQuint);
            dock.FadeOut(duration, Easing.OutQuint);

            if (regionShown)
                filterControl.FadeIn(duration, Easing.OutQuint);
        }
        else
        {
            carouselContainer.FadeOut(duration, Easing.OutQuint);
            difficultyContainer.FadeIn(duration, Easing.OutQuint);

            if (regionShown)
                dock.FadeIn(duration, Easing.OutQuint);

            // The filters are for finding a song; with one picked they step aside (and let go of the keyboard).
            filterControl.FadeOut(duration, Easing.OutQuint);
            filterControl.ReleaseSearchFocus();
        }
    }

    /// <summary>
    /// Docks the song being picked from at the start of the difficulty list, flying it there from the
    /// song list's focus window so it reads as the same card moving.
    /// </summary>
    private void dockSong(BeatmapSet set)
    {
        dock.Show(set);

        dock.ClearTransforms();
        dock.Position = dockShownPosition;

        var window = carousel.FocusWindowRect;
        var from = ToLocalSpace(carousel.ToScreenSpace(window.Center)) - ToLocalSpace(dock.ToScreenSpace(dock.DrawSize / 2));

        dock.MoveTo(dockShownPosition + from)
            .MoveTo(dockShownPosition, 460, UserInterfaceConstant.SoftBounce);
    }

    /// <summary>
    /// Slides the region into view.
    /// </summary>
    public void ShowRegion()
    {
        regionShown = true;

        // No overshoot: the band is anchored to the screen's edge, and a bounce would open a gap there.
        var target = shownPosition;
        band.MoveTo(target, region_slide_duration, Easing.OutQuint)
            .FadeIn(region_slide_duration, Easing.OutQuint);
        carouselContainer.MoveTo(target, region_slide_duration, Easing.OutQuint);
        difficultyContainer.MoveTo(target, region_slide_duration, Easing.OutQuint);
        dock.MoveTo(dockShownPosition, region_slide_duration, Easing.OutQuint);
        // Its fade follows the state (it stays hidden while picking a difficulty).
        filterControl.MoveTo(filterShownPosition, region_slide_duration, Easing.OutQuint);

        updateStateVisibility(true);

        // Cascade the cards in on top of the region slide, so the whole thing doesn't just fade as a slab.
        if (State == SelectionState.SelectingBeatmap)
            difficultyCarousel.AnimateEntrance();
        else
            carousel.AnimateEntrance();
    }

    /// <summary>
    /// Slides the region out of view.
    /// </summary>
    public void HideRegion()
    {
        regionShown = false;

        var target = hiddenPosition;
        band.MoveTo(target, region_slide_duration, Easing.OutQuint)
            .FadeOut(region_slide_duration, Easing.OutQuint);
        carouselContainer.MoveTo(target, region_slide_duration, Easing.OutQuint)
            .FadeOut(region_slide_duration, Easing.OutQuint);
        difficultyContainer.MoveTo(target, region_slide_duration, Easing.OutQuint)
            .FadeOut(region_slide_duration, Easing.OutQuint);
        dock.MoveTo(dockHiddenPosition, region_slide_duration, Easing.OutQuint)
            .FadeOut(region_slide_duration, Easing.OutQuint);
        filterControl.MoveTo(filterHiddenPosition, region_slide_duration, Easing.OutQuint).FadeOut(300, Easing.OutQuint);

        // Cascade the cards out to match the entrance.
        carousel.AnimateExit();
        difficultyCarousel.AnimateExit();
    }
}
