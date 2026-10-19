// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapFilterControl : Container
{
    /// <summary>
    /// How long typing has to pause before the search applies.
    /// </summary>
    private const double search_debounce = 150;

    private const float inset = 14;
    private const float row_gap = 8;
    private const float search_height = 56;
    private const float level_height = 30;
    private const float sort_height = 32;
    private const float level_tab_width = 86;
    private const float sort_width = 280;
    private const float magnifier_width = 58;

    /// <summary>
    /// How far past the right edge the vertical panel's background runs, so its slanted end is off-screen.
    /// </summary>
    private const float tail_cover = 60;

    /// <summary>
    /// The compact bar's width in the horizontal layout.
    /// </summary>
    public const float HORIZONTAL_WIDTH = 820;

    /// <summary>
    /// The panel's height in the vertical layout: what the vertical carousel is moved down by.
    /// </summary>
    public const float VERTICAL_HEIGHT = inset + search_height + row_gap + level_height + row_gap + sort_height + inset;

    /// <summary>
    /// The bar's height in the horizontal layout.
    /// </summary>
    public const float HORIZONTAL_HEIGHT = 12 + level_height + row_gap + sort_height + 12;

    private static Vector2 shear => UserInterfaceConstant.GentleShear;

    private static float slope => -shear.X;

    public BeatmapFilter Filter { get; } = new BeatmapFilter();

    /// <summary>
    /// Fired whenever the filter changes (typing settles a moment first).
    /// </summary>
    public event Action<BeatmapFilter>? FilterChanged;

    private YuukiColorPalette palette;

    private Container background = null!;
    private Container searchRow = null!;
    private FilterSearchBox searchBox = null!;
    private SpriteText countText = null!;
    private Container levelTab = null!;
    private LevelRangeSlider levelSlider = null!;
    private YuukiDropdown<BeatmapSort> sortDropdown = null!;

    private CarouselOrientation layout = CarouselOrientation.Horizontal;
    private float layoutWidth = HORIZONTAL_WIDTH;
    private int searchVersion;
    private int matchCount;

    public bool IsSearchFocused => searchBox?.HasFocus ?? false;

    /// <summary>
    /// The open sort list hangs below the panel, over the song list. Input only reaches children
    /// through a parent the pointer is over, so the panel claims the list's area while it's open —
    /// otherwise its choices can't be clicked and the song list underneath takes the press.
    /// </summary>
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
        base.ReceivePositionalInputAt(screenSpacePos)
        || (sortDropdown != null && sortDropdown.IsMenuOpen && sortDropdown.ReceivePositionalInputAt(screenSpacePos));

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        Children = new Drawable[]
        {
            background = shearedBox(palette.Backdrop, 0.75f, 12),
            searchRow = new Container
            {
                Height = search_height,
                Children = new Drawable[]
                {
                    shearedBox(palette.Control, 1, 8),
                    new Container
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        RelativeSizeAxes = Axes.Y,
                        Width = magnifier_width,
                        Children = new Drawable[]
                        {
                            shearedBox(palette.Backdrop, 1, 8),
                            new IconSprite
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = IconUsage.Search,
                                IconSize = 26,
                                Color = Color.White
                            }
                        }
                    },
                    searchBox = new FilterSearchBox
                    {
                        X = inset,
                        Y = 2,
                        Height = 34,
                        PlaceholderText = "search...",
                        ReleaseFocusOnCommit = true
                    },
                    countText = new SpriteText
                    {
                        X = inset + 14,
                        Y = 34,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 14, FontWeight.Bold),
                        Color = palette.AccentLight
                    }
                }
            },
            levelTab = new Container
            {
                Size = new Vector2(level_tab_width, level_height),
                Children = new Drawable[]
                {
                    shearedBox(palette.Backdrop, 1, 6),
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "Level",
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold),
                        Color = Color.White
                    }
                }
            },
            levelSlider = new LevelRangeSlider(palette, shear),
            sortDropdown = new YuukiDropdown<BeatmapSort>
            {
                Width = sort_width,
                HeaderShear = shear,
                ItemHeight = sort_height,
                Label = "Sort",
                LabelWidth = 76,
                Formatter = sort => sort == BeatmapSort.Bpm ? "BPM" : sort.ToString(),
                Items = Enum.GetValues<BeatmapSort>()
            }
        };

        sortDropdown.Current.Value = Filter.Sort;
        sortDropdown.Current.BindValueChanged(e =>
        {
            Filter.Sort = e.NewValue;
            FilterChanged?.Invoke(Filter);
        });

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

        levelSlider.Min.BindValueChanged(_ => onLevelChanged());
        levelSlider.Max.BindValueChanged(_ => onLevelChanged());

        SetLayout(layout, layoutWidth);
        SetMatchCount(matchCount);
    }

    /// <summary>A sheared, rounded fill for a row: it leans; whatever sits on it doesn't.</summary>
    private static Container shearedBox(Color color, float alpha, float cornerRadius) => new Container
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        RelativeSizeAxes = Axes.Both,
        Masking = true,
        CornerRadius = cornerRadius,
        Shear = shear,
        Child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = color,
            Alpha = alpha
        }
    };

    /// <summary>
    /// Arranges the control for the carousel's orientation: stacked rows <paramref name="width"/> wide
    /// over the vertical list, or the compact bar above the horizontal strip.
    /// </summary>
    public void SetLayout(CarouselOrientation orientation, float width = HORIZONTAL_WIDTH)
    {
        layout = orientation;
        layoutWidth = width;

        if (searchRow == null)
            return;

        float height = orientation == CarouselOrientation.Vertical ? VERTICAL_HEIGHT : HORIZONTAL_HEIGHT;
        Size = new Vector2(width, height);

        // The panel's slanted left edge at a given height (0 at its middle, further left lower down).
        float edgeAt(float y) => slope * (height / 2 - y);

        background.RelativeSizeAxes = Axes.Y;

        if (orientation == CarouselOrientation.Vertical)
        {
            // Flush with the screen's right edge, its slanted end tucked past it.
            background.Width = width + tail_cover;

            float right = width - inset;

            float searchY = inset;
            float levelY = searchY + search_height + row_gap;
            float sortY = levelY + level_height + row_gap;

            placeRow(searchRow, searchY, search_height, edgeAt(searchY + search_height / 2) + inset, right);

            float levelLeft = edgeAt(levelY + level_height / 2) + inset;
            placeRow(levelTab, levelY, level_height, levelLeft, levelLeft + level_tab_width);
            placeRow(levelSlider, levelY, level_height, levelLeft + level_tab_width + 10, right);

            sortDropdown.Position = new Vector2(edgeAt(sortY + sort_height / 2) + inset, sortY);
        }
        else
        {
            // A floating bar: search on the left, level and sort stacked on the right.
            background.Width = width;

            const float search_width = 420;
            float right = width - inset;

            float levelY = 12;
            float sortY = levelY + level_height + row_gap;
            float searchY = (height - search_height) / 2;

            float searchLeft = edgeAt(height / 2) + inset;
            placeRow(searchRow, searchY, search_height, searchLeft, searchLeft + search_width);

            float groupLeft = inset + search_width + 26;
            float levelLeft = groupLeft + edgeAt(levelY + level_height / 2);
            placeRow(levelTab, levelY, level_height, levelLeft, levelLeft + level_tab_width);
            placeRow(levelSlider, levelY, level_height, levelLeft + level_tab_width + 10, right);

            sortDropdown.Position = new Vector2(groupLeft + edgeAt(sortY + sort_height / 2), sortY);
        }

        searchBox.Width = searchRow.Width - inset - magnifier_width - 8;
    }

    private static void placeRow(Drawable row, float y, float height, float left, float right)
    {
        row.Anchor = Anchor.TopLeft;
        row.Origin = Anchor.TopLeft;
        row.Position = new Vector2(left, y);
        row.Size = new Vector2(Math.Max(0, right - left), height);
    }

    /// <summary>
    /// Shows how many songs the filter lets through.
    /// </summary>
    public void SetMatchCount(int count)
    {
        matchCount = count;

        if (countText != null)
            countText.Text = count == 1 ? "1 match" : $"{count:N0} matches";
    }

    /// <summary>
    /// Types <paramref name="text"/> into the search, as if typed (it applies once typing would have settled).
    /// </summary>
    public void SetSearch(string text) => searchBox.Text.Value = text;

    /// <summary>
    /// Opens the level range back up to every level.
    /// </summary>
    public void ClearLevelRange()
    {
        levelSlider.Min.Value = 0;
        levelSlider.Max.Value = BeatmapFilter.MAX_LEVEL;
    }

    /// <summary>
    /// Puts the cursor in the search box (typing on song select starts a search).
    /// </summary>
    public void FocusSearch() => GetContainingFocusManager()?.ChangeFocus(searchBox);

    /// <summary>
    /// Takes the cursor out of the search box, if it's there.
    /// </summary>
    public void ReleaseSearchFocus()
    {
        if (IsSearchFocused)
            GetContainingFocusManager()?.ChangeFocus(null);
    }

    /// <summary>
    /// Sets the sort without the dropdown being used (restoring the saved one).
    /// </summary>
    public void SetSort(BeatmapSort sort)
    {
        Filter.Sort = sort;

        if (sortDropdown != null)
            sortDropdown.Current.Value = sort;
    }

    private void onLevelChanged()
    {
        Filter.MinLevel = levelSlider.Min.Value;
        Filter.MaxLevel = levelSlider.Max.Value;
        FilterChanged?.Invoke(Filter);
    }

    /// <summary>
    /// The search field: the game's text box drawn bare, on the search row's own sheared fill.
    /// </summary>
    private partial class FilterSearchBox : YuukiTextBox
    {
        public FilterSearchBox()
        {
            SpriteText.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold);
            PlaceholderSprite.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Regular);
            PlaceholderSprite.Color = Color.White.WithAlpha(0.6f);
        }

        protected override Drawable? CreateBackground() => null;
    }
}
