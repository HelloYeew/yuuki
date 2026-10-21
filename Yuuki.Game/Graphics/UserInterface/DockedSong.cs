// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The song being picked from, docked at the start of the difficulty list while you choose: a card at the
/// top of the rail, or a small cover at the strip's left end. Clicking it goes back to the songs.
/// </summary>
public partial class DockedSong : ClickableContainer
{
    private const float card_cover_size = 168;
    private const float tile_cover_size = 130;

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    [Resolved(canBeNull: true)]
    private CoverColorStore? coverColors { get; set; }

    private readonly Box fill;
    private readonly DelayedLoadCover cover;
    private readonly FlowContainer text;
    private readonly ScrollingText title;
    private readonly ScrollingText artist;
    private readonly SpriteText details;
    private readonly FlowContainer hint;
    private readonly Container hintKey;
    private readonly SpriteText hintKeyText;
    private readonly SpriteText hintText;

    private BeatmapSet? set;
    private bool vertical = true;
    private int seenColorVersion;
    private Reactive<bool>? useUnicodeInfo;

    public DockedSong()
    {
        Masking = true;
        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Color = Color.Black.WithAlpha(0.35f),
            Radius = 18f,
            Offset = new Vector2(0f, 10f)
        };

        Children = new Drawable[]
        {
            fill = new Box { RelativeSizeAxes = Axes.Both },
            cover = new DelayedLoadCover
            {
                RelativeSizeAxes = Axes.None,
                Masking = true,
                CornerRadius = 12,
                DecodeTargetSize = new Vector2(256),
                LoadInScale = 1.06f
            },
            text = new FlowContainer
            {
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 1),
                Children = new Drawable[]
                {
                    title = new ScrollingText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 34, FontWeight.Bold)
                    },
                    artist = new ScrollingText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 19, FontWeight.SemiBold)
                    },
                    details = new SpriteText
                    {
                        Margin = new MarginPadding { Top = 12 },
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Bold)
                    }
                }
            },
            hint = new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    hintKey = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 6,
                        BorderThickness = 1.5f,
                        Child = hintKeyText = new SpriteText
                        {
                            Margin = new MarginPadding { Left = 6, Right = 6, Top = 2, Bottom = 2 },
                            Text = "ESC",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 12, FontWeight.ExtraBold)
                        }
                    },
                    hintText = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "BACK TO SONGS",
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.ExtraBold)
                    }
                }
            }
        };

        SetOrientation(CarouselOrientation.Vertical);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (config == null)
            return;

        useUnicodeInfo = config.Get<bool>(YuukiSetting.UseUnicodeInfo);
        BindValueChanged(useUnicodeInfo, _ => updateText());
    }

    public void SetOrientation(CarouselOrientation orientation)
    {
        vertical = orientation == CarouselOrientation.Vertical;

        if (vertical)
        {
            // A card at the top of the rail, in the space the filters leave.
            CornerRadius = 16;
            cover.Position = new Vector2(16);
            cover.Size = new Vector2(card_cover_size);
            text.Alpha = 1;
            text.Position = new Vector2(card_cover_size + 36, 22);
            hint.Anchor = hint.Origin = Anchor.BottomLeft;
            hint.Position = new Vector2(card_cover_size + 36, -18);
            hintKey.Alpha = 1;
        }
        else
        {
            // A small cover at the strip's left end; the info panel above already names the song.
            CornerRadius = 14;
            cover.Position = new Vector2(15);
            cover.Size = new Vector2(tile_cover_size);
            text.Alpha = 0;
            hint.Anchor = hint.Origin = Anchor.BottomCentre;
            hint.Position = new Vector2(0, -15);
            hintKey.Alpha = 0;
        }
    }

    /// <summary>
    /// The size this needs for <paramref name="orientation"/>, across the rail (whose width it fills) or as a tile.
    /// </summary>
    public static Vector2 TileSize => new Vector2(tile_cover_size + 30, tile_cover_size + 59);

    public const float CARD_HEIGHT = card_cover_size + 32;

    public void Show(BeatmapSet song)
    {
        set = song;

        var difficulties = beatmapsCollection?.GetBeatmapsFromBeatmapSet(song) ?? Array.Empty<Beatmap>();

        updateColors();
        updateText();
        details.Text = detailsOf(song, difficulties);

        cover.SetSource(song.Cover == null && string.IsNullOrEmpty(song.CoverStoragePath) ? null : new BeatmapSetCoverSource(song, storage));
    }

    /// <summary>
    /// The song's cover colour (the same as its focus window's), or its tier until that's known.
    /// </summary>
    private void updateColors()
    {
        if (set == null)
            return;

        seenColorVersion = coverColors?.Version ?? 0;

        var difficulties = beatmapsCollection?.GetBeatmapsFromBeatmapSet(set) ?? Array.Empty<Beatmap>();
        var (fillColor, ink) = SetlistColors.ForSong(set, difficulties, coverColors);

        fill.Color = fillColor;
        title.Color = ink;
        artist.Color = ink.WithAlpha(0.8f);
        details.Color = ink;
        hintText.Color = ink.WithAlpha(0.7f);
        hintKey.BorderColor = ink.WithAlpha(0.7f);
        hintKeyText.Color = ink.WithAlpha(0.7f);
        cover.PlaceholderColor = fillColor.Darken(0.15f);
    }

    private static string detailsOf(BeatmapSet song, IReadOnlyList<Beatmap> difficulties)
    {
        var parts = new List<string>();

        if (difficulties.Count > 0)
        {
            int low = difficulties.Min(BeatmapFilter.LevelOf), high = difficulties.Max(BeatmapFilter.LevelOf);
            parts.Add(low == high ? $"Lv {low}" : $"Lv {low}–{high}");
            parts.Add(BeatmapUtility.FormatLaneRange(difficulties));
        }

        if (song.TotalLength > 0)
            parts.Add(TimeSpan.FromMilliseconds(song.TotalLength).ToString(@"m\:ss"));

        if (song.BPM > 0)
            parts.Add($"{Math.Round(song.BPM)} BPM");

        return string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private void updateText()
    {
        if (set == null)
            return;

        bool unicode = useUnicodeInfo?.Value ?? false;
        string displayTitle = set.GetDisplayTitle(unicode);
        title.Text = string.IsNullOrEmpty(displayTitle) ? "Untitled" : displayTitle;
        artist.Text = set.GetDisplayArtist(unicode);
    }

    public override void Update()
    {
        base.Update();

        if (coverColors != null && coverColors.Version != seenColorVersion)
            updateColors();

        if (!vertical)
            return;

        // Long names scroll within the card.
        text.Width = MathF.Max(0, DrawWidth - text.X - 20);
    }
}
