// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Overlay;

public partial class MusicPlayerOverlay : OverlayContainer
{
    private const float panel_width = 380;
    private const float header_height = 150;
    private const float controls_height = 56;
    private const float list_height = 320;
    private const float footer_margin = 116;

    /// <summary>
    /// How much bigger the panel is on a phone, so its text and buttons come out big enough; its list
    /// gets shorter to fit.
    /// </summary>
    private const float phone_scale = 1.2f;

    /// <summary>
    /// The least room kept above the panel, and the shortest its list gets.
    /// </summary>
    private const float top_margin = 24;

    private const float min_list_height = 120;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmapsCollection { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    private YuukiColorPalette palette;

    private Container panel = null!;
    private DelayedLoadCover cover = null!;
    private ScrollingText titleText = null!;
    private ScrollingText artistText = null!;
    private MusicProgressBar progressBar = null!;
    private IconSprite playPauseIcon = null!;
    private YuukiScrollableContainer listScroll = null!;
    private Container list = null!;

    // The list's height as fitted to the canvas (list_height when there's room).
    private float listHeight = list_height;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    // The controls that switch the song, locked (dimmed, ignoring clicks) on screens tied to one song.
    private ControlButton[] trackButtons = Array.Empty<ControlButton>();
    private FlowContainer listFlow = null!;

    private readonly List<SongRow> rows = new List<SongRow>();

    private Reactive<bool> useUnicode = new Reactive<bool>(false);

    private bool listExpanded = true;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] YuukiConfigManager? configManager)
    {
        palette = colorProvider.IsNotNull()
            ? colorProvider!.GetPalette(YuukiScreenPart.Play)
            : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

        RelativeSizeAxes = Axes.Both;

        Add(new ClickCatcher(Hide)
        {
            RelativeSizeAxes = Axes.Both
        });

        Add(panel = new Container
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Position = new Vector2(-20, -footer_margin),
            Size = new Vector2(panel_width, header_height + controls_height + list_height),
            Masking = true,
            CornerRadius = 12,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = Color.Black.WithAlpha(0.4f),
                Radius = 16
            },
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Surface.Darken(0.4f)
                },
                buildHeader(),
                buildControls(),
                buildList()
            }
        });

        progressBar.Seeked += fraction => audioManager?.SeekToFraction(fraction);

        if (configManager.IsNotNull())
        {
            useUnicode = configManager.Get<bool>(YuukiSetting.UseUnicodeInfo);
        }

        useUnicode.ValueChanged += _ => onUseUnicodeChanged();

        if (audioManager != null)
            BindValueChanged(audioManager.AllowTrackChange, e => setTrackChangeAllowed(e.NewValue), true);
    }

    private Drawable buildHeader() => new Container
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        RelativeSizeAxes = Axes.X,
        Height = header_height,
        Masking = true,
        CornerRadius = 12,
        Children = new Drawable[]
        {
            cover = new DelayedLoadCover
            {
                RelativeSizeAxes = Axes.Both,
                // Header is only panel_width x header_height, so cap the decoded background well below its
                // native (up to 4K) size — same reasoning as the carousel cards.
                DecodeTargetSize = new Vector2(512, 200),
                PlaceholderColor = palette.Surface.Darken(0.2f)
            },
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.Black.WithAlpha(0.45f)
            },
            new FlowContainer
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Padding = new MarginPadding { Left = 16, Right = 16, Bottom = 12 },
                Children = new Drawable[]
                {
                    titleText = new ScrollingText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        RelativeSizeAxes = Axes.X,
                        Size = new Vector2(1f, 30),
                        Text = "-",
                        ScrollSpeed = 45f,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                        Color = Color.White
                    },
                    artistText = new ScrollingText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        RelativeSizeAxes = Axes.X,
                        Size = new Vector2(1f, 20),
                        Text = "-",
                        ScrollSpeed = 45f,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
                        Color = Color.White.WithAlpha(0.85f)
                    }
                }
            },
            // Seekable progress bar pinned to the very bottom of the header.
            progressBar = new MusicProgressBar(palette.Control)
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft
            }
        }
    };

    private FlowContainer buildControls() => new FlowContainer
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
        Position = new Vector2(0, header_height),
        RelativeSizeAxes = Axes.X,
        Height = controls_height,
        Direction = FlowDirection.Horizontal,
        Spacing = new Vector2(10, 0),
        Padding = new MarginPadding
        {
            Left = panel_width / 2 - 118
        },
        Children = new Drawable[]
        {
            trackButton(IconUsage.Shuffle, 20, () => audioManager?.PlayRandom()),
            trackButton(IconUsage.SkipPrevious, 24, () => audioManager?.PlayPrevious()),
            playControl(),
            trackButton(IconUsage.SkipNext, 24, () => audioManager?.PlayNext()),
            new ControlButton(IconUsage.List, 22, toggleList, palette.Text)
        }
    };

    private ControlButton trackButton(IconUsage icon, float iconSize, Action onClick)
    {
        var button = new ControlButton(icon, iconSize, onClick, palette.Text);
        trackButtons = trackButtons.Append(button).ToArray();
        return button;
    }

    private ControlButton playControl()
    {
        var button = new ControlButton(IconUsage.PlayArrow, 26, () => audioManager?.TogglePlayback(), palette.Text, filled: true);
        playPauseIcon = button.IconDrawable;
        return button;
    }

    private Drawable buildList()
    {
        var container = list = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(0, header_height + controls_height),
            RelativeSizeAxes = Axes.X,
            Height = list_height,
            Child = listScroll = new YuukiScrollableContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = listFlow = new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Padding = new MarginPadding { Top = 6, Bottom = 6 }
                }
            }
        };

        return container;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        buildRows();

        if (workingBeatmap != null)
            workingBeatmap.ReactiveWorkingBeatmapSet.BindValueChanged(e => onSetChanged(e.NewValue), true);
    }

    private void buildRows()
    {
        rows.Clear();
        listFlow.Clear();

        if (beatmapsCollection == null)
            return;

        foreach (BeatmapSet set in beatmapsCollection.BeatmapSets.Where(s => !s.Hide))
        {
            var row = new SongRow(set, palette, () =>
            {
                // Through the audio manager, which refuses while the screen locks track changes.
                if (audioManager != null)
                    audioManager.PlaySet(set);
                else if (workingBeatmap != null)
                    workingBeatmap.BeatmapSet = set;
            }, useUnicode.Value);
            rows.Add(row);
            listFlow.Add(row);
        }
    }

    private void onSetChanged(BeatmapSet? set)
    {
        if (set == null)
            return;

        titleText.Text = BeatmapSetExtensions.GetDisplayTitle(set, useUnicode.Value) ?? "";
        artistText.Text = BeatmapSetExtensions.GetDisplayArtist(set, useUnicode.Value) ?? "";

        cover.SetSource(new BeatmapSetBackgroundSource(set, storage));

        foreach (SongRow row in rows)
            if (row.Set.ID == set.ID)
            {
                row.SetSelected(true);
                listScroll.ScrollIntoView(row);
            }
            else
            {
                row.SetSelected(false);
            }
    }

    /// <summary>
    /// Rows take their text when built, so they're rebuilt in the new script; the header follows, and
    /// the playing song's row is marked again.
    /// </summary>
    private void onUseUnicodeChanged()
    {
        buildRows();

        BeatmapSet? set = workingBeatmap?.BeatmapSet;
        if (set == null)
            return;

        titleText.Text = BeatmapSetExtensions.GetDisplayTitle(set, useUnicode.Value) ?? "";
        artistText.Text = BeatmapSetExtensions.GetDisplayArtist(set, useUnicode.Value) ?? "";

        foreach (SongRow row in rows)
            row.SetSelected(row.Set.ID == set.ID);
    }

    private void toggleList()
    {
        listExpanded = !listExpanded;

        panel.ResizeTo(new Vector2(panel_width, panelHeight), 300, Easing.OutQuint);
        listScroll.FadeTo(listExpanded ? 1 : 0, 200, Easing.OutQuint);
    }

    private float panelHeight => header_height + controls_height + (listExpanded ? listHeight : 0);

    public override void Update()
    {
        base.Update();

        fitToCanvas();

        if (audioManager == null)
            return;

        // Reflect play state on the button.
        playPauseIcon.Icon = audioManager.IsPlaying ? IconUsage.Pause : IconUsage.PlayArrow;

        // Feed the live playback position into the scrubber (ignored while the user is dragging it).
        double length = audioManager.TrackLength;
        progressBar.DisplayDurationMs = length;
        progressBar.SetProgressDisplay(length > 0 ? (float)(audioManager.CurrentTime / length) : 0);
    }

    /// <summary>
    /// Bigger on a phone; the list shortens to keep the panel under the top of the canvas.
    /// </summary>
    private void fitToCanvas()
    {
        float scale = gameLayout?.IsPhone == true ? phone_scale : 1;

        if (panel.Scale.X != scale)
            panel.Scale = new Vector2(scale);

        float fitted = Math.Clamp((DrawHeight - footer_margin - top_margin) / scale - header_height - controls_height, min_list_height, list_height);

        if (Math.Abs(fitted - listHeight) < 0.5f)
            return;

        // Resized rather than set, so a resize already running (the list opening) heads for the new height
        // and the open animation keeps going.
        listHeight = fitted;
        list.Height = fitted;
        panel.ResizeTo(new Vector2(panel_width, panelHeight), 300, Easing.OutQuint);
    }

    protected override void PopIn()
    {
        uiSounds?.Play(UISound.OverlayOpen);
        this.FadeIn(200, Easing.OutQuint);
        panel.Y = -footer_margin + 30;
        panel.MoveToY(-footer_margin, 400, Easing.OutQuint);
        panel.FadeIn(200, Easing.OutQuint);
    }

    protected override void PopOut()
    {
        uiSounds?.Play(UISound.OverlayClose);
        this.FadeOut(200, Easing.OutQuint);
        panel.MoveToY(-footer_margin + 30, 300, Easing.OutQuint);
    }

    /// <summary>
    /// Transparent full-screen layer that closes the popup when clicked outside the panel.
    /// </summary>
    private partial class ClickCatcher : Box
    {
        private readonly Action onClick;

        public ClickCatcher(Action onClick)
        {
            this.onClick = onClick;
            // Invisible but still present, so it catches clicks that fall outside the panel.
            Color = Color.Transparent;
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            onClick();
            return true;
        }
    }

    /// <summary>
    /// The player stays usable everywhere, but on screens tied to the current song (setup, gameplay,
    /// results) the song-switching controls and the list dim and stop responding.
    /// </summary>
    private void setTrackChangeAllowed(bool allowed)
    {
        foreach (ControlButton button in trackButtons)
            button.Enabled.Value = allowed;

        listScroll.FadeTo(allowed ? 1f : 0.45f, 200, Easing.OutQuint);
    }

    /// <summary>
    /// A round icon button used for the transport controls.
    /// </summary>
    private partial class ControlButton : Container
    {
        public IconSprite IconDrawable { get; }

        /// <summary>
        /// Whether the button responds; a disabled button is dimmed and ignores clicks.
        /// </summary>
        public readonly ReactiveBool Enabled = new ReactiveBool(true);

        private readonly Action onClick;
        private readonly Box background;
        private readonly float baseAlpha;

        public ControlButton(IconUsage icon, float iconSize, Action onClick, Color accent, bool filled = false)
        {
            this.onClick = onClick;
            baseAlpha = filled ? 0.25f : 0f;

            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            Size = new Vector2(filled ? 44 : 38);
            Masking = true;
            CornerRadius = (filled ? 44 : 38) / 2f;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = accent,
                    Alpha = baseAlpha
                },
                IconDrawable = new IconSprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = icon,
                    IconSize = iconSize,
                    Color = Color.White
                },
                new HoverClickSounds { Enabled = () => Enabled.Value }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Enabled.BindValueChanged(e => this.FadeTo(e.NewValue ? 1f : 0.35f, 200, Easing.OutQuint), true);
        }

        public override bool OnHover(MouseEvent e)
        {
            if (Enabled.Value)
                background.FadeTo(0.4f, 150, Easing.OutQuint);
            return true;
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            background.FadeTo(baseAlpha, 150, Easing.OutQuint);
            return true;
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            if (!Enabled.Value)
                return true;

            onClick();
            background.FlashColor(Color.White, 250, Easing.OutQuint);
            return true;
        }
    }

    /// <summary>
    /// A single selectable song entry in the list.
    /// </summary>
    private partial class SongRow : Container
    {
        public BeatmapSet Set { get; }

        private readonly Action onClick;
        private readonly YuukiColorPalette palette;
        private readonly Box hoverBox;
        private SpriteText title = null!;
        private SpriteText artist = null!;
        private bool useUnicode;

        private bool selected;

        public SongRow(BeatmapSet set, YuukiColorPalette palette, Action onClick, bool useUnicode)
        {
            Set = set;
            this.palette = palette;
            this.onClick = onClick;

            RelativeSizeAxes = Axes.X;
            Height = 42;

            Add(hoverBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.White,
                Alpha = 0
            });

            this.useUnicode = useUnicode;

            Add(new HoverClickSounds(UISound.Select) { HoverVolume = 0.6 });
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(new FlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Padding = new MarginPadding { Left = 16, Right = 16 },
                Children = new Drawable[]
                {
                    title = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = BeatmapSetExtensions.GetDisplayTitle(Set, useUnicode),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 16, FontWeight.Bold),
                        Color = Color.White
                    },
                    artist = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = BeatmapSetExtensions.GetDisplayArtist(Set, useUnicode),
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Regular),
                        Color = Color.White.WithAlpha(0.6f)
                    }
                }
            });
        }

        public void SetSelected(bool value)
        {
            selected = value;
            title.FadeToColor(value ? palette.Control : Color.White, 150, Easing.OutQuint);
            hoverBox.FadeTo(value ? 0.12f : 0f, 150, Easing.OutQuint);
        }

        public override bool OnHover(MouseEvent e)
        {
            if (!selected)
                hoverBox.FadeTo(0.08f, 120, Easing.OutQuint);
            return true;
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            if (!selected)
                hoverBox.FadeTo(0f, 120, Easing.OutQuint);
            return true;
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            onClick();
            return true;
        }
    }
}
