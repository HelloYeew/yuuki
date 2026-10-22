// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// Song select's search and filters: what you type as a headline, a level histogram you drag a range
/// across, lane chips and sort words. In the vertical layout it heads the rail, on the rail's own glass;
/// in the horizontal one it's a frosted card in the top-right corner.
/// </summary>
public partial class BeatmapFilterControl : Container
{
    /// <summary>
    /// How long typing has to pause before the search applies.
    /// </summary>
    private const double search_debounce = 150;

    private const float pad_top = 22;
    private const float pad_left = 30;
    private const float pad_right = 28;
    private const float search_height = 62;
    private const float level_gap = 12;
    private const float level_height = 84;
    private const float controls_gap = 10;
    private const float controls_height = 34;

    /// <summary>
    /// The space between the lane chips and the sort words.
    /// </summary>
    private const float controls_spacing = 12;

    /// <summary>
    /// The header's height in either layout: what the vertical list is moved down by.
    /// </summary>
    public const float HEIGHT = pad_top + search_height + level_gap + level_height + controls_gap + controls_height + 4;

    /// <summary>
    /// The card's width in the horizontal layout.
    /// </summary>
    public const float CARD_WIDTH = 640;

    /// <summary>
    /// The header's height on a phone: just the search bar; the rest is in a sheet it opens.
    /// </summary>
    public const float PHONE_BAR_HEIGHT = 84;

    private const float phone_card_width = 600;

    private bool phone;

    /// <summary>
    /// The header's height in the layout it's in now: what the vertical list is moved down by.
    /// </summary>
    public float HeaderHeight => phone ? PHONE_BAR_HEIGHT : HEIGHT;

    /// <summary>
    /// The card's width in the horizontal layout, in the layout it's in now.
    /// </summary>
    public float CardWidth => phone ? phone_card_width : CARD_WIDTH;

    public BeatmapFilter Filter { get; } = new BeatmapFilter();

    /// <summary>
    /// Fired by the random button (see <see cref="ShowRandom"/>).
    /// </summary>
    public Action? RandomRequested { get; set; }

    /// <summary>
    /// Shows a random button in the phone's bar, for touch, where there's no key for one (the footer
    /// offers it on a keyboard).
    /// </summary>
    public void ShowRandom(bool show)
    {
        randomShown = show;
        randomButton.Alpha = phone && show ? 1 : 0;
    }

    /// <summary>The random button, while it shows (tests).</summary>
    public Drawable? RandomButton => randomButton.Alpha > 0 ? randomButton : null;

    /// <summary>
    /// Fired whenever the filter changes (typing settles a moment first).
    /// </summary>
    public event Action<BeatmapFilter>? FilterChanged;

    public bool IsSearchFocused => searchBox.HasFocus;

    /// <summary>
    /// The level histogram's bars, where a press moves the range (diagnostics/tests).
    /// </summary>
    public Drawable LevelBarsArea => levels.Area;

    /// <summary>
    /// The phone's filter button (diagnostics/tests).
    /// </summary>
    public Drawable FilterButton => filterToggle;

    /// <summary>
    /// Whether the phone's filter button shows that a filter or sort is on (diagnostics/tests).
    /// </summary>
    public bool FilterDotShown => filterToggle.DotShown;

    private readonly Container card;
    private readonly Container body;
    private readonly Container searchRow;
    private readonly BarButton filterToggle;
    private readonly BarButton randomButton;
    private bool randomShown;
    private readonly Container sheet;
    private readonly Box sheetFill;
    private readonly FlowContainer sheetFlow;
    private readonly Box rule;
    private readonly Container searchTab;
    private readonly Box searchTabFill;
    private readonly IconSprite searchIcon;
    private readonly HeadlineSearchBox searchBox;
    private readonly FlowContainer countFlow;
    private readonly SpriteText countNumber;
    private readonly SpriteText countRest;
    private readonly LevelBars levels;
    private readonly Container controlsRow;
    private readonly Container lanesGroup;
    private readonly FlowContainer lanesFlow;
    private readonly IconSprite lanesIcon;
    private readonly FlowContainer sortWords;

    private readonly List<LaneChip> laneChips = new List<LaneChip>();

    private Color ink = Color.Black;
    private Color secondary = Color.Gray;
    private Color accent = Color.Gray;
    private Color accentInk = Color.White;
    private Color control = Color.Black;

    private int searchVersion;

    public BeatmapFilterControl()
    {
        Size = new Vector2(CARD_WIDTH, HEIGHT);

        Children = new Drawable[]
        {
            // The horizontal card's own glass; the vertical header sits on the rail's.
            card = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 20,
                BorderThickness = 1,
                BorderColor = Color.White.WithAlpha(0.35f),
                Child = new FrostedBand
                {
                    RelativeSizeAxes = Axes.Both,
                    RimEdge = Edges.None
                }
            },
            body = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = pad_top, Left = pad_left, Right = pad_right },
                Children = new Drawable[]
                {
                    searchRow = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = search_height,
                        Children = new Drawable[]
                        {
                            searchTab = new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(54, 42),
                                Children = new Drawable[]
                                {
                                    new Container
                                    {
                                        // Leaning about its middle; about a corner, the blade slid out from under the icon.
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        RelativeSizeAxes = Axes.Both,
                                        Masking = true,
                                        CornerRadius = 9,
                                        Shear = UserInterfaceConstant.Shear,
                                        Child = searchTabFill = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both
                                        }
                                    },
                                    searchIcon = new IconSprite
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Icon = IconUsage.Search,
                                        IconSize = 22
                                    }
                                }
                            },
                            searchBox = new HeadlineSearchBox
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                X = 54 + 16,
                                Height = 46,
                                PlaceholderText = "search.",
                                ReleaseFocusOnCommit = true
                            },
                            countFlow = new FlowContainer
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                AutoSizeAxes = Axes.Both,
                                Direction = FlowDirection.Horizontal,
                                Children = new Drawable[]
                                {
                                    countNumber = new SpriteText
                                    {
                                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.ExtraBold)
                                    },
                                    countRest = new SpriteText
                                    {
                                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 18, FontWeight.Bold)
                                    }
                                }
                            },
                            rule = new Box
                            {
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                RelativeSizeAxes = Axes.X,
                                Height = 1.5f
                            },
                            // Only on a phone, where the filters live in a sheet it opens.
                            filterToggle = new BarButton(IconUsage.Tune, () => SetSheetOpen(!SheetOpen))
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Alpha = 0
                            },
                            // Only by touch, where there's no key for a random song (see ShowRandom).
                            randomButton = new BarButton(IconUsage.Shuffle, () => RandomRequested?.Invoke())
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Alpha = 0
                            }
                        }
                    },
                    levels = new LevelBars
                    {
                        RelativeSizeAxes = Axes.X,
                        Y = search_height + level_gap,
                        Height = level_height,
                        RangeChanged = onRangeChanged
                    },
                    controlsRow = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Y = search_height + level_gap + level_height + controls_gap,
                        Height = controls_height,
                        Children = new Drawable[]
                        {
                            lanesGroup = new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                AutoSizeAxes = Axes.Both,
                                Masking = true,
                                CornerRadius = 9,
                                BorderThickness = 1.5f,
                                Alpha = 0,
                                Child = lanesFlow = new FlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FlowDirection.Horizontal,
                                    Spacing = new Vector2(2, 0),
                                    Padding = new MarginPadding { Left = 8, Right = 3, Top = 2, Bottom = 2 },
                                    Child = lanesIcon = new IconSprite
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = IconUsage.ViewColumn,
                                        IconSize = 17,
                                        Margin = new MarginPadding { Right = 4 }
                                    }
                                }
                            },
                            sortWords = new FlowContainer
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                AutoSizeAxes = Axes.Both,
                                Direction = FlowDirection.Horizontal,
                                Children = Enum.GetValues<BeatmapSort>().Select(sort => new SortWord(sort, () => setSort(sort))).ToArray()
                            }
                        }
                    }
                }
            },
            // The phone's filter sheet: the level bars, lane chips and sort words, opened from the bar. It
            // hangs below the bar, over the list.
            sheet = new Container
            {
                X = sheet_inset,
                Y = PHONE_BAR_HEIGHT,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 18,
                Alpha = 0,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Color = Color.Black.WithAlpha(0.3f),
                    Radius = 18,
                    Offset = new Vector2(0, 10)
                },
                Children = new Drawable[]
                {
                    sheetFill = new Box
                    {
                        RelativeSizeAxes = Axes.Both
                    },
                    sheetFlow = new FlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 12),
                        Padding = new MarginPadding { Top = 18, Bottom = 20, Left = 24, Right = 24 }
                    }
                }
            }
        };

        searchBox.Text.BindValueChanged(e =>
        {
            Filter.Search = e.NewValue;
            int version = ++searchVersion;
            Scheduler.AddDelayed(() =>
            {
                if (version == searchVersion)
                    FilterChanged?.Invoke(Filter);
            }, search_debounce);
        });

        SetCounts(0, 0);
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        var palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        ink = palette.Text;
        secondary = SetlistColors.Secondary(ink);
        accent = palette.Accent;
        accentInk = accent.IsLight() ? SetlistColors.TierInk(accent) : palette.TextLight;
        control = palette.Control;

        searchTabFill.Color = accent;
        searchIcon.Color = accentInk;
        filterToggle.SetColors(accent, accentInk, control, palette.Surface);
        randomButton.SetColors(accent, accentInk, control, palette.Surface);
        sheetFill.Color = palette.Surface.Lighten(0.2f).WithAlpha(0.97f);
        searchBox.SetColors(ink, control);
        countNumber.Color = ink;
        countRest.Color = secondary;
        rule.Color = ink.WithAlpha(0.14f);
        lanesGroup.BorderColor = ink.WithAlpha(0.2f);
        lanesIcon.Color = secondary;
        levels.SetColors(ink, secondary, control);

        updateControls();
    }

    public override void Update()
    {
        base.Update();

        // Right to left: the filter button, the random button, the count.
        float countWidth = countFlow.DrawWidth;
        float toggleWidth = phone ? filterToggle.DrawWidth + 14 : 0;
        float randomWidth = phone && randomShown ? randomButton.DrawWidth + 14 : 0;
        randomButton.X = -toggleWidth;
        searchBox.Width = Math.Max(0, searchBox.Parent!.DrawWidth - searchBox.X - countWidth - toggleWidth - randomWidth - 16);
        countFlow.X = -toggleWidth - randomWidth;

        // On a phone the sheet spans the bar, and its lane chips share the sheet's width.
        if (phone)
        {
            sheet.Width = Math.Max(0, DrawWidth - 2 * sheet_inset);
            layOutPhoneChips();
        }

        // Lanes and sort share a row on the desktop; on a phone the words have the sheet's row to
        // themselves. Either way they shrink to fit rather than run off.
        float lanesWidth = !phone && lanesGroup.Alpha > 0 ? lanesGroup.DrawWidth + controls_spacing : 0;
        float room = (phone ? sheetFlow.DrawWidth - sheetFlow.Padding.Left - sheetFlow.Padding.Right : controlsRow.DrawWidth) - lanesWidth;
        float fit = sortWords.DrawWidth > 0 && room > 0 ? Math.Clamp(room / sortWords.DrawWidth, 0.7f, 1) : 1;

        if (Math.Abs(sortWords.Scale.X - fit) > 0.001f)
            sortWords.Scale = new Vector2(fit);
    }

    /// <summary>
    /// The phone sheet's distance from the bar's sides.
    /// </summary>
    private const float sheet_inset = 12;

    /// <summary>
    /// Whether the phone's filter sheet is open.
    /// </summary>
    public bool SheetOpen { get; private set; }

    /// <summary>
    /// The open sheet hangs below the bar. Input only reaches children through a parent the pointer is
    /// over, so the header claims the sheet's area while it's open, or its controls couldn't be tapped.
    /// </summary>
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
        base.ReceivePositionalInputAt(screenSpacePos) || (SheetOpen && sheet.ReceivePositionalInputAt(screenSpacePos));

    /// <summary>
    /// Opens or closes the phone's filter sheet. On the desktop there's none.
    /// </summary>
    public void SetSheetOpen(bool open)
    {
        open &= phone;

        if (SheetOpen == open)
            return;

        SheetOpen = open;

        if (!IsLoaded)
        {
            sheet.Alpha = open ? 1 : 0;
            return;
        }

        sheet.ClearTransforms();
        sheet.FadeTo(open ? 1 : 0, open ? 200 : 150, Easing.OutQuint);
        sheet.MoveToY(open ? PHONE_BAR_HEIGHT : PHONE_BAR_HEIGHT - 10, 200, Easing.OutQuint);
    }

    /// <summary>
    /// Switches between the full header and the phone's bar. On a phone the header is only the search, a
    /// count and a filter button; the level bars, lane chips and sort words move into the sheet that
    /// button opens, at sizes for a thumb.
    /// </summary>
    public void SetPhone(bool value)
    {
        if (phone == value)
            return;

        phone = value;
        SetSheetOpen(false);

        body.Padding = phone
            ? new MarginPadding { Top = 14, Left = 24, Right = 22 }
            : new MarginPadding { Top = pad_top, Left = pad_left, Right = pad_right };
        searchRow.Height = phone ? 56 : search_height;
        rule.Alpha = phone ? 0 : 1;
        filterToggle.Alpha = phone ? 1 : 0;
        randomButton.Alpha = phone && randomShown ? 1 : 0;
        searchBox.SetFontSize(phone ? 32 : 36);
        countNumber.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, phone ? 19 : 18, FontWeight.ExtraBold);
        countRest.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, phone ? 19 : 18, FontWeight.Bold);

        // The filters move house: into the sheet on a phone, back into the header on the desktop.
        foreach (var drawable in new Drawable[] { levels, lanesGroup, sortWords })
            drawable.Parent?.Remove(drawable, false);

        if (phone)
        {
            levels.Y = 0;
            levels.Height = LevelBars.PHONE_HEIGHT;
            sortWords.Anchor = sortWords.Origin = Anchor.TopLeft;
            lanesGroup.Anchor = lanesGroup.Origin = Anchor.TopLeft;
            sheetFlow.AddRange(new Drawable[] { levels, lanesGroup, sortWords });
        }
        else
        {
            // Placed afresh: the sheet's flow moved each of them down its column.
            levels.Position = new Vector2(0, search_height + level_gap);
            levels.Height = level_height;
            body.Add(levels);
            lanesGroup.Position = sortWords.Position = Vector2.Zero;
            lanesGroup.Anchor = lanesGroup.Origin = Anchor.CentreLeft;
            sortWords.Anchor = sortWords.Origin = Anchor.CentreRight;
            controlsRow.AddRange(new Drawable[] { lanesGroup, sortWords });
        }

        levels.SetPhone(phone);

        // The lane chips fill the sheet's width on a phone, so the group stops sizing to them.
        lanesGroup.AutoSizeAxes = phone ? Axes.None : Axes.Both;
        lanesGroup.RelativeSizeAxes = phone ? Axes.X : Axes.None;

        // Relative now, so the whole of the sheet's width (its old width, in pixels, would be read as a
        // multiple of it).
        if (phone)
        {
            lanesGroup.Width = 1;
            lanesGroup.Height = 68;
        }
        lanesFlow.Padding = phone
            ? new MarginPadding { Left = 14, Right = 6, Top = 4, Bottom = 4 }
            : new MarginPadding { Left = 8, Right = 3, Top = 2, Bottom = 2 };
        lanesFlow.Spacing = new Vector2(phone ? 6 : 2, 0);
        lanesIcon.IconSize = phone ? 24 : 17;
        lanesIcon.Margin = new MarginPadding { Right = phone ? 12 : 4 };

        foreach (var chip in laneChips)
            chip.SetPhone(phone);

        foreach (var word in sortWords.Children.OfType<SortWord>())
            word.SetPhone(phone);

        updateCardSize();
    }

    /// <summary>
    /// On a phone the lane chips share the sheet's row equally, each a thumb's width or more.
    /// </summary>
    private void layOutPhoneChips()
    {
        if (laneChips.Count == 0)
            return;

        float room = lanesGroup.DrawWidth - lanesFlow.Padding.Left - lanesFlow.Padding.Right - lanesIcon.DrawWidth - lanesIcon.Margin.Right
                     - lanesFlow.Spacing.X * laneChips.Count;
        float width = Math.Max(48, room / laneChips.Count);

        foreach (var chip in laneChips)
            chip.Width = width;
    }

    /// <summary>
    /// Lays the header out for the carousel's orientation: <paramref name="width"/> wide across the top of
    /// the vertical rail, on its glass, or the frosted card in the horizontal layout.
    /// </summary>
    public void SetLayout(CarouselOrientation orientation, float width = CARD_WIDTH)
    {
        vertical = orientation == CarouselOrientation.Vertical;
        layoutWidth = width;
        updateCardSize();
    }

    private bool vertical;
    private float layoutWidth = CARD_WIDTH;

    private void updateCardSize()
    {
        Size = new Vector2(vertical ? layoutWidth : CardWidth, HeaderHeight);
        card.Alpha = vertical ? 0 : 1;
    }

    /// <summary>
    /// Shows how many songs the filter lets through out of how many there are: "360 songs", or "37 of 360"
    /// while anything is filtered.
    /// </summary>
    public void SetCounts(int matched, int total)
    {
        if (Filter.IsFiltering)
        {
            countNumber.Text = $"{matched:N0}";
            countRest.Text = $" of {total:N0}";
        }
        else
        {
            countNumber.Text = $"{total:N0}";
            countRest.Text = total == 1 ? " song" : " songs";
        }
    }

    /// <summary>
    /// How many difficulties each level has for the search and lanes (see <see cref="BeatmapFilter.LevelCounts"/>).
    /// </summary>
    public void SetLevelCounts(int[] counts) => levels.SetCounts(counts);

    /// <summary>
    /// The lane counts the library has, one chip each. With fewer than two there's nothing to pick between,
    /// so there are no chips.
    /// </summary>
    public void SetLanes(IEnumerable<int> laneCounts)
    {
        int[] available = laneCounts.Distinct().Order().ToArray();

        if (available.SequenceEqual(laneChips.Select(c => c.Lanes)))
            return;

        foreach (var chip in laneChips)
            lanesFlow.Remove(chip);

        laneChips.Clear();

        foreach (int lanes in available)
        {
            var chip = new LaneChip(lanes, () => toggleLanes(lanes));
            chip.SetPhone(phone);
            laneChips.Add(chip);
            lanesFlow.Add(chip);
        }

        lanesGroup.Alpha = available.Length >= 2 ? 1 : 0;

        // A lane count the library no longer has can't be picked off again, so it stops filtering.
        if (Filter.Lanes.RemoveWhere(l => !available.Contains(l)) > 0)
            FilterChanged?.Invoke(Filter);

        updateControls();
    }

    /// <summary>
    /// Types <paramref name="text"/> into the search, as if typed (it applies once typing would have settled).
    /// </summary>
    public void SetSearch(string text) => searchBox.Text.Value = text;

    /// <summary>
    /// What's typed in the search.
    /// </summary>
    public string SearchText => searchBox.Text.Value;

    /// <summary>
    /// Opens the level range back up to every level.
    /// </summary>
    public void ClearLevelRange() => onRangeChanged(0, BeatmapFilter.MAX_LEVEL);

    /// <summary>
    /// Lets every lane count through again.
    /// </summary>
    public void ClearLanes()
    {
        if (Filter.Lanes.Count == 0)
            return;

        Filter.Lanes.Clear();
        updateControls();
        FilterChanged?.Invoke(Filter);
    }

    /// <summary>
    /// Puts the cursor in the search (typing on song select starts a search).
    /// </summary>
    public void FocusSearch() => GetContainingFocusManager()?.ChangeFocus(searchBox);

    /// <summary>
    /// Takes the cursor out of the search, if it's there.
    /// </summary>
    public void ReleaseSearchFocus()
    {
        if (IsSearchFocused)
            GetContainingFocusManager()?.ChangeFocus(null);
    }

    /// <summary>
    /// Sets the sort without a word being clicked (restoring the saved one).
    /// </summary>
    public void SetSort(BeatmapSort sort)
    {
        Filter.Sort = sort;
        updateControls();
    }

    private void setSort(BeatmapSort sort)
    {
        if (Filter.Sort == sort)
            return;

        SetSort(sort);
        FilterChanged?.Invoke(Filter);
    }

    private void toggleLanes(int lanes)
    {
        if (!Filter.Lanes.Remove(lanes))
            Filter.Lanes.Add(lanes);

        updateControls();
        FilterChanged?.Invoke(Filter);
    }

    private void onRangeChanged(int min, int max)
    {
        if (Filter.MinLevel == min && Filter.MaxLevel == max)
            return;

        Filter.MinLevel = min;
        Filter.MaxLevel = max;
        levels.SetRange(min, max);
        FilterChanged?.Invoke(Filter);
    }

    private void updateControls()
    {
        // On a phone the filters are out of sight in the sheet, so the button says when any are on.
        filterToggle.ShowDot(Filter.LevelRangeSet || Filter.Lanes.Count > 0 || Filter.Sort != BeatmapSort.Added);

        foreach (var chip in laneChips)
            chip.SetState(Filter.Lanes.Contains(chip.Lanes), accent, accentInk, ink);

        foreach (var word in sortWords.Children.OfType<SortWord>())
            word.SetState(word.Sort == Filter.Sort, ink, secondary, control);

        levels.SetRange(Filter.MinLevel, Filter.MaxLevel);
    }

    /// <summary>
    /// The search: the game's text box with no box, typed as large as a title.
    /// </summary>
    private partial class HeadlineSearchBox : YuukiTextBox
    {
        public HeadlineSearchBox()
        {
            SetFontSize(36);

            SpriteText.Margin = new MarginPadding();
            PlaceholderSprite.Margin = new MarginPadding();
            ImeText.Margin = new MarginPadding();
        }

        public void SetFontSize(int size)
        {
            var font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, size, FontWeight.Bold);

            SpriteText.Font = font;
            PlaceholderSprite.Font = font;
            ImeText.Font = font;
        }

        public void SetColors(Color ink, Color caret)
        {
            SpriteText.Color = ink;
            PlaceholderSprite.Color = ink;
            Caret.Color = caret;
            SelectionBox.Color = ink.WithAlpha(0.2f);
        }

        protected override Drawable? CreateBackground() => null;

        /// <summary>
        /// Escape with something typed clears it as it leaves the box, so the whole list is back at
        /// once (as in osu!); empty, it just leaves.
        /// </summary>
        public override bool OnKeyDown(KeyEvent e)
        {
            if (e.Key == Key.Escape && HasFocus && !IsComposing && Text.Value.Length > 0)
                Text.Value = string.Empty;

            return base.OnKeyDown(e);
        }
    }

    /// <summary>
    /// One bar per level, colored by tier and as tall as the level's share of what the search and lanes
    /// let through. Press or drag on it to move the nearer end of the range; the tier names under it take
    /// a whole tier at a click.
    /// </summary>
    private partial class LevelBars : Container
    {
        private const int level_count = BeatmapFilter.MAX_LEVEL + 1;
        private const int levels_per_tier = 4;
        private const float gap = 4;
        private const float bars_top = 4;
        /// <summary>
        /// The bars' height on a phone, tier names included: taller bars, a bigger target.
        /// </summary>
        public const float PHONE_HEIGHT = 116;

        // On a phone the bars, the tier names and the handles are all bigger (see SetPhone).
        private float barsHeight = 48;
        private float tiersTop = 60;
        private float tiersHeight = 22;
        private const float off_alpha = 0.22f;
        private float handleWidth = 6;
        private const float handle_lean = 0.2f;

        /// <summary>
        /// The range picked, low then high.
        /// </summary>
        public Action<int, int>? RangeChanged { get; init; }

        [Resolved(canBeNull: true)]
        private UISoundPlayer? uiSounds { get; set; }

        private readonly BarArea area;

        public Drawable Area => area;
        private readonly Container[] bars = new Container[level_count];
        private readonly float[] shown = new float[level_count];
        private readonly float[] target = new float[level_count];
        private readonly Container minHandle;
        private readonly Container maxHandle;
        private readonly Box minHandleFill;
        private readonly Box maxHandleFill;
        private readonly TierTab[] tiers = new TierTab[level_count / levels_per_tier];
        private readonly Container hoverColumn;
        private readonly Box hoverColumnFill;

        private int min;
        private int max = BeatmapFilter.MAX_LEVEL;

        /// <summary>
        /// The level under the pointer, or -1 when it isn't over the bars.
        /// </summary>
        private int hovered = -1;

        public LevelBars()
        {
            Add(area = new BarArea(this)
            {
                RelativeSizeAxes = Axes.X,
                Height = tiersTop - 2
            });

            // The column under the pointer, shaded behind its bar: the bars take a press.
            area.Add(hoverColumn = new Container
            {
                Origin = Anchor.BottomLeft,
                Y = bars_top + barsHeight,
                Height = barsHeight,
                Masking = true,
                CornerRadius = 4,
                Alpha = 0,
                Child = hoverColumnFill = new Box
                {
                    RelativeSizeAxes = Axes.Both
                }
            });

            for (int level = 0; level < level_count; level++)
            {
                shown[level] = target[level] = 0.04f;

                area.Add(bars[level] = new Container
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.BottomLeft,
                    Y = bars_top + barsHeight,
                    Masking = true,
                    CornerRadius = 3,
                    BorderThickness = 1,
                    BorderColor = Color.Black.WithAlpha(0.1f),
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = DifficultyColors.ForLevel(level)
                    }
                });
            }

            area.Add(minHandle = handle(out minHandleFill));
            area.Add(maxHandle = handle(out maxHandleFill));

            for (int i = 0; i < tiers.Length; i++)
            {
                int low = i * levels_per_tier, high = low + levels_per_tier - 1;
                var tier = BeatmapUtility.CalculateDifficultyLevel(low);
                Add(tiers[i] = new TierTab(tier.ToString().ToUpperInvariant(), DifficultyColors.ForTier(tier), () => takeTier(low, high))
                {
                    Y = tiersTop,
                    Height = tiersHeight
                });
            }
        }

        /// <summary>
        /// An end of the range: a grip a little taller than the bars, leaning with the blade about its middle
        /// so it stands in its gap. Leaning about its top, it tipped out past the bars like a stray stroke.
        /// </summary>
        private Container handle(out Box fill) => new Container
        {
            Origin = Anchor.Centre,
            Y = bars_top + barsHeight / 2,
            Size = new Vector2(handleWidth, barsHeight + 10),
            Masking = true,
            CornerRadius = handleWidth / 2,
            Shear = new Vector2(-handle_lean, 0),
            Child = fill = new Box
            {
                RelativeSizeAxes = Axes.Both
            }
        };

        /// <summary>
        /// Sizes the bars, handles and tier names for a phone (taller, and bigger to tap) or the desktop.
        /// </summary>
        public void SetPhone(bool phone)
        {
            barsHeight = phone ? 66 : 48;
            tiersTop = phone ? 82 : 60;
            tiersHeight = phone ? 32 : 22;
            handleWidth = phone ? 8 : 6;

            area.Height = tiersTop - 2;

            foreach (var bar in bars)
                bar.Y = bars_top + barsHeight;

            hoverColumn.Y = bars_top + barsHeight;
            hoverColumn.Height = barsHeight;

            foreach (var handle in new[] { minHandle, maxHandle })
            {
                handle.Y = bars_top + barsHeight / 2;
                handle.Size = new Vector2(handleWidth, barsHeight + 10);
                handle.CornerRadius = handleWidth / 2;
            }

            foreach (var tier in tiers)
            {
                tier.Y = tiersTop;
                tier.Height = tiersHeight;
                tier.SetFontSize(phone ? 15 : 12);
            }
        }

        public void SetColors(Color ink, Color secondary, Color handleColor)
        {
            minHandleFill.Color = maxHandleFill.Color = handleColor;
            hoverColumnFill.Color = ink.WithAlpha(0.1f);

            foreach (var tier in tiers)
                tier.SetColors(ink, secondary);

            updateRangeDisplay();
        }

        public void SetCounts(int[] counts)
        {
            int most = Math.Max(1, counts.Length == 0 ? 0 : counts.Max());

            for (int level = 0; level < level_count; level++)
            {
                int count = level < counts.Length ? counts[level] : 0;
                target[level] = count > 0 ? Math.Max(0.06f, (float)count / most) : 0.04f;
            }
        }

        public void SetRange(int newMin, int newMax)
        {
            if (min == newMin && max == newMax)
                return;

            min = newMin;
            max = newMax;
            updateRangeDisplay();
        }

        private void updateRangeDisplay()
        {
            for (int level = 0; level < level_count; level++)
            {
                float alpha = level < min || level > max ? off_alpha : 1;

                // Set outright until loaded: a fade needs the clock, which comes with loading.
                if (IsLoaded)
                    bars[level].FadeTo(alpha, 200, Easing.OutQuint);
                else
                    bars[level].Alpha = alpha;
            }

            for (int i = 0; i < tiers.Length; i++)
                tiers[i].Active = min == i * levels_per_tier && max == (i + 1) * levels_per_tier - 1;
        }

        public override void Update()
        {
            base.Update();

            float width = area.DrawWidth;
            float barWidth = Math.Max(1, (width - gap * (level_count - 1)) / level_count);
            float column = barWidth + gap;

            // Grown toward their counts rather than jumping: the bars settle as you type.
            float follow = 1 - MathF.Exp(-(float)Math.Max(0, Clock.ElapsedFrameTime) / 70f);

            for (int level = 0; level < level_count; level++)
            {
                float height = shown[level] + (target[level] - shown[level]) * follow;
                shown[level] = Math.Abs(target[level] - height) < 0.001f ? target[level] : height;

                var size = new Vector2(barWidth, shown[level] * barsHeight);
                var bar = bars[level];

                if (bar.Size != size)
                    bar.Size = size;

                if (Math.Abs(bar.X - level * column) > 0.01f)
                    bar.X = level * column;
            }

            // The handles stand in the gaps at the range's ends, their lean kept inside the bars. Not before
            // there's a width to stand in.
            float inset = handleWidth / 2 + handle_lean * minHandle.Height / 2;
            float edge = Math.Max(inset, width - inset);
            minHandle.X = Math.Clamp(min * column - gap / 2, inset, edge);
            maxHandle.X = Math.Clamp((max + 1) * column - gap / 2, inset, edge);

            if (hovered >= 0)
            {
                hoverColumn.X = hovered * column - gap / 2;
                hoverColumn.Width = column;
            }

            for (int i = 0; i < tiers.Length; i++)
            {
                tiers[i].X = i * levels_per_tier * column;
                tiers[i].Width = levels_per_tier * column - gap;
            }
        }

        private void takeTier(int low, int high)
        {
            // The tier again opens the range back up.
            if (min == low && max == high)
                RangeChanged?.Invoke(0, BeatmapFilter.MAX_LEVEL);
            else
                RangeChanged?.Invoke(low, high);
        }

        private void setHovered(int level)
        {
            if (hovered == level)
                return;

            hovered = level;
            hoverColumn.FadeTo(level >= 0 ? 1 : 0, 100, Easing.OutQuint);
        }

        private int levelAt(Vector2 screenPosition)
        {
            float width = area.DrawWidth;
            if (width <= 0)
                return 0;

            float x = area.ToLocalSpace(screenPosition).X / width;
            return Math.Clamp((int)(x * level_count), 0, BeatmapFilter.MAX_LEVEL);
        }

        /// <summary>
        /// Where a press lands on the bars: it takes the nearer end of the range (on a tie, the side it's
        /// on) and moves it, and dragging carries on moving it.
        /// </summary>
        private partial class BarArea : Container
        {
            private readonly LevelBars owner;
            private bool? movingMin;

            public BarArea(LevelBars owner)
            {
                this.owner = owner;
            }

            // A left press is the drag's start (Drawable.OnMouseDown calls this), so a plain click moves
            // the range too.
            public override bool OnDragStart(MouseButtonEvent e)
            {
                int level = owner.levelAt(e.ScreenSpaceMousePosition);
                int toMin = Math.Abs(level - owner.min);
                int toMax = Math.Abs(level - owner.max);

                movingMin = toMin < toMax || (toMin == toMax && level <= owner.min);
                moveTo(level);
                return true;
            }

            public override bool OnDrag(MouseEvent e)
            {
                if (movingMin == null)
                    return false;

                moveTo(owner.levelAt(e.ScreenSpaceMousePosition));
                return true;
            }

            public override bool OnDragEnd(MouseButtonEvent e)
            {
                movingMin = null;
                return true;
            }

            public override bool OnHover(MouseEvent e)
            {
                owner.setHovered(owner.levelAt(e.ScreenSpaceMousePosition));
                return true;
            }

            public override bool OnMouseMove(MouseEvent e)
            {
                bool handled = base.OnMouseMove(e);

                if (IsHovered || movingMin != null)
                    owner.setHovered(owner.levelAt(e.ScreenSpaceMousePosition));

                return handled;
            }

            public override bool OnHoverLost(MouseEvent e)
            {
                owner.setHovered(-1);
                return base.OnHoverLost(e);
            }

            private void moveTo(int level)
            {
                int newMin = owner.min, newMax = owner.max;

                if (movingMin == true)
                    newMin = Math.Min(level, owner.max);
                else
                    newMax = Math.Max(level, owner.min);

                if (newMin == owner.min && newMax == owner.max)
                    return;

                // A notch per level, higher up the range, pitched down at the ends (as the slider was).
                float at = (float)(movingMin == true ? newMin : newMax) / BeatmapFilter.MAX_LEVEL;
                owner.uiSounds?.Play(UISound.SliderTick, at <= 0 || at >= 1 ? 0.7 : 1 + at * 0.2);

                owner.RangeChanged?.Invoke(newMin, newMax);
            }
        }
    }

    /// <summary>
    /// The phone's filter button: the accent blade the search tab is, with a dot while any filter or a sort
    /// other than "added" is on, so a filtered list never looks complete by accident.
    /// </summary>
    /// <summary>
    /// A button in the phone's bar: a sheared accent pill with an icon, and a dot it can show (the filter
    /// button's "a filter is on").
    /// </summary>
    private partial class BarButton : ClickableContainer
    {
        private readonly Box fill;
        private readonly IconSprite icon;
        private readonly Container dot;
        private readonly Box dotFill;

        public BarButton(IconUsage iconUsage, Action action)
        {
            Action = action;
            Size = new Vector2(60, 48);

            Children = new Drawable[]
            {
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 10,
                    Shear = UserInterfaceConstant.Shear,
                    Child = fill = new Box
                    {
                        RelativeSizeAxes = Axes.Both
                    }
                },
                icon = new IconSprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = iconUsage,
                    IconSize = 26
                },
                dot = new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    Position = new Vector2(2, 4),
                    Size = new Vector2(14),
                    Masking = true,
                    CornerRadius = 7,
                    BorderThickness = 3,
                    Alpha = 0,
                    Child = dotFill = new Box
                    {
                        RelativeSizeAxes = Axes.Both
                    }
                },
                new HoverClickSounds()
            };
        }

        public void SetColors(Color accent, Color accentInk, Color control, Color ring)
        {
            fill.Color = accent;
            icon.Color = accentInk;
            dotFill.Color = control;
            dot.BorderColor = ring;
        }

        public bool DotShown { get; private set; }

        public void ShowDot(bool show)
        {
            DotShown = show;

            if (IsLoaded)
                dot.FadeTo(show ? 1 : 0, 150, Easing.OutQuint);
            else
                dot.Alpha = show ? 1 : 0;
        }
    }

    /// <summary>
    /// A tier's name under its four bars, with a line in its color over it. It fills with the tier's color
    /// under the pointer and while its levels are the range, so it reads as a button and not a caption.
    /// </summary>
    private partial class TierTab : ClickableContainer
    {
        private readonly Box background;
        private readonly SpriteText label;
        private Color ink = Color.Black;
        private Color secondary = Color.Gray;
        private bool active;

        public TierTab(string name, Color color, Action action)
        {
            Action = action;
            Masking = true;
            CornerRadius = 4;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = color,
                    Alpha = 0
                },
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 3,
                    Color = color
                },
                label = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Y = 1.5f,
                    Text = name,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 12, FontWeight.ExtraBold)
                },
                new HoverClickSounds()
            };
        }

        public bool Active
        {
            set
            {
                active = value;
                updateState();
            }
        }

        public void SetColors(Color newInk, Color newSecondary)
        {
            ink = newInk;
            secondary = newSecondary;
            updateState();
        }

        public void SetFontSize(float size) => label.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)size, FontWeight.ExtraBold);

        private bool hovered;

        public override bool OnHover(MouseEvent e)
        {
            hovered = true;
            updateState();
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hovered = false;
            updateState();
            return base.OnHoverLost(e);
        }

        private void updateState()
        {
            float fill = active ? 0.35f : hovered ? 0.18f : 0;
            var text = active || hovered ? ink : secondary;

            // Set outright until loaded: a fade needs the clock, which comes with loading.
            if (IsLoaded)
            {
                background.FadeTo(fill, 150, Easing.OutQuint);
                label.FadeToColor(text, 150, Easing.OutQuint);
            }
            else
            {
                background.Alpha = fill;
                label.Color = text;
            }
        }
    }

    /// <summary>
    /// A lane count to filter by: a faint pill at rest, darker under the pointer, filled in the accent
    /// while it's picked.
    /// </summary>
    private partial class LaneChip : ClickableContainer
    {
        public readonly int Lanes;

        private readonly Box rest;
        private readonly Box fill;
        private readonly SpriteText label;

        private bool on;
        private Color accentInk = Color.White;
        private Color ink = Color.Black;

        public LaneChip(int lanes, Action action)
        {
            Lanes = lanes;
            Action = action;

            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            AutoSizeAxes = Axes.X;
            Height = 26;
            Masking = true;
            CornerRadius = 6;

            Children = new Drawable[]
            {
                rest = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0
                },
                fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Alpha = 0
                },
                label = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Margin = new MarginPadding { Left = 8, Right = 8 },
                    Text = BeatmapUtility.FormatLanes(lanes),
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.ExtraBold)
                },
                new HoverClickSounds()
            };
        }

        /// <summary>
        /// On a phone a chip is a thumb's target, as wide as the sheet gives it (set by the header); on the
        /// desktop it fits its label.
        /// </summary>
        public void SetPhone(bool phone)
        {
            AutoSizeAxes = phone ? Axes.None : Axes.X;
            Height = phone ? 58 : 26;
            CornerRadius = phone ? 9 : 6;
            label.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, phone ? 20 : 14, FontWeight.ExtraBold);
            label.Anchor = label.Origin = phone ? Anchor.Centre : Anchor.CentreLeft;
        }

        public void SetState(bool picked, Color accent, Color newAccentInk, Color newInk)
        {
            on = picked;
            accentInk = newAccentInk;
            ink = newInk;
            rest.Color = ink;
            fill.Color = accent;
            updateState();
        }

        private bool hovered;

        public override bool OnHover(MouseEvent e)
        {
            hovered = true;
            updateState();
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hovered = false;
            updateState();
            return base.OnHoverLost(e);
        }

        private void updateState()
        {
            float restAlpha = hovered ? 0.16f : 0.07f;
            var text = on ? accentInk : ink;

            // Set outright until loaded: a fade needs the clock, which comes with loading.
            if (IsLoaded)
            {
                rest.FadeTo(restAlpha, 120, Easing.OutQuint);
                fill.FadeTo(on ? 1 : 0, 150, Easing.OutQuint);
                label.FadeToColor(text, 150, Easing.OutQuint);
            }
            else
            {
                rest.Alpha = restAlpha;
                fill.Alpha = on ? 1 : 0;
                label.Color = text;
            }
        }
    }

    /// <summary>
    /// A sort order as a word: the one in use is in full ink, with a blade under it, and the others darken
    /// under the pointer.
    /// </summary>
    private partial class SortWord : ClickableContainer
    {
        public readonly BeatmapSort Sort;

        private readonly SpriteText label;
        private readonly Container underline;
        private readonly Box underlineFill;

        private bool on;
        private Color ink = Color.Black;
        private Color secondary = Color.Gray;

        public SortWord(BeatmapSort sort, Action action)
        {
            Sort = sort;
            Action = action;

            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            AutoSizeAxes = Axes.Both;

            Children = new Drawable[]
            {
                label = new SpriteText
                {
                    Margin = new MarginPadding { Left = 7, Right = 7, Top = 5, Bottom = 5 },
                    Text = sort.ToString().ToLowerInvariant(),
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold)
                },
                underline = new Container
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.X,
                    Height = 4,
                    Padding = new MarginPadding { Left = 7, Right = 7 },
                    Alpha = 0,
                    Child = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 2,
                        Shear = UserInterfaceConstant.Shear,
                        Child = underlineFill = new Box
                        {
                            RelativeSizeAxes = Axes.Both
                        }
                    }
                },
                new HoverClickSounds()
            };
        }

        public void SetPhone(bool phone)
        {
            label.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, phone ? 20 : 15, FontWeight.Bold);
            label.Margin = phone
                ? new MarginPadding { Left = 12, Right = 12, Top = 16, Bottom = 16 }
                : new MarginPadding { Left = 7, Right = 7, Top = 5, Bottom = 5 };
            underline.Padding = new MarginPadding { Left = phone ? 12 : 7, Right = phone ? 12 : 7 };
            underline.Y = phone ? -6 : 0;
        }

        public void SetState(bool inUse, Color newInk, Color newSecondary, Color control)
        {
            on = inUse;
            ink = newInk;
            secondary = newSecondary;
            underlineFill.Color = control;
            updateState();
        }

        private bool hovered;

        public override bool OnHover(MouseEvent e)
        {
            hovered = true;
            updateState();
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hovered = false;
            updateState();
            return base.OnHoverLost(e);
        }

        private void updateState()
        {
            var text = on || hovered ? ink : secondary;

            // Set outright until loaded: a fade needs the clock, which comes with loading.
            if (IsLoaded)
            {
                underline.FadeTo(on ? 1 : 0, 150, Easing.OutQuint);
                label.FadeToColor(text, 150, Easing.OutQuint);
            }
            else
            {
                underline.Alpha = on ? 1 : 0;
                label.Color = text;
            }
        }
    }
}
