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

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The reusable song-selection cluster: a beatmap-set carousel plus a difficulty carousel (with their
/// navigation chevrons and backdrop), the two-stage set->difficulty selection state machine, the
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

    private const float bar_hidden_y = 200f;
    private const float bar_shown_y = -98f;

    /// <summary>
    /// Fraction of the screen width the carousel region occupies in vertical mode.
    /// </summary>
    private const float vertical_region_fraction = 0.5f;

    /// <summary>
    /// Hard cap (px) on the vertical region width, so panels don't grow huge on ultrawide windows.
    /// </summary>
    private const float vertical_region_max_width = 640f;

    private const double selection_commit_delay = 300;

    private static readonly Color cream_color = ColorExtensions.FromHex("#DFCFBC");

    private Box menuBox = null!;
    private Container carouselContainer = null!;
    private BeatmapSetCarousel carousel = null!;
    private Container difficultyContainer = null!;

    // Shown where the song list would be when the filter lets nothing through, with a shade behind it
    // in the vertical layout, where the list floats over the background.
    private NoResultsPlaceholder noResults = null!;
    private Container noResultsShade = null!;

    // A song picked from outside (the music player) that the filter would hide: listed anyway
    // so the list holds what's playing and Go picks what's shown. Dropped when the filter changes.
    private BeatmapSet? forcedSet;
    private BeatmapCarousel difficultyCarousel = null!;

    // Every navigation chevron, tagged with whether it steps to the previous item, so the layout can
    // reposition/re-icon them (left/right vs. up/down) when the orientation flips.
    private readonly List<(ClickableContainer Button, IconSprite Icon, bool Previous)> chevrons = new List<(ClickableContainer, IconSprite, bool)>();

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

    /// <summary>The search and filters in effect.</summary>
    public BeatmapFilter Filter => filterControl.Filter;

    /// <summary>How many songs the filter lets through (what the carousel holds).</summary>
    public int MatchCount { get; private set; }

    /// <summary>Whether the search box has the keyboard (song select then leaves Space and letters to it).</summary>
    public bool IsSearchFocused => filterControl.IsSearchFocused;

    /// <summary>Puts the cursor in the search box.</summary>
    public void FocusSearch() => filterControl.FocusSearch();

    /// <summary>
    /// Where the horizontal strip begins, in screen space (its top edge) — what song select's info panel
    /// has to stay above. Infinite in the vertical layout, where the list is off to the side.
    /// </summary>
    public float StripTop => currentOrientation == CarouselOrientation.Horizontal ? carouselContainer.DrawRectangle.Y : float.PositiveInfinity;

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
        var textColor = palette.Text;
        var iconColor = textColor.Darken(0.2f);

        Add(menuBox = new Box
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Position = new Vector2(0, bar_hidden_y),
            Size = new Vector2(1, 150),
            Alpha = 0,
            Color = cream_color
        });

        Add(noResultsShade = new Container
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            RelativeSizeAxes = Axes.Y,
            Alpha = 0,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                ColorInfo = ColorInfo.GradientHorizontal(Color.Black.WithAlpha(0), Color.Black.WithAlpha(0.6f))
            }
        });

        Add(carouselContainer = new Container()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Position = new Vector2(0, bar_hidden_y),
            Size = new Vector2(1, 150),
            Alpha = 0,
            Children = new Drawable[]
            {
                carousel = new BeatmapSetCarousel(),
                noResults = new NoResultsPlaceholder(palette)
                {
                    RequestClearSearch = () => filterControl.SetSearch(string.Empty),
                    RequestClearLevelRange = () => filterControl.ClearLevelRange()
                },
                createChevronButton(previous: true, NavigatePrevious, iconColor),
                createChevronButton(previous: false, NavigateNext, iconColor)
            }
        });

        Add(difficultyContainer = new Container()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Position = new Vector2(0, bar_hidden_y),
            Size = new Vector2(1, 150),
            Alpha = 0,
            Children = new Drawable[]
            {
                difficultyCarousel = new BeatmapCarousel(),
                createChevronButton(previous: true, NavigatePrevious, iconColor),
                createChevronButton(previous: false, NavigateNext, iconColor)
            }
        });

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

        filterControl.FilterChanged += _ => applyFilter();

        var sets = filteredBeatmapSets();
        carousel.SetItems(sets);
        updateMatchCount(sets.Count);

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
    /// Reshapes the whole region for a new orientation: forwards it to both carousels, swaps the region
    /// between a bottom bar and a right-hand strip, and re-places the navigation chevrons. Keeps the
    /// region's shown/hidden state so a live switch while on screen stays visible.
    /// </summary>
    private void applyOrientation(CarouselOrientation orientation)
    {
        currentOrientation = orientation;
        carousel.Orientation.Value = orientation;
        difficultyCarousel.Orientation.Value = orientation;

        bool vertical = orientation == CarouselOrientation.Vertical;

        foreach (var region in new Drawable[] { menuBox, carouselContainer, difficultyContainer })
        {
            region.Anchor = region.Origin = vertical ? Anchor.CentreRight : Anchor.BottomCentre;
            region.RelativeSizeAxes = vertical ? Axes.Y : Axes.X;
            region.Size = vertical ? new Vector2(verticalRegionWidth, 1f) : new Vector2(1, 150);
            region.Position = regionShown ? shownPosition : hiddenPosition;
        }

        noResults.SetLayout(orientation);

        // The shade is for the vertical list only; the horizontal strip has its cream bar.
        if (!vertical)
            noResultsShade.FadeOut(200, Easing.OutQuint);
        else if (noResults.IsPresented)
            noResultsShade.FadeIn(400, Easing.OutQuint);

        // The vertical song list starts below the filters rather than running under them. The
        // difficulty list has the column to itself: the filters step aside while picking a difficulty.
        carouselContainer.Padding = new MarginPadding { Top = vertical ? BeatmapFilterControl.VERTICAL_HEIGHT + filter_gap : 0 };

        // The cream backdrop is only the horizontal bottom bar. In vertical the list floats over the game
        // background, so the backdrop is hidden entirely.
        menuBox.Color = cream_color;
        if (regionShown)
            menuBox.Alpha = menuBoxShownAlpha;

        // The filters ride with the region: over the top of the vertical list, or above the strip's right end.
        filterControl.Anchor = filterControl.Origin = vertical ? Anchor.TopRight : Anchor.BottomRight;
        filterControl.SetLayout(orientation, filterWidth);
        filterControl.Position = regionShown ? filterShownPosition : filterHiddenPosition;

        foreach (var (button, icon, previous) in chevrons)
        {
            if (vertical)
            {
                button.Anchor = button.Origin = previous ? Anchor.TopCentre : Anchor.BottomCentre;
                button.RelativeSizeAxes = Axes.X;
                button.Size = new Vector2(1f, 48);
                icon.Icon = previous ? IconUsage.ExpandLess : IconUsage.ExpandMore;
            }
            else
            {
                button.Anchor = button.Origin = previous ? Anchor.CentreLeft : Anchor.CentreRight;
                button.RelativeSizeAxes = Axes.None;
                button.Size = new Vector2(48, 150);
                icon.Icon = previous ? IconUsage.ChevronLeft : IconUsage.ChevronRight;
            }
        }
    }

    /// <summary>The space between the filters and the vertical list below them.</summary>
    private const float filter_gap = 8;

    private float filterWidth => currentOrientation == CarouselOrientation.Vertical
        ? Math.Max(400, verticalRegionWidth)
        : BeatmapFilterControl.HORIZONTAL_WIDTH;

    // Vertical: across the top of the list's column, flush with the screen's right edge.
    // Horizontal: just above the strip's right end (the strip's top sits a strip's height above bar_shown_y).
    private Vector2 filterShownPosition => currentOrientation == CarouselOrientation.Vertical
        ? Vector2.Zero
        : new Vector2(-24, bar_shown_y - 150 - 12);

    private Vector2 filterHiddenPosition => currentOrientation == CarouselOrientation.Vertical
        ? filterShownPosition + new Vector2(verticalRegionWidth, 0)
        : filterShownPosition + new Vector2(0, bar_hidden_y - bar_shown_y);

    private Vector2 shownPosition => currentOrientation == CarouselOrientation.Vertical ? Vector2.Zero : new Vector2(0, bar_shown_y);

    private Vector2 hiddenPosition => currentOrientation == CarouselOrientation.Vertical
        ? new Vector2(verticalRegionWidth, 0)
        : new Vector2(0, bar_hidden_y);

    /// <summary>Vertical region width: half the screen, capped so it never gets absurdly wide.</summary>
    private float verticalRegionWidth => Math.Min(DrawWidth * vertical_region_fraction, vertical_region_max_width);

    // Cream backdrop: shown as the horizontal bottom bar, hidden entirely for the vertical list.
    private float menuBoxShownAlpha => currentOrientation == CarouselOrientation.Vertical ? 0f : 1f;

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

    private List<BeatmapSet> filteredBeatmapSets() => Filter.Apply(getDisplayBeatmapSets(), difficultiesOf, forcedSet);

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

        showSets();
    }

    /// <summary>Lists what passes (plus a forced song), keeping the current song selected if it's listed.</summary>
    private void showSets()
    {
        List<BeatmapSet> sets = filteredBeatmapSets();
        BeatmapSet? current = workingBeatmap?.BeatmapSet ?? carousel.CurrentItem.Value;

        carousel.SetItems(sets);
        updateMatchCount(countMatches(sets));

        int index = current == null ? -1 : sets.FindIndex(s => s.ID == current.ID);
        if (index >= 0)
            carousel.SetIndex(index, animate: false);
    }

    private void updateMatchCount(int count)
    {
        MatchCount = count;
        filterControl.SetMatchCount(count);
        updateNoResults();
        ListChanged?.Invoke();
    }

    /// <summary>
    /// Shows the placeholder when nothing is listed: the music muffles for a moment as it
    /// appears, and in the vertical layout a shade comes up behind it so it reads over the background.
    /// </summary>
    private void updateNoResults()
    {
        if (carousel.Count > 0)
        {
            noResults.Dismiss();
            noResultsShade.FadeOut(400, Easing.OutQuint);
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

        if (currentOrientation == CarouselOrientation.Vertical)
            noResultsShade.FadeIn(1000, Easing.OutQuint);
    }

    private Drawable createChevronButton(bool previous, Action action, Color iconColor)
    {
        IconSprite icon;
        var button = new ClickableContainer()
        {
            Anchor = previous ? Anchor.CentreLeft : Anchor.CentreRight,
            Origin = previous ? Anchor.CentreLeft : Anchor.CentreRight,
            Size = new Vector2(48, 150),
            Action = action,
            Child = icon = new IconSprite()
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = previous ? IconUsage.ChevronLeft : IconUsage.ChevronRight,
                IconSize = 32,
                Color = iconColor.Darken(0.1f)
            }
        };

        chevrons.Add((button, icon, previous));
        return button;
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

        // The shade sits behind the list's column and reaches well past it, following it in and out.
        noResultsShade.X = carouselContainer.X;
        noResultsShade.Width = Math.Min(DrawSize.X, carouselContainer.DrawSize.X * 2);

        // Keep the (capped, screen-relative) region width in sync as the window resizes.
        if (currentOrientation == CarouselOrientation.Vertical)
        {
            float width = verticalRegionWidth;
            if (Math.Abs(carouselContainer.Width - width) > 0.5f)
            {
                menuBox.Width = width;
                carouselContainer.Width = width;
                difficultyContainer.Width = width;
                filterControl.SetLayout(CarouselOrientation.Vertical, filterWidth);
            }
        }
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

            if (regionShown)
                filterControl.FadeIn(duration, Easing.OutQuint);
        }
        else
        {
            carouselContainer.FadeOut(duration, Easing.OutQuint);
            difficultyContainer.FadeIn(duration, Easing.OutQuint);

            // The filters are for finding a song; with one picked they step aside (and let go of the keyboard).
            filterControl.FadeOut(duration, Easing.OutQuint);
            filterControl.ReleaseSearchFocus();
        }
    }

    /// <summary>
    /// Slides the region into view.
    /// </summary>
    public void ShowRegion()
    {
        regionShown = true;

        var target = shownPosition;
        menuBox.MoveTo(target, 500, Easing.OutBack)
            .FadeTo(menuBoxShownAlpha, 500, Easing.OutQuint);
        carouselContainer.MoveTo(target, 500, Easing.OutBack);
        difficultyContainer.MoveTo(target, 500, Easing.OutBack);
        // Its fade follows the state (it stays hidden while picking a difficulty).
        filterControl.MoveTo(filterShownPosition, 500, Easing.OutQuint);

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
        menuBox.MoveTo(target, 500, Easing.OutBack)
            .FadeOut(500, Easing.OutQuint);
        carouselContainer.MoveTo(target, 500, Easing.OutBack)
            .FadeOut(500, Easing.OutQuint);
        difficultyContainer.MoveTo(target, 500, Easing.OutBack)
            .FadeOut(500, Easing.OutQuint);
        filterControl.MoveTo(filterHiddenPosition, 500, Easing.OutQuint).FadeOut(300, Easing.OutQuint);

        // Cascade the cards out to match the entrance.
        carousel.AnimateExit();
        difficultyCarousel.AnimateExit();
    }
}
