// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// What's playing, for the idle main menu
/// </summary>
public partial class NowPlayingPanel : Container
{
    private const float panel_width = 620;
    private const float cover_size = 132;

    /// <summary>
    /// Space above and below the cover.
    /// </summary>
    private const float inset_y = 22;

    /// <summary>
    /// Space beside the content. The card leans (gently sheared about its middle), so its corners sit
    /// up to a sixth of its height further in or out than its middle: these keep the content clear.
    /// </summary>
    private const float inset_left = 30;
    private const float inset_right = 42;

    private const float cover_gap = 24;
    private const float text_x = inset_left + cover_size + cover_gap;
    private const float text_width = panel_width - text_x - inset_right;

    /// <summary>
    /// Room for a detail line's icon, before its text.
    /// </summary>
    private const float icon_space = 28;

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    private readonly YuukiColorPalette palette;

    private DelayedLoadCover cover = null!;
    private ScrollingText title = null!;
    private ScrollingText artist = null!;
    private ScrollingText source = null!;
    private Container sourceLine = null!;
    private Box progressFill = null!;
    private SpriteText elapsed = null!;
    private SpriteText total = null!;

    private BeatmapSet? shownSet;

    public NowPlayingPanel(YuukiColorPalette palette)
    {
        this.palette = palette;
        Size = new Vector2(panel_width, cover_size + inset_y * 2);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            // Dark and see-through so it reads over any background; leaning about its middle, so the
            // slant is shared evenly between the top and the bottom.
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 14,
                Shear = UserInterfaceConstant.GentleShear,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = ColorExtensions.FromHex("#16161C"),
                    Alpha = 0.6f
                }
            },
            new Container
            {
                Position = new Vector2(inset_left, inset_y),
                Size = new Vector2(cover_size),
                Masking = true,
                CornerRadius = 12,
                Child = cover = new DelayedLoadCover
                {
                    RelativeSizeAxes = Axes.Both,
                    DecodeTargetSize = new Vector2(cover_size * 2),
                    PlaceholderColor = palette.AccentLight,
                    LoadInScale = 1.08f
                }
            },
            new Container
            {
                Position = new Vector2(text_x, inset_y),
                Size = new Vector2(text_width, cover_size),
                Children = new Drawable[]
                {
                    new Container
                    {
                        Y = 4,
                        RelativeSizeAxes = Axes.X,
                        Height = 38,
                        Child = title = line(30, FontWeight.Bold, Color.White)
                    },
                    iconLine(48, 26, IconUsage.Artist, 20, artist = line(20, FontWeight.Regular, Color.White.WithAlpha(0.85f))),
                    sourceLine = iconLine(76, 24, IconUsage.Share, 17, source = line(17, FontWeight.Regular, Color.White.WithAlpha(0.6f))),
                    new Container
                    {
                        Y = 106,
                        RelativeSizeAxes = Axes.X,
                        Height = 4,
                        Masking = true,
                        CornerRadius = 2,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.White.WithAlpha(0.2f)
                            },
                            progressFill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0,
                                Color = palette.Accent
                            }
                        }
                    },
                    elapsed = time(Anchor.TopLeft),
                    total = time(Anchor.TopRight)
                }
            }
        };
    }

    /// <summary>
    /// One line of the song's details, scrolling sideways when it's wider than the card.
    /// </summary>
    private static ScrollingText line(int size, FontWeight weight, Color color) => new ScrollingText
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        RelativeSizeAxes = Axes.X,
        ScrollSpeed = 45f,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, size, weight),
        Color = color
    };

    /// <summary>
    /// A detail line led by an icon saying what it is, the text scrolling in the room after it.
    /// </summary>
    private static Container iconLine(float y, float height, IconUsage icon, int iconSize, ScrollingText text) => new Container
    {
        Y = y,
        RelativeSizeAxes = Axes.X,
        Height = height,
        Children = new Drawable[]
        {
            new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Icon = icon,
                IconSize = iconSize,
                Color = text.Color
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = icon_space },
                Child = text
            }
        }
    };

    private static SpriteText time(Anchor side) => new SpriteText
    {
        Anchor = side,
        Origin = side,
        Y = 114,
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 14, FontWeight.Bold),
        Color = Color.White.WithAlpha(0.75f)
    };

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Drawable's BindValueChanged, not the reactive's own: the working beatmap outlives the menu.
        if (workingBeatmap != null)
            BindValueChanged(workingBeatmap.ReactiveWorkingBeatmapSet, e => showSet(e.NewValue), true);

        // Likewise for the setting, which is shared and lives as long as the config does.
        if (config != null)
            BindValueChanged(config.Get<bool>(YuukiSetting.UseUnicodeInfo), _ => updateText());
    }

    private void showSet(BeatmapSet? set)
    {
        if (set == null || ReferenceEquals(set, shownSet))
            return;

        shownSet = set;
        updateText();

        // The set's own cover art when it has one, otherwise the middle of its background.
        cover.SetSource(storage != null && (set.Cover != null || !string.IsNullOrEmpty(set.CoverStoragePath))
            ? new BeatmapSetCoverSource(set, storage)
            : new BeatmapSetBackgroundSource(set, storage));
    }

    /// <summary>
    /// Fills the title, artist and source of the shown song, in the script the setting picks.
    /// </summary>
    private void updateText()
    {
        if (shownSet == null)
            return;

        bool unicode = config?.Get<bool>(YuukiSetting.UseUnicodeInfo).Value ?? false;
        title.Text = shownSet.GetDisplayTitle(unicode);
        artist.Text = shownSet.GetDisplayArtist(unicode);

        // No source, no line (rather than an icon beside nothing).
        string from = shownSet.GetDisplaySource(unicode);
        source.Text = string.IsNullOrWhiteSpace(from) ? string.Empty : from;
        sourceLine.Alpha = string.IsNullOrWhiteSpace(from) ? 0 : 1;
    }

    public override void Update()
    {
        base.Update();

        double length = audioManager?.TrackLength ?? 0;
        double at = Math.Clamp(audioManager?.CurrentTime ?? 0, 0, Math.Max(length, 0));

        progressFill.Width = length > 0 ? (float)(at / length) : 0;
        elapsed.Text = format(at);
        total.Text = format(length);
    }

    private static string format(double ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    /// <summary>
    /// The title it shows (diagnostics/tests).
    /// </summary>
    public string ShownTitle => title.Text;

    /// <summary>
    /// The source it shows, empty when the song has none (diagnostics/tests).
    /// </summary>
    public string ShownSource => source.Text;
}
