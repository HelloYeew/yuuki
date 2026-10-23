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

    /// <summary>
    /// Faster than this (items a second) a row has gone by before its cover could be decoded and seen, so
    /// it comes in with its song's color and its cover follows once the list slows. A cover at hand (on
    /// show elsewhere, or seen a moment ago) shows regardless.
    /// </summary>
    private const float cover_speed_limit = 40;

    /// <summary>
    /// Whether the cover on show is waiting for the list to slow before it's decoded.
    /// </summary>
    private bool coverDeferred;

    /// <summary>
    /// The sizes of a card's faces, at rest and (Skim…) while the list skims: smaller covers, the title and
    /// artist a little smaller, the extras gone.
    /// </summary>
    private sealed record FaceSizes(
        float RowCover, float SkimRowCover, float RowTextX, float SkimRowTextX, float SkimTextScale,
        int TitleFont, int ArtistFont, float ExtrasText, float BestText, float SkylineHeight, float SkylineBar,
        float TileCover, float SkimTileCover, int TileTitleFont, int TileArtistFont, float TileArtistOffset, float TileChipText);

    private static readonly FaceSizes desktop_sizes = new FaceSizes(
        48, 34, 74, 60, 0.86f,
        21, 15, 14, 12.5f, 26, 6,
        120, 84, 15, 12, 28, 11);

    // A phone's: a little bigger in the rail (its rows are only a little taller than the desktop's), and
    // bigger in the strip, for a thumb.
    private static readonly FaceSizes phone_sizes = new FaceSizes(
        54, 40, 80, 66, 0.85f,
        23, 17, 15, 13.5f, 28, 7,
        132, 100, 18, 14, 31, 12);

    /// <summary>
    /// How far up from the foot of a tile's cover its chips sit.
    /// </summary>
    private const float tile_chip_inset = 6;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    private FaceSizes sizes = desktop_sizes;

    /// <summary>
    /// The skim amount the faces are laid out for.
    /// </summary>
    private float laidOutSkim = -1;

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

    [Resolved(canBeNull: true)]
    private CoverColorStore? coverColors { get; set; }

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
    private Container? tileChips;
    private BestMark? tileBest;
    private BestMark.Blade? tileLanes;
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
                    Size = new Vector2(sizes.RowCover),
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
                    X = sizes.RowTextX,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 1),
                    Children = new Drawable[]
                    {
                        rowTitle = new ScrollingText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, sizes.TitleFont, FontWeight.Bold),
                            Color = ink
                        },
                        rowArtist = new ScrollingText
                        {
                            RelativeSizeAxes = Axes.X,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, sizes.ArtistFont, FontWeight.SemiBold),
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
                        rowBest = new BestMark(sizes.BestText)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        },
                        rowSkyline = new LevelSkyline(sizes.SkylineHeight, sizes.SkylineBar)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft
                        },
                        rowLanes = new LaneLabel(sizes.ExtrasText)
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
    /// Builds the strip's tile on first use: the cover, with your best mark and the lanes at its foot, and
    /// the title and artist under it. The chips carry their own colors, so they read on any cover.
    /// </summary>
    private void ensureTile()
    {
        if (tileFace != null)
            return;

        Add(tileFace = new Container
        {
            // Centred, so a press squeezes it about its middle.
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Alpha = vertical ? 0 : 1,
            Children = new Drawable[]
            {
                tileCover = new DelayedLoadCover
                {
                    RelativeSizeAxes = Axes.None,
                    X = 6,
                    Size = new Vector2(sizes.TileCover),
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
                // Placed on the cover as it's sized (see layOutForSkim). Beside the cover rather than in it:
                // the cover clears its children for each new source.
                tileChips = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        tileBest = new BestMark(sizes.TileChipText)
                        {
                            Origin = Anchor.BottomLeft
                        },
                        tileLanes = new BestMark.Blade(sizes.TileChipText)
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.BottomRight
                        }
                    }
                },
                tileTitle = new ScrollingText
                {
                    X = 6,
                    Y = sizes.TileCover + 8,
                    Width = sizes.TileCover + 10,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, sizes.TileTitleFont, FontWeight.Bold),
                    Color = ink
                },
                tileArtist = new ScrollingText
                {
                    X = 6,
                    Y = sizes.TileCover + sizes.TileArtistOffset,
                    Width = sizes.TileCover + 10,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, sizes.TileArtistFont, FontWeight.SemiBold),
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

        // Orientation is assigned by the pool right before Apply (but OnOrientationChanged, which sets
        // `vertical`, only runs later on attach), so read it here to build the correct face straight away.
        vertical = Orientation?.Value == CarouselOrientation.Vertical;

        ensureActiveFace();
        populate();

        // A card back from the pool may have gone mid-flash.
        if (rowHover != null)
        {
            rowHover.ClearTransforms();
            rowHover.Alpha = 0;
        }

        if (tileFace != null)
        {
            tileFace.ClearTransforms();
            tileFace.Scale = Vector2.One;
        }
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

        // Until the cover's in (or for good, with none), the song's own color, as its window wears: a row
        // coming in reads as that song still loading rather than a colored square standing in for it.
        placeholderColor = SetlistColors.ForSong(BeatmapSet, beatmaps, coverColors).Fill.Darken(0.15f);

        if (rowFace != null)
        {
            rowLanes!.Text = BeatmapUtility.FormatLaneRange(beatmaps);
            rowSkyline!.SetDifficulties(beatmaps);
            rowBest!.SetBest(scoreStore?.GetBestMark(BeatmapSet));
            rowCover!.PlaceholderColor = placeholderColor;
        }

        if (tileFace != null)
        {
            tileCover!.PlaceholderColor = placeholderColor;
            tileBest!.SetBest(scoreStore?.GetBestMark(BeatmapSet));

            // Plain, against the best mark's colors: white's tone-on-tone.
            tileLanes!.Apply(new BestMark.Piece(BeatmapUtility.FormatLaneRange(beatmaps), SetlistColors.TierFill(Color.White), SetlistColors.TierInk(Color.White)));

            // Laid out again, as whether the lanes fit beside the best mark is up to both.
            laidOutSkim = -1;
        }

        updateCoverSources();
    }

    private void rebuildFaces(FaceSizes newSizes)
    {
        sizes = newSizes;

        if (rowFace != null)
            Remove(rowFace);

        if (tileFace != null)
            Remove(tileFace);

        rowFace = null;
        tileFace = null;

        if (BeatmapSet != null)
        {
            ensureActiveFace();
            populate();
        }
    }

    private void ensureActiveFace()
    {
        if (vertical)
            ensureRow();
        else
            ensureTile();

        // A face built just now (or a card come back from the pool) is laid out for the skim on the next update.
        laidOutSkim = -1;
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

        bool decode = (Motion?.Speed ?? 0) <= cover_speed_limit;

        rowCover?.SetSource(vertical ? source : null, decode);
        tileCover?.SetSource(vertical ? null : source, decode);

        coverDeferred = source != null && !decode && (vertical ? rowCover : tileCover)?.HasCover != true;
    }

    /// <summary>
    /// Ready once the face on show has its cover, or has none coming.
    /// </summary>
    public override bool IsReady => !((vertical ? rowCover : tileCover)?.IsLoading ?? false);

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

        // The faces are built for one layout's sizes; a change of layout builds them again.
        var wanted = gameLayout?.IsPhone == true ? phone_sizes : desktop_sizes;

        if (wanted != sizes)
            rebuildFaces(wanted);

        if (coverDeferred && (Motion?.Speed ?? 0) <= cover_speed_limit)
            updateCoverSources();

        float skim = Motion?.Skim ?? 0;

        if (skim != laidOutSkim)
            layOutForSkim(skim);

        // The row's text runs up to the extras on the right (as far as they're still there), and scrolls
        // when it's longer.
        if (rowFace != null && vertical)
        {
            float room = rowFace.DrawWidth - rowText!.X - rowExtras!.DrawWidth * (1 - skim) - 32;
            rowText.Width = Math.Max(0, room / rowText.Scale.X);
        }
    }

    /// <summary>
    /// Compacts the faces as the list skims (<paramref name="skim"/> 0 at rest, 1 skimming): the title and
    /// artist stay, smaller; the best mark, skyline and lanes step aside.
    /// </summary>
    private void layOutForSkim(float skim)
    {
        laidOutSkim = skim;

        float lerp(float rest, float skimming) => rest + (skimming - rest) * skim;

        if (rowFace != null)
        {
            rowCover!.Size = new Vector2(lerp(sizes.RowCover, sizes.SkimRowCover));
            rowText!.X = lerp(sizes.RowTextX, sizes.SkimRowTextX);
            rowText.Scale = new Vector2(lerp(1, sizes.SkimTextScale));
            rowExtras!.Alpha = 1 - skim;
        }

        if (tileFace != null)
        {
            float cover = lerp(sizes.TileCover, sizes.SkimTileCover);

            tileCover!.Size = new Vector2(cover);
            tileTitle!.Y = cover + 8;
            tileTitle.Width = cover + 10;
            tileArtist!.Y = cover + sizes.TileArtistOffset;
            tileArtist.Width = cover + 10;

            // In the foot's corners, stepping aside with the rest while skimming. Each is in from its edge as
            // far as the best mark's margin keeps it, clear of the blades' slanted ends. The lanes give way on
            // a cover too narrow for both (a long range beside a medal).
            float edge = sizes.TileChipText * 0.6f;

            tileBest!.Position = new Vector2(6, cover - tile_chip_inset);
            tileLanes!.Position = new Vector2(6 + cover - edge, cover - tile_chip_inset);
            tileChips!.Alpha = 1 - skim;

            float best = tileBest.Alpha > 0 ? tileBest.DrawWidth + edge : 0;
            tileLanes.Alpha = !string.IsNullOrEmpty(tileLanes.Text) && best + tileLanes.DrawWidth + edge * 2 <= cover ? 1 : 0;
        }
    }

    /// <summary>
    /// A press shows on the card at once, a moment before the list moves to it: a row darkens a little more
    /// than on hover, a tile gives a little. On its own, since the release goes to the carousel, which
    /// takes the press in case it's a drag.
    /// </summary>
    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (rowHover != null && vertical)
        {
            rowHover.ClearTransforms();
            rowHover.FadeTo(0.14f, 60, Easing.OutQuint).Then().FadeTo(IsHovered ? 0.06f : 0, 450, Easing.OutQuint);
        }

        if (tileFace != null && !vertical)
        {
            tileFace.ClearTransforms();
            tileFace.ScaleTo(0.96f, 70, Easing.OutQuint).Then().ScaleTo(1, 500, Easing.OutQuint);
        }

        return base.OnMouseDown(e);
    }

    // Cleared first, so a press's flash doesn't fade back to how the row was hovered when it was pressed.
    public override bool OnHover(MouseEvent e)
    {
        rowHover?.ClearTransforms();
        rowHover?.FadeTo(0.06f, 150, Easing.OutQuint);

        if (tileCover != null)
            tileCover.BorderThickness = 3;

        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        rowHover?.ClearTransforms();
        rowHover?.FadeOut(300, Easing.OutQuint);

        if (tileCover != null)
            tileCover.BorderThickness = 0;

        return base.OnHoverLost(e);
    }

    /// <summary>
    /// The lane counts on show ("4K", "4-7K"), from whichever face is built.
    /// </summary>
    public string LaneText => rowLanes?.Text ?? tileLanes?.Text ?? BeatmapUtility.FormatLaneRange(beatmapsCollection?.GetBeatmapsFromBeatmapSet(BeatmapSet) ?? Array.Empty<Beatmap>());

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
