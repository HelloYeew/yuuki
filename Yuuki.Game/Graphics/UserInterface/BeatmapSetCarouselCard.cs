// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// One song in the list: a row of the vertical rail or a tile of the horizontal strip. Rows and tiles keep
/// one size and have no focused look of their own; the carousel's focus window shows the focused song.
/// </summary>
public partial class BeatmapSetCarouselCard : CarouselCard<BeatmapSet>
{
    // Cover decode caps: a little above the size each face shows the cover at (see DelayedLoadCover.DecodeTargetSize).
    private static readonly Vector2 tile_decode_size = new Vector2(256, 256);
    private static readonly Vector2 row_decode_size = new Vector2(128, 128);

    // Covers ease in from a slight zoom as they finish loading, rather than a flat fade.
    private const float cover_load_in_scale = 1.06f;

    private const float row_cover_size = 48;
    private const float row_text_x = 74;
    private const float tile_cover_size = 120;

    private static readonly string[] placeholder_palette =
    {
        "#F4A6C0", "#F6C177", "#9CCFD8", "#C4A7E7", "#A3BE8C", "#EBCB8B", "#88C0D0", "#D08770"
    };

    public BeatmapSet BeatmapSet { get; private set; } = null!;

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

    [Resolved(canBeNull: true)]
    private ScreenPalette? screenPalette { get; set; }

    [Resolved(canBeNull: true)]
    private IYuukiColorProvider? colorProvider { get; set; }

    private Reactive<bool>? useUnicodeInfo;

    private Color ink = Color.Black;
    private Color placeholderColor = Color.Transparent;
    private bool vertical;

    // Both faces are built lazily: a card in the strip never allocates the row's drawables, and vice versa.
    // Only the active face's cover is given a source.
    private Container? rowFace;
    private Box? rowHover;
    private DelayedLoadCover? rowCover;
    private FlowContainer? rowText;
    private ScrollingText? rowTitle;
    private ScrollingText? rowArtist;
    private FlowContainer? rowExtras;
    private BestMark? rowBest;
    private LevelSkyline? rowSkyline;
    private LaneLabel? rowLanes;

    private Container? tileFace;
    private DelayedLoadCover? tileCover;
    private ScrollingText? tileTitle;
    private ScrollingText? tileArtist;

    public BeatmapSetCarouselCard()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (config != null)
            useUnicodeInfo = config.Get<bool>(YuukiSetting.UseUnicodeInfo);

        ink = ScreenPalette.Resolve(screenPalette, colorProvider).Text;
    }

    /// <summary>
    /// Builds the rail's row on first use: the cover, title over artist, and on the right your best mark, the
    /// level skyline and the lanes. Ink straight on the glass, a hairline between rows.
    /// </summary>
    private void ensureRow()
    {
        if (rowFace != null)
            return;

        Add(rowFace = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 14,
            Alpha = vertical ? 1 : 0,
            Children = new Drawable[]
            {
                rowHover = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = ink,
                    Alpha = 0
                },
                rowCover = new DelayedLoadCover
                {
                    // A cover fills its parent by default; this one has its own size.
                    RelativeSizeAxes = Axes.None,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 10,
                    Size = new Vector2(row_cover_size),
                    Masking = true,
                    CornerRadius = 8,
                    DecodeTargetSize = row_decode_size,
                    LoadInScale = cover_load_in_scale
                },
                // Its width is the room left of the extras, set every frame, so long names scroll within it.
                rowText = new FlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = row_text_x,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 1),
                    Children = new Drawable[]
                    {
                        rowTitle = new ScrollingText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 21, FontWeight.Bold),
                            Color = ink
                        },
                        rowArtist = new ScrollingText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.SemiBold),
                            Color = SetlistColors.Secondary(ink)
                        }
                    }
                },
                rowExtras = new FlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    X = -16,
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        rowBest = new BestMark
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Color = ink
                        },
                        rowSkyline = new LevelSkyline
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        },
                        rowLanes = new LaneLabel
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Color = ink
                        }
                    }
                },
                new Container
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Padding = new MarginPadding { Left = 12, Right = 12 },
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = ink.WithAlpha(0.1f)
                    }
                }
            }
        });

        populate();
    }

    /// <summary>
    /// Builds the strip's tile on first use: the cover, with the title and artist under it.
    /// </summary>
    private void ensureTile()
    {
        if (tileFace != null)
            return;

        Add(tileFace = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = vertical ? 0 : 1,
            Children = new Drawable[]
            {
                tileCover = new DelayedLoadCover
                {
                    RelativeSizeAxes = Axes.None,
                    X = 6,
                    Size = new Vector2(tile_cover_size),
                    Masking = true,
                    CornerRadius = 10,
                    BorderColor = ink.WithAlpha(0.3f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Color = Color.Black.WithAlpha(0.2f),
                        Radius = 6,
                        Offset = new Vector2(0, 3)
                    },
                    DecodeTargetSize = tile_decode_size,
                    LoadInScale = cover_load_in_scale
                },
                tileTitle = new ScrollingText
                {
                    X = 6,
                    Y = tile_cover_size + 8,
                    Width = tile_cover_size + 10,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 15, FontWeight.Bold),
                    Color = ink
                },
                tileArtist = new ScrollingText
                {
                    X = 6,
                    Y = tile_cover_size + 28,
                    Width = tile_cover_size + 10,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 12, FontWeight.SemiBold),
                    Color = SetlistColors.Secondary(ink)
                }
            }
        });

        populate();
    }

    /// <summary>
    /// Reconfigure this (possibly recycled) card for a given beatmap set. Called by the pool's setup
    /// action every time the card is taken out of the pool.
    /// </summary>
    public override void Apply(BeatmapSet set, int index)
    {
        BeatmapSet = set;
        placeholderColor = ColorExtensions.FromHex(placeholder_palette[index % placeholder_palette.Length]);

        // Orientation is assigned by the pool right before Apply (but OnOrientationChanged, which sets
        // `vertical`, only runs later on attach), so read it here to build the correct face straight away.
        vertical = Orientation?.Value == CarouselOrientation.Vertical;

        ensureActiveFace();
        populate();
    }

    /// <summary>
    /// Fills every built face with the current set. Safe to call before either face exists.
    /// </summary>
    private void populate()
    {
        if (BeatmapSet == null)
            return;

        updateText();

        var beatmaps = beatmapsCollection?.GetBeatmapsFromBeatmapSet(BeatmapSet) ?? Array.Empty<Beatmap>();

        if (rowFace != null)
        {
            rowLanes!.Text = BeatmapUtility.FormatLaneRange(beatmaps);
            rowSkyline!.SetDifficulties(beatmaps);
            rowBest!.SetBest(scoreStore?.GetBestMark(BeatmapSet));
            rowCover!.PlaceholderColor = placeholderColor;
        }

        if (tileCover != null)
            tileCover.PlaceholderColor = placeholderColor;

        updateCoverSources();
    }

    private void ensureActiveFace()
    {
        if (vertical)
            ensureRow();
        else
            ensureTile();
    }

    /// <summary>
    /// Points only the active face's cover at the album cover, and clears the other so we never decode both.
    /// </summary>
    private void updateCoverSources()
    {
        if (BeatmapSet == null)
            return;

        ICoverSource? source = BeatmapSet.Cover == null && string.IsNullOrEmpty(BeatmapSet.CoverStoragePath)
            ? null
            : new BeatmapSetCoverSource(BeatmapSet, storage);

        rowCover?.SetSource(vertical ? source : null);
        tileCover?.SetSource(vertical ? null : source);
    }

    protected override void OnOrientationChanged(CarouselOrientation orientation)
    {
        vertical = orientation == CarouselOrientation.Vertical;

        ensureActiveFace();

        if (rowFace != null)
            rowFace.Alpha = vertical ? 1 : 0;
        if (tileFace != null)
            tileFace.Alpha = vertical ? 0 : 1;

        updateCoverSources();
    }

    public override void Update()
    {
        base.Update();

        // The row's text runs up to the extras on the right, and scrolls when it's longer.
        if (rowFace != null && vertical)
            rowText!.Width = Math.Max(0, rowFace.DrawWidth - row_text_x - rowExtras!.DrawWidth - 32);
    }

    public override bool OnHover(MouseEvent e)
    {
        rowHover?.FadeTo(0.06f, 150, Easing.OutQuint);

        if (tileCover != null)
            tileCover.BorderThickness = 3;

        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        rowHover?.FadeOut(300, Easing.OutQuint);

        if (tileCover != null)
            tileCover.BorderThickness = 0;

        return base.OnHoverLost(e);
    }

    /// <summary>
    /// The lane counts on show ("4K", "4-7K"), from whichever face is built.
    /// </summary>
    public string LaneText => rowLanes?.Text ?? BeatmapUtility.FormatLaneRange(beatmapsCollection?.GetBeatmapsFromBeatmapSet(BeatmapSet) ?? Array.Empty<Beatmap>());

    private void updateText()
    {
        if (BeatmapSet == null)
            return;

        bool unicode = useUnicodeInfo?.Value ?? false;
        string title = BeatmapSet.GetDisplayTitle(unicode);
        title = string.IsNullOrEmpty(title) ? "Untitled" : title;
        string artist = BeatmapSet.GetDisplayArtist(unicode);

        if (rowTitle != null)
        {
            rowTitle.Text = title;
            rowArtist!.Text = artist;
        }

        if (tileTitle != null)
        {
            tileTitle.Text = title;
            tileArtist!.Text = artist;
        }
    }

    private void onUseUnicodeInfoChanged(ValueChangedEvent<bool> e) => updateText();

    protected override void OnParentChanged()
    {
        if (Parent == null)
        {
            // A pooled card is detached precisely so it can be reused, so it opts out of removal disposal —
            // nothing releases its covers on the way out, and it would otherwise sit in the pool holding GPU
            // textures for content that is not on screen. Clearing the covers here is the unload half of
            // pooling, and the covers reload on reuse.
            rowCover?.SetSource(null);
            tileCover?.SetSource(null);

            // Same reasoning for the setting: an idle pooled card should not be reshaping its text.
            if (useUnicodeInfo != null)
                useUnicodeInfo.ValueChanged -= onUseUnicodeInfoChanged;
        }
        else if (useUnicodeInfo != null)
        {
            useUnicodeInfo.ValueChanged += onUseUnicodeInfoChanged;
        }

        base.OnParentChanged();
    }
}
