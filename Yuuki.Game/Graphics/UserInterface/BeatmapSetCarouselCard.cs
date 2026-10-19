// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
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
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class BeatmapSetCarouselCard : CarouselCard<BeatmapSet>
{
    /// <summary>
    /// The edge length of a card at scale 1.
    /// </summary>
    public const float CARD_SIZE = 118f;

    // Cover decode caps. A card only shows a small region, so we cap the decoded/uploaded texture to a
    // little above its on-screen size (see DelayedLoadCover.DecodeTargetSize). The square face is ~118px
    // (up to ~168 selected); the bar face is a wide, short strip that shows only a horizontal band.
    private static readonly Vector2 album_decode_size = new Vector2(256, 256);
    private static readonly Vector2 background_decode_size = new Vector2(768, 128);

    // Covers ease in from a slight zoom as they finish loading, rather than a flat fade.
    private const float cover_load_in_scale = 1.06f;

    private static readonly string[] palette =
    {
        "#F4A6C0", "#F6C177", "#9CCFD8", "#C4A7E7", "#A3BE8C", "#EBCB8B", "#88C0D0", "#D08770"
    };

    public BeatmapSet BeatmapSet { get; private set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    private Reactive<bool>? useUnicodeInfo;

    // The square face shows the album cover; the bar face shows the beatmap background.
    // Only the active face's cover is given a source, so we never decode both images at once.
    // Both faces are built lazily (see ensureSquareFace/ensureBarFace) — a card in a horizontal carousel
    // never allocates the bar face's drawable tree, and vice-versa.
    private DelayedLoadCover? albumCover;
    private DelayedLoadCover? backgroundCover;

    // The two swappable faces. Only one face is visible at a time, and only built faces are non-null.
    private Container? squareFace;
    private Container? barFace;

    private ScrollingText? titleText;
    private ScrollingText? barTitleText;
    private SpriteText? barArtistText;
    private FlowContainer? difficultyDots;
    private LaneCountBadge? squareLanes;
    private LaneCountBadge? barLanes;

    private Box? selectedOverlay;
    private bool vertical;

    private Box flashBox = null!;

    /// <summary>
    /// How bright the selected card flashes on each beat
    /// </summary>
    private const float beat_flash_alpha = 0.3f;

    public BeatmapSetCarouselCard()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 12;
        EdgeEffect = SHADOW_EDGE;

        // Additive glow used to pulse the card on the beat while it is the selected set. A positive depth
        // keeps it in front of the (lazily added, depth-0) faces — higher depth = front — so it always
        // washes over the cover.
        Add(flashBox = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Color = Color.White,
            Alpha = 0,
            Depth = 1,
            Blending = BlendingMode.Additive
        });

        // Invisible helper that ticks off the beatmap BPM (or a steady default beat while nothing plays)
        // and drives the flash.
        var beatSync = new BeatSyncContainer
        {
            AlwaysPresent = true,
            AllowBeatSyncWhenStopped = true,
            UseDefaultTempoWhenStopped = true
        };
        beatSync.NewBeat += onNewBeat;
        Add(beatSync);
    }

    /// <summary>
    /// Builds the coverflow (square) face on first use: album cover, bottom scrim, centred title along
    /// the bottom edge, the lane counts in the corner.
    /// </summary>
    private void ensureSquareFace()
    {
        if (squareFace != null)
            return;

        Add(squareFace = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Alpha = vertical ? 0f : 1f,
            Children = new Drawable[]
            {
                albumCover = new DelayedLoadCover
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    DecodeTargetSize = album_decode_size,
                    LoadInScale = cover_load_in_scale
                },
                new Box
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Color = Color.Black.WithAlpha(0.5f)
                },
                titleText = new ScrollingText()
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    ScrollSpeed = 60f,
                    Size = new Vector2(0.9f, 20),
                    RelativeSizeAxes = Axes.X,
                    Position = new Vector2(0, -8),
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 14, FontWeight.Bold),
                    Color = Color.White
                },
                squareLanes = new LaneCountBadge(11)
                {
                    Position = new Vector2(8)
                }
            }
        });

        populateFaces();
    }

    /// <summary>
    /// Builds the bar (list) face on first use: background cover under a left-to-right dark
    /// gradient so the left-aligned title/artist stay readable, plus the lane counts and difficulty dots.
    /// </summary>
    private void ensureBarFace()
    {
        if (barFace != null)
            return;

        Add(barFace = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Alpha = vertical ? 1f : 0f,
            Children = new Drawable[]
            {
                backgroundCover = new DelayedLoadCover
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    DecodeTargetSize = background_decode_size,
                    LoadInScale = cover_load_in_scale
                },
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.Both,
                    ColorInfo = ColorInfo.GradientHorizontal(Color.Black.WithAlpha(0.8f), Color.Black.WithAlpha(0f))
                },
                selectedOverlay = new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.White,
                    Alpha = 0
                },
                new FlowContainer
                {
                    // set panel: a vertical block centred on the left, title over artist over a
                    // status/difficulty row, nudged slightly above centre with a snug left inset.
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 1),
                    Padding = new MarginPadding { Left = 20, Right = 100, Top = 6, Bottom = 12 },
                    Children = new Drawable[]
                    {
                        barTitleText = new ScrollingText()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            ScrollSpeed = 60f,
                            RelativeSizeAxes = Axes.X,
                            Size = new Vector2(1f, 30),
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                            Color = Color.White
                        },
                        barArtistText = new SpriteText()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Regular),
                            Color = Color.White.WithAlpha(0.85f)
                        },
                        new FlowContainer
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Horizontal,
                            Spacing = new Vector2(8, 0),
                            // Less above than the dots alone had, and a slim badge, so the taller row
                            // doesn't open a gap under the artist or push the block off centre.
                            Padding = new MarginPadding { Top = 3 },
                            Children = new Drawable[]
                            {
                                barLanes = new LaneCountBadge(11, verticalPadding: 1.5f)
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft
                                },
                                difficultyDots = new FlowContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FlowDirection.Horizontal,
                                    Spacing = new Vector2(5, 0)
                                }
                            }
                        }
                    }
                }
            }
        });

        populateFaces();
    }

    private void onNewBeat(int beat, double beatLength, ChannelAmplitudes amplitudes)
    {
        // Only the currently selected (working) set pulses.
        if (BeatmapSet == null || workingBeatmap?.BeatmapSet == null || !Equals(workingBeatmap.BeatmapSet, BeatmapSet))
            return;

        // The same flash every beat: the beat sets it, not how loud the song is there.
        flashBox.Alpha = beat_flash_alpha;
        flashBox.FadeTo(0f, beatLength, Easing.OutQuint);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (config != null)
            useUnicodeInfo = config.Get<bool>(YuukiSetting.UseUnicodeInfo);
    }

    /// <summary>
    /// Reconfigure this (possibly recycled) card for a given beatmap set. Called by the pool's setup
    /// action every time the card is taken out of the pool.
    /// </summary>
    public override void Apply(BeatmapSet set, int index)
    {
        BeatmapSet = set;

        placeholderColor = ColorExtensions.FromHex(palette[index % palette.Length]);

        // Orientation is assigned by the pool right before Apply (but OnOrientationChanged, which sets
        // `vertical`, only runs later on attach), so read it here to build the correct face straight away
        // rather than building the square face now and the bar face moments later.
        vertical = Orientation?.Value == CarouselOrientation.Vertical;

        ensureActiveFace();
        populateFaces();
    }

    private Color placeholderColor = Color.Transparent;

    /// <summary>
    /// Fills every built face with the current set's text, lane counts, difficulty dots, cover
    /// placeholder Color, and cover source. Safe to call before either face exists (it only touches built
    /// faces).
    /// </summary>
    private void populateFaces()
    {
        if (BeatmapSet == null)
            return;

        updateText();

        // One look through the library for both the lanes and the dots.
        var beatmaps = beatmapsCollection?.GetBeatmapsFromBeatmapSet(BeatmapSet) ?? Array.Empty<Beatmap>();

        string lanes = BeatmapUtility.FormatLaneRange(beatmaps);
        if (squareLanes != null)
            squareLanes.Text = lanes;
        if (barLanes != null)
            barLanes.Text = lanes;

        buildDifficultyDots(beatmaps);

        if (albumCover != null)
            albumCover.PlaceholderColor = placeholderColor;
        if (backgroundCover != null)
            backgroundCover.PlaceholderColor = placeholderColor;

        updateCoverSources();
    }

    private void ensureActiveFace()
    {
        if (vertical)
            ensureBarFace();
        else
            ensureSquareFace();
    }

    /// <summary>
    /// Point only the active face's cover at its image — album art for the square (horizontal) face,
    /// beatmap background for the bar (vertical) face — and clear the other so we never decode both.
    /// </summary>
    private void updateCoverSources()
    {
        if (BeatmapSet == null)
            return;

        if (vertical)
        {
            albumCover?.SetSource(null);
            backgroundCover?.SetSource(
                string.IsNullOrEmpty(BeatmapSet.BackgroundStoragePath) && BeatmapSet.Background == null
                    ? null
                    : new BeatmapSetBackgroundSource(BeatmapSet, storage));
        }
        else
        {
            backgroundCover?.SetSource(null);
            albumCover?.SetSource(
                BeatmapSet.Cover == null && string.IsNullOrEmpty(BeatmapSet.CoverStoragePath)
                    ? null
                    : new BeatmapSetCoverSource(BeatmapSet, storage));
        }
    }

    /// <summary>
    /// Rebuilds the little row of difficulty-Colored dots (one per beatmap in the set),
    /// panel. No-op before the bar face (which owns the dots) is built.
    /// </summary>
    private void buildDifficultyDots(Beatmap[] beatmaps)
    {
        if (difficultyDots == null)
            return;

        difficultyDots.Clear();

        int count = Math.Min(beatmaps.Length, 14);

        for (int i = 0; i < count; i++)
        {
            difficultyDots.Add(new Circle
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(10),
                Color = BeatmapUtility.ColorForDifficulty(beatmaps[i].DifficultyRating)
            });
        }
    }

    protected override void OnOrientationChanged(CarouselOrientation orientation)
    {
        vertical = orientation == CarouselOrientation.Vertical;

        // Build the now-active face if this is the first time we've been shown in this orientation.
        ensureActiveFace();

        if (squareFace != null)
            squareFace.Alpha = vertical ? 0f : 1f;
        if (barFace != null)
            barFace.Alpha = vertical ? 1f : 0f;

        updateCoverSources();
    }

    public override void Update()
    {
        base.Update();

        // Lighten the selected set's bar. Only meaningful on the vertical (bar) face.
        bool selected = vertical
                        && BeatmapSet != null
                        && workingBeatmap?.BeatmapSet != null
                        && Equals(workingBeatmap.BeatmapSet, BeatmapSet);

        if (selectedOverlay != null)
            selectedOverlay.Alpha = selected ? 0.2f : 0f;

        EdgeEffect = selected ? SELECTED_EDGE : SHADOW_EDGE;
    }

    /// <summary>
    /// The lane counts on show ("4K", "4-7K"), from whichever face is built.
    /// </summary>
    public string LaneText => (barLanes ?? squareLanes)?.Text ?? "";

    private void updateText()
    {
        if (BeatmapSet == null)
            return;

        bool unicode = useUnicodeInfo?.Value ?? false;
        string title = BeatmapSet.GetDisplayTitle(unicode);
        title = string.IsNullOrEmpty(title) ? "Untitled" : title;

        if (titleText != null)
            titleText.Text = title;
        if (barTitleText != null)
            barTitleText.Text = title;
        if (barArtistText != null)
            barArtistText.Text = BeatmapSet.GetDisplayArtist(unicode);
    }

    private void onUseUnicodeInfoChanged(ValueChangedEvent<bool> e) => updateText();

    protected override void OnParentChanged()
    {
        if (Parent == null)
        {
            // This hook stays deliberately, and is not the OnParentChanged workaround the disposal cascade
            // replaced. A pooled card is detached precisely so it can be reused, so it opts out of removal
            // disposal — nothing releases its covers on the way out, and it would otherwise sit in the pool
            // holding GPU textures for content that is not on screen. Cancelling the in-flight decode and
            // clearing the covers here is the unload half of pooling, and the covers reload on reuse.
            albumCover?.SetSource(null);
            backgroundCover?.SetSource(null);

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
