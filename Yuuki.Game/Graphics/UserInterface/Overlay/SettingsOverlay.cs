// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;
using Sakura.Framework.Platform.Dialogs;
using Sakura.Framework.Reactive;
using Sakura.Framework.Threading;
using Yuuki.Game.Audio;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Yuuki.Game.Graphics.UserInterface.Overlay;

public partial class SettingsOverlay : FocusedOverlayContainer
{
    private const float control_width = 360;
    private const float sidebar_x = 48;
    private const float sidebar_width = 260;

    /// <summary>
    /// Space around the panel: it takes everything right of the sidebar, to the window's edges.
    /// </summary>
    private const float panel_margin = 48;

    /// <summary>
    /// The search over the panel, and the gap between them.
    /// </summary>
    private const float search_height = 52;

    private const float search_gap = 14;

    /// <summary>
    /// How visible the settings stay while the playfield preview shows over them.
    /// </summary>
    private const float previewing_alpha = 0.4f;

    private YuukiColorPalette palette;

    private Container settingsContent = null!;
    private Container settingsBanner = null!;
    private Container panel = null!;
    private Container searchRow = null!;
    private SearchBox searchBox = null!;
    private FlowContainer sidebar = null!;
    private LeftBottomShearButton backButton = null!;
    private YuukiScrollableContainer scroll = null!;
    private PlayfieldPreview playfieldPreview = null!;

    private readonly List<Section> sections = new List<Section>();

    // The Controls section's keycap rows take turns waiting for a key; key presses go to the one waiting.
    private readonly KeycapCaptureGroup keyCapture = new KeycapCaptureGroup();

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    [Resolved(canBeNull: true)]
    private ITextureManager? textureManager { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private AppHost? host { get; set; }


    [Resolved(canBeNull: true)]
    private BeatmapImportManager? importManager { get; set; }

    private const string framework_url = "https://github.com/HelloYeew/sakura";
    private const string yuuki_url = "https://github.com/HelloYeew/yuuki";

    [BackgroundDependencyLoader]
    private void load(YuukiConfigManager configManager, FrameworkConfigManager frameworkConfigManager, [CanBeNull] IYuukiColorProvider? colorProvider)
    {
        RelativeSizeAxes = Axes.Both;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        palette = colorProvider.IsNotNull()
            ? colorProvider!.GetPalette(YuukiScreenPart.Play)
            : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

        FlowContainer content;

        AddRange(new Drawable[]
        {
            // Everything but the playfield preview, so the settings can step back while it shows.
            settingsContent = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = palette.Backdrop,
                        Alpha = 0.85f
                    },
                    settingsBanner = createBanner(),
                    sidebar = new FlowContainer
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Position = new Vector2(sidebar_x, 190),
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 10)
                    },
                    // The search spans the panel's width, over it.
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = panel_margin + search_height,
                        Padding = new MarginPadding
                        {
                            Left = sidebar_x + sidebar_width + panel_margin,
                            Right = panel_margin,
                            Top = panel_margin
                        },
                        Child = searchRow = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            Child = searchBox = new SearchBox
                            {
                                SettingsKeys = handleKey
                            }
                        }
                    },
                    // The panel fills whatever the window leaves beside the sidebar and under the search. Sized
                    // relatively, it follows every window resize (and any aspect ratio) by itself, with no resize
                    // handling needed.
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding
                        {
                            Left = sidebar_x + sidebar_width + panel_margin,
                            Right = panel_margin,
                            Top = panel_margin + search_height + search_gap,
                            Bottom = panel_margin
                        },
                        Child = panel = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = 14,
                            Alpha = 0,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Color = palette.Surface,
                                    Alpha = 0.96f
                                },
                                scroll = new YuukiScrollableContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = content = new FlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FlowDirection.Vertical,
                                        Spacing = new Vector2(0, 36),
                                        Padding = new MarginPadding { Top = 36, Bottom = 36, Left = 44, Right = 44 }
                                    }
                                }
                            }
                        }
                    },
                    backButton = new LeftBottomShearButton
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Position = new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y),
                        Alpha = 0,
                        Action = Hide,
                        BackgroundColor = palette.Surface
                    }
                }
            },
            // Over everything, so the highway shows through the settings while it's being placed.
            playfieldPreview = new PlayfieldPreview()
        });

        addSection(content, IconUsage.Monitor, "Graphics",
            settingRow("Renderer", "The graphics backend the game draws with.",
                dropdown(frameworkConfigManager.Get<RendererType>(FrameworkSetting.RendererType), RendererTypes.GetSuitableRenderers()),
                "graphics api", "compatibility", "directx"),
            settingRow("Window mode", "Windowed, borderless or full screen.",
                dropdown(frameworkConfigManager.Get<WindowMode>(FrameworkSetting.WindowMode), Enum.GetValues<WindowMode>()),
                "fullscreen", "display", "screen"),
            settingRow("Frame limiter", "How often frames are drawn. VSync avoids tearing; unlimited is the most responsive.",
                dropdown(frameworkConfigManager.Get<FrameSync>(FrameworkSetting.FrameLimiter), Enum.GetValues<FrameSync>()),
                "fps", "framerate", "refresh rate"),
            settingRow("Execution mode", "Whether drawing and updating run on their own threads.",
                dropdown(frameworkConfigManager.Get<ExecutionMode>(FrameworkSetting.ExecutionMode), Enum.GetValues<ExecutionMode>()),
                "threading", "multithreaded", "performance"),
            settingRow("Hardware video decoding", "Decode background videos on the GPU where it can.",
                toggle(frameworkConfigManager.Get<bool>(FrameworkSetting.HardwareAcceleration)),
                "acceleration", "decoder", "performance"),
            settingRow("Disable background video", "Show only the background image, without decoding the beatmap's video.",
                toggle(configManager.Get<bool>(YuukiSetting.DisableVideo)),
                "no-video", "performance"));

        addSection(content, IconUsage.VolumeUp, "Audio",
            settingRow("Master volume", "Everything, together.",
                percentSlider(frameworkConfigManager.Get<double>(FrameworkSetting.MasterVolume)),
                "sound", "loudness"),
            settingRow("Inactive volume", "How loud the game gets, of master volume, while you're in another window.",
                percentSlider(configManager.Get<double>(YuukiSetting.InactiveVolume)),
                "unfocused", "background", "alt-tab", "minimize"),
            settingRow("Music volume", "The songs.",
                percentSlider(frameworkConfigManager.Get<double>(FrameworkSetting.TrackVolume)),
                "track", "bgm", "song"),
            settingRow("Effects volume", "Hitsounds and interface sounds.",
                percentSlider(frameworkConfigManager.Get<double>(FrameworkSetting.SampleVolume)),
                "sfx", "samples"),
            settingRow("Hitsound volume", "The notes, holds and combo break during play, on top of effects volume.",
                percentSlider(configManager.Get<double>(YuukiSetting.HitsoundVolume)),
                "samples", "sfx", "keysound"),
            settingRow("Audio driver", "The audio backend sound plays through.",
                dropdown(frameworkConfigManager.Get<AudioBackend>(FrameworkSetting.AudioBackend), AudioBackends.GetSuitableBackends()),
                "device", "output", "speaker", "headphone"));

        // Everything the setup screen before a song offers, so it can be set up without picking one.
        addSection(content, IconUsage.SportsEsports, "Gameplay",
            settingRow("Scroll speed", "How long a note is on screen before it reaches the line. Lower is faster.",
                intSlider(configManager.Get<int>(YuukiSetting.ScrollSpeed), 300, 1200, 10, v => $"{v} ms"),
                "note speed", "hi-speed", "velocity"),
            settingRow("Follow song BPM", "Scroll speed becomes the time at 120 BPM, so faster songs scroll faster.",
                toggle(configManager.Get<bool>(YuukiSetting.ScrollSpeedFollowsBpm)),
                "tempo", "note speed"),
            settingRow("Audio offset", "Hitting late on average? Lower it. Early? Raise it. The results screen can work it out for you.",
                intSlider(configManager.Get<int>(YuukiSetting.AudioOffset), YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_AUDIO_OFFSET, 1,
                    v => v > 0 ? $"+{v} ms" : $"{v} ms"),
                "latency", "delay", "sync", "timing", "calibrate"),
            settingRow("Background dim", "How dark the background is during play.",
                intSlider(configManager.Get<int>(YuukiSetting.BackgroundDim), 0, 100, 5, v => $"{v}%"),
                "brightness", "level"),
            settingRow("Background blur", "How blurred the background is during play.",
                intSlider(configManager.Get<int>(YuukiSetting.BackgroundBlur), 0, 100, 5, v => $"{v}%")),
            settingRow("Disable video during play", "Show only the background image while playing. Menus and song select keep the video.",
                toggle(configManager.Get<bool>(YuukiSetting.DisableGameplayVideo)),
                "no-video", "performance"),
            settingRow("Playfield size", "How much of the window the note highway takes. It keeps its shape at any size.",
                percentSlider(configManager.Get<double>(YuukiSetting.PlayfieldSize), YuukiConfigManager.MIN_PLAYFIELD_SIZE, YuukiConfigManager.MAX_PLAYFIELD_SIZE),
                "scale", "scaling", "lanes"),
            settingRow("Horizontal position", "Where the playfield sits between the left and right of the window.",
                percentSlider(configManager.Get<double>(YuukiSetting.PlayfieldPositionX)),
                "playfield", "screen", "scaling"),
            settingRow("Vertical position", "Where a smaller playfield sits between the top and bottom of the window.",
                percentSlider(configManager.Get<double>(YuukiSetting.PlayfieldPositionY)),
                "playfield", "screen", "scaling"));

        // Any change to where the playfield goes flashes it up over the settings, so it can be placed
        // without starting a song.
        foreach (YuukiSetting setting in new[] { YuukiSetting.PlayfieldSize, YuukiSetting.PlayfieldPositionX, YuukiSetting.PlayfieldPositionY })
            BindValueChanged(configManager.Get<double>(setting), _ => showPlayfieldPreview());

        addControlsSection(content, configManager);

        addSection(content, IconUsage.Palette, "Interface",
            settingRow("Original-language titles", "Show titles, artists and sources in their original script instead of romanised.",
                toggle(configManager.Get<bool>(YuukiSetting.UseUnicodeInfo)),
                "unicode", "romaji", "metadata", "language"),
            settingRow("Yuuki theme on start", "Open the game on the Yuuki theme instead of a random song.",
                toggle(configManager.Get<bool>(YuukiSetting.UseYuukiTheme)),
                "intro", "welcome", "menu music", "startup"),
            settingRow("Song list layout", "A horizontal strip of covers, or a vertical list.",
                dropdown(configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation), Enum.GetValues<CarouselOrientation>()),
                "carousel", "song select", "coverflow", "strip", "rail"),
            settingRow("Blur song select background", "Soften the background, and frost the glass the song list sits on. Off is lighter on slower machines.",
                toggle(configManager.Get<bool>(YuukiSetting.BlurBackground)),
                "carousel"),
            settingRow("UI scale", "How big menus, buttons and text are. Gameplay keeps its own size.",
                multiplierSlider(configManager.Get<double>(YuukiSetting.UIScale), YuukiConfigManager.MIN_UI_SCALE, YuukiConfigManager.MAX_UI_SCALE),
                "scaling", "zoom", "interface size"),
            settingRow("Cursor size", "How big the cursor is.",
                multiplierSlider(configManager.Get<double>(YuukiSetting.CursorSize), 0.5, 2),
                "mouse", "pointer", "scale"),
            settingRow("Cursor rotation", "Turn the cursor to point along a drag while a button is held.",
                toggle(configManager.Get<bool>(YuukiSetting.CursorRotate)),
                "mouse", "pointer", "rotate"),
            settingRow("Clock", "A face, the time, the game's running time, or all three. Clicking the clock changes it too.",
                dropdown(configManager.Get<ClockDisplayMode>(YuukiSetting.ClockDisplayMode), Enum.GetValues<ClockDisplayMode>(), formatClockMode),
                "footer", "runtime", "controller"),
            settingRow("24-hour time", "Show the time as 15:30 instead of 3:30 PM.",
                toggle(configManager.Get<bool>(YuukiSetting.Prefer24HourTime)),
                "clock", "12-hour", "am", "pm"));

        // The framework's own low-level settings, everything not offered above. Kept apart, behind a
        // warning, so they're reachable for troubleshooting without inviting casual fiddling.
        addSection(content, IconUsage.BugReport, "Debug",
            warningBanner("If you don't know what one of these does, don't touch it.",
                "They're the framework's low-level settings, here for troubleshooting."),
            settingRow("Game folder", "Where your settings, scores and beatmaps are kept.",
                new PillButton(palette.Control, Color.White)
                {
                    Text = "Open yuuki. folder",
                    Action = () => storage?.PresentExternally()
                },
                "files", "directory", "storage", "logs", "data"),
            settingRow("Import beatmaps", "Adds osu!mania beatmaps (.osz) to your library. You can also drop them on the window.",
                importButton(),
                "osz", "songs", "maps", "add"),
            settingRow("Performance overlay", "The frame-time graph in the corner.",
                dropdown(frameworkConfigManager.Get<PerformanceOverlayState>(FrameworkSetting.ShowFpsGraph), Enum.GetValues<PerformanceOverlayState>()),
                "fps", "framerate", "counter", "frame time"),
            settingRow("Zero-copy video", "Hand decoded video frames straight to the GPU, skipping a copy.",
                toggle(frameworkConfigManager.Get<bool>(FrameworkSetting.VideoZeroCopy)),
                "performance"),
            settingRow("Audio buffer size", "Smaller cuts audio latency; larger guards against crackling.",
                dropdown(frameworkConfigManager.Get<int>(FrameworkSetting.AudioDeviceBufferFrames), new[] { 128, 256, 512, 1024, 2048 },
                    frames => $"{frames} frames"),
                "delay", "stutter", "glitch"),
            settingRow("Relative mouse mode", "Lock and hide the cursor, reading raw mouse movement.",
                toggle(frameworkConfigManager.Get<bool>(FrameworkSetting.RelativeMouseMode)),
                "raw input", "cursor"),
            settingRow("Cursor sensitivity", "How far the cursor moves in relative mouse mode.",
                multiplierSlider(frameworkConfigManager.Get<double>(FrameworkSetting.CursorSensitivity), 0.1, 4),
                "mouse", "speed", "velocity", "raw input"));

        searchBox.Text.BindValueChanged(e => applySearch(e.NewValue));

        content.Add(createAbout());
    }

    #region Layout

    private Container createBanner() => new Container
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Size = new Vector2(320, 100),
        Position = new Vector2(-200, 48),
        Alpha = 0,
        Children = new Drawable[]
        {
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                Shear = UserInterfaceConstant.Shear,
                CornerRadius = 5,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Surface
                }
            },
            new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "Settings",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 56, FontWeight.Bold),
                Color = palette.Text
            }
        }
    };

    private void addSection(FlowContainer content, IconUsage icon, string title, params Drawable[] rows)
    {
        var section = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 22),
            Children = new Drawable[]
            {
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        new IconSprite
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = icon,
                            IconSize = 30,
                            Filled = true,
                            Color = palette.Control
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = title,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 32, FontWeight.Bold),
                            Color = palette.Control
                        }
                    }
                }
            }
        };

        foreach (Drawable row in rows)
            section.Add(row);

        content.Add(section);

        var button = new SidebarButton(icon, title, palette)
        {
            Action = () => scroll.ScrollTo(new Vector2(0, section.Y))
        };

        sidebar.Add(button);
        sections.Add(new Section(section, button, title, rows.OfType<SettingRow>().ToArray()));
    }

    /// <summary>
    /// A section: its part of the panel, its sidebar button, and the rows a search looks through.
    /// </summary>
    private sealed record Section(Drawable Body, SidebarButton Button, string Title, IReadOnlyList<SettingRow> Rows);

    private void applySearch(string search)
    {
        string[] words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (Section section in sections)
        {
            string[] remaining = words.Where(word => !matches(section.Title, word)).ToArray();
            bool anyShown = false;

            foreach (SettingRow row in section.Rows)
            {
                bool shown = remaining.All(word => row.FilterTerms.Any(term => matches(term, word)));
                row.Alpha = shown ? 1 : 0;
                anyShown |= shown;
            }

            section.Body.Alpha = anyShown ? 1 : 0;
            section.Button.Available = anyShown;
        }

        // What's left starts from the top; the old position could be past the end of it.
        scroll.ScrollToStart(false);
    }

    private static bool matches(string term, string word) => term.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>The search's current text, as typed (tests).</summary>
    public string SearchText
    {
        get => searchBox.Text.Value;
        set => searchBox.Text.Value = value;
    }

    /// <summary>The titles of the settings the search leaves showing, in order (tests).</summary>
    public IEnumerable<string> ShownSettings => sections.Where(s => s.Body.Alpha > 0)
                                                        .SelectMany(s => s.Rows)
                                                        .Where(row => row.Alpha > 0)
                                                        .Select(row => row.Title);

    /// <summary>The names of the sections the search leaves showing (tests).</summary>
    public IEnumerable<string> ShownSections => sections.Where(s => s.Body.Alpha > 0).Select(s => s.Title);

    /// <summary>Whether the search box has the keyboard (tests).</summary>
    public bool SearchFocused => searchBox.HasFocus;

    /// <summary>
    /// Every key the game listens for in play. The lanes for each lane count and the two swipes are
    /// keycaps to rebind (click one, then press a key); pause, skip and quick retry are fixed, shown so
    /// they can be found. The setup screen before a song edits the same settings for that song's lanes.
    /// </summary>
    private void addControlsSection(FlowContainer content, YuukiConfigManager configManager)
    {
        Reactive<Key> swipeLeft = configManager.Get<Key>(YuukiSetting.SwipeLeftKey);
        Reactive<Key> swipeRight = configManager.Get<Key>(YuukiSetting.SwipeRightKey);

        var rows = new List<Drawable>();

        for (int lanes = LaneKeyLayouts.MIN_LANES; lanes <= LaneKeyLayouts.MAX_LANES; lanes++)
        {
            int count = lanes;
            Reactive<string> setting = configManager.Get<string>(YuukiConfigManager.LaneKeysSetting(count));
            Key[] defaults = LaneKeyLayouts.Default(count);

            var strip = new KeycapStrip(palette, LaneKeyLayouts.Parse(setting.Value, count), defaults, keyCapture)
            {
                // A lane on a swipe key would never fire (swipes are checked first).
                IsReserved = key => isReservedInPlay(key) || key == swipeLeft.Value || key == swipeRight.Value
            };

            strip.KeysChanged += keys => setting.Value = LaneKeyLayouts.Format(keys);
            BindValueChanged(setting, e => strip.SetKeys(LaneKeyLayouts.Parse(e.NewValue, count)));

            rows.Add(settingRow($"{count} lanes", count == LaneKeyLayouts.MIN_LANES
                    ? "Left to right. Click a key, then press the one you want; a key already in the row swaps over."
                    : $"Left to right, for {count}-lane songs.",
                keycaps(strip),
                "keys", "keybinds", "bindings", $"{count}k"));
        }

        var swipes = new KeycapStrip(palette, new[] { swipeLeft.Value, swipeRight.Value },
            new[] { swipeLeft.Default, swipeRight.Default }, keyCapture)
        {
            Format = (side, key) => side == 0 ? $"{KeyLabels.For(key)}  «" : $"»  {KeyLabels.For(key)}",
            // A swipe on a lane's key would take that lane's presses in every song with that many lanes.
            IsReserved = key => isReservedInPlay(key) || usedByAnyLane(configManager, key)
        };

        swipes.KeysChanged += keys =>
        {
            swipeLeft.Value = keys[0];
            swipeRight.Value = keys[1];
        };

        BindValueChanged(swipeLeft, _ => swipes.SetKeys(new[] { swipeLeft.Value, swipeRight.Value }));
        BindValueChanged(swipeRight, _ => swipes.SetKeys(new[] { swipeLeft.Value, swipeRight.Value }));

        rows.Add(settingRow("Swipe", "Left swipe, then right swipe. A swipe that goes either way takes either.", keycaps(swipes),
            "keys", "keybinds", "bindings", "flick"));

        rows.Add(settingRow("Pause", "Pauses the song, with a count-in to resume.", fixedKeys("Esc"),
            "keys", "keybinds", "escape"));
        rows.Add(settingRow("Skip intro", "Jumps ahead when the first note is a while away. Works even where it's a lane key.", fixedKeys("Space"),
            "keys", "keybinds", "space"));
        rows.Add(settingRow("Quick retry", "Hold to start the song over, in play, paused or failed.", fixedKeys("Hold  `"),
            "keys", "keybinds", "restart"));

        addSection(content, IconUsage.Keyboard, "Controls", rows.ToArray());
    }

    /// <summary>Keys no lane or swipe may take: Enter (it starts a song from the setup screen) and the quick retry key.</summary>
    private static bool isReservedInPlay(Key key) => key is Key.Enter or Key.Grave;

    private static bool usedByAnyLane(YuukiConfigManager configManager, Key key)
    {
        for (int lanes = LaneKeyLayouts.MIN_LANES; lanes <= LaneKeyLayouts.MAX_LANES; lanes++)
        {
            if (LaneKeyLayouts.Parse(configManager.Get<string>(YuukiConfigManager.LaneKeysSetting(lanes)).Value, lanes).Contains(key))
                return true;
        }

        return false;
    }

    /// <summary>A keycap row with a revert button while it's off its default.</summary>
    private static SettingControl keycaps(KeycapStrip strip) =>
        new SettingControl(strip, new RevertToDefaultButton(() => strip.IsDefault, strip.ResetToDefault), keycap_height);

    private SettingControl fixedKeys(params string[] labels)
    {
        var strip = new KeycapStrip(palette, labels.Select(_ => Key.Unknown).ToArray()) { Editable = false };
        strip.Format = (index, _) => labels[index];
        return new SettingControl(strip, HeaderHeight: keycap_height);
    }

    /// <summary>A keycap's height, for centring a row's revert button on it.</summary>
    private const float keycap_height = 37;

    /// <summary>
    /// A setting: title and explanation on the left, its control on the right — with, for a setting
    /// (not an action), a revert button left of the control while it's off its default. A search finds
    /// it by its title, explanation, <paramref name="keywords"/> and, for a dropdown, its choices.
    /// </summary>
    private SettingRow settingRow(string title, string description, SettingControl setting, params string[] keywords)
    {
        Drawable control = setting.Control;
        control.Anchor = Anchor.TopRight;
        control.Origin = Anchor.TopRight;

        var row = new SettingRow(title, new[] { title, description }.Concat(keywords).Concat(setting.Choices ?? Array.Empty<string>()).ToArray())
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Width = 0.55f,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = title,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold),
                            Color = palette.Text
                        },
                        new SpriteText
                        {
                            Text = description,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Regular),
                            Color = palette.TextMuted
                        }
                    }
                },
                control
            }
        };

        if (setting.Revert is RevertToDefaultButton revert)
        {
            revert.Anchor = Anchor.TopRight;
            revert.Origin = Anchor.CentreRight;
            revert.Y = setting.HeaderHeight / 2;
            revert.LeftOf = control;
            row.Add(revert);
        }

        return row;
    }

    /// <summary>
    /// What a settings row shows on its right: the control and, for a setting, the button that reverts
    /// it, centred on the control's top <see cref="HeaderHeight"/> (a dropdown grows when it opens).
    /// </summary>
    private readonly record struct SettingControl(Drawable Control, RevertToDefaultButton? Revert = null, float HeaderHeight = 0, IReadOnlyList<string>? Choices = null)
    {
        public static implicit operator SettingControl(Drawable control) => new SettingControl(control);
    }

    /// <summary>A row of <see cref="settingRow"/>, and what a search finds it by.</summary>
    private partial class SettingRow : Container
    {
        public readonly string Title;

        /// <summary>Its title, explanation, keywords and any dropdown choices.</summary>
        public readonly IReadOnlyList<string> FilterTerms;

        public SettingRow(string title, IReadOnlyList<string> filterTerms)
        {
            Title = title;
            FilterTerms = filterTerms;
        }
    }

    // The overlay lives as long as the game, so binding its controls both ways to the long-lived
    // settings can't leak a screen (see the binding notes on FinalSetupScreen for short-lived ones).

    /// <summary>
    /// Picks .osz files and imports them in the background; progress and the result show as
    /// notifications. Dropping .osz files on the window does the same.
    /// </summary>
    private Drawable importButton() => new PillButton(palette.Control, Color.White)
    {
        Text = "Import .osz files",
        Action = () => host?.ShowOpenFileDialog(new FileDialogOptions
        {
            Title = "Choose .osz files to import",
            AllowMultiple = true,
            Filters = new[]
            {
                new FileDialogFilter("osu! beatmaps", "osz")
            }
        }, result =>
        {
            if (result.Successful)
                importManager?.Import(result.Paths.ToList());
        })
    };

    private SettingControl dropdown<T>(Reactive<T> setting, IEnumerable<T> items, Func<T, string>? format = null)
    {
        var dropdown = new YuukiDropdown<T>
        {
            Width = control_width,
            Items = items
        };

        if (format != null)
            dropdown.Formatter = format;

        dropdown.Current.BindTo(setting);
        setting.BindTo(dropdown.Current);
        return new SettingControl(dropdown, RevertToDefaultButton.For(setting), dropdown.ItemHeight, dropdown.Items.Select(dropdown.Formatter).ToArray());
    }

    private static string formatClockMode(ClockDisplayMode mode) => mode switch
    {
        ClockDisplayMode.DigitalWithRuntime => "Digital with running time",
        _ => mode.ToString()
    };

    private SettingControl toggle(Reactive<bool> setting)
    {
        var checkbox = new YuukiCheckbox();
        checkbox.Current.BindTo(setting);
        setting.BindTo(checkbox.Current);
        return new SettingControl(checkbox, RevertToDefaultButton.For(setting), checkbox.Height);
    }

    /// <summary>A slider for a fraction (a volume, the playfield's size or position), shown as "50%".</summary>
    private SettingControl percentSlider(Reactive<double> setting, double min = 0, double max = 1)
    {
        SpriteText value;

        var slider = new YuukiSliderBar<double>(palette.Control, palette.Text.WithAlpha(0.25f))
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.None,
            Width = control_width - 76,
            MinValue = min,
            MaxValue = max,
            KeyboardStep = 0.05,
            DefaultValue = setting.Default
        };

        var control = new Container
        {
            Size = new Vector2(control_width, 30),
            Children = new Drawable[]
            {
                slider,
                value = new SpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
                    Color = palette.Text
                }
            }
        };

        slider.Current.BindTo(setting);
        setting.BindTo(slider.Current);
        slider.Current.BindValueChanged(e => value.Text = $"{e.NewValue * 100:0}%", true);

        return new SettingControl(control, RevertToDefaultButton.For(setting), control.Height);
    }

    /// <summary>A multiplier slider (e.g. cursor sensitivity, cursor size), shown as "1.00×".</summary>
    private SettingControl multiplierSlider(Reactive<double> setting, double min, double max)
    {
        SpriteText value;

        var slider = new YuukiSliderBar<double>(palette.Control, palette.Text.WithAlpha(0.25f))
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.None,
            Width = control_width - 76,
            MinValue = min,
            MaxValue = max,
            KeyboardStep = 0.05,
            DefaultValue = 1
        };

        var control = new Container
        {
            Size = new Vector2(control_width, 30),
            Children = new Drawable[]
            {
                slider,
                value = new SpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
                    Color = palette.Text
                }
            }
        };

        slider.Current.BindTo(setting);
        setting.BindTo(slider.Current);
        slider.Current.BindValueChanged(e => value.Text = $"{e.NewValue:0.00}×", true);

        return new SettingControl(control, RevertToDefaultButton.For(setting), control.Height);
    }

    /// <summary>A slider for a whole-number setting (scroll speed, offset, dim), its value shown by <paramref name="format"/>.</summary>
    private SettingControl intSlider(Reactive<int> setting, int min, int max, int keyboardStep, Func<int, string> format)
    {
        SpriteText value;

        var slider = new YuukiSliderBar<int>(palette.Control, palette.Text.WithAlpha(0.25f))
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.None,
            Width = control_width - 96,
            MinValue = min,
            MaxValue = max,
            KeyboardStep = keyboardStep,
            DefaultValue = setting.Default
        };

        var control = new Container
        {
            Size = new Vector2(control_width, 30),
            Children = new Drawable[]
            {
                slider,
                value = new SpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
                    Color = palette.Text
                }
            }
        };

        slider.Current.BindTo(setting);
        setting.BindTo(slider.Current);
        slider.Current.BindValueChanged(e => value.Text = format(e.NewValue), true);

        return new SettingControl(control, RevertToDefaultButton.For(setting), control.Height);
    }

    /// <summary>An amber notice at the top of a section.</summary>
    private Drawable warningBanner(string title, string detail) => new Container
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Masking = true,
        CornerRadius = 10,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = JudgementColors.CRITICAL
            },
            new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(14, 0),
                Padding = new MarginPadding { Top = 14, Bottom = 14, Left = 18, Right = 18 },
                Children = new Drawable[]
                {
                    new IconSprite
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = IconUsage.Warning,
                        IconSize = 32,
                        Filled = true,
                        Color = Color.White
                    },
                    new FlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = title,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
                                Color = Color.White
                            },
                            new SpriteText
                            {
                                Text = detail,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Regular),
                                Color = Color.White
                            }
                        }
                    }
                }
            }
        }
    };

    /// <summary>
    /// The bottom of the settings: the game's logo, version and build, and the framework it runs on.
    /// </summary>
    private Drawable createAbout()
    {
        string built = BuildInfo.BuildTime is DateTime time ? $"  ·  built {time:yyyy-MM-dd HH:mm}" : "";
        string commit = BuildInfo.Commit is string hash ? $"  ·  {hash}" : "";

        return new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 24),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Color = palette.Text,
                    Alpha = 0.15f
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        new FlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Horizontal,
                            Spacing = new Vector2(18, 0),
                            Children = new Drawable[]
                            {
                                new LogoLink(logo("logo.png", new Vector2(348, 140) * 0.42f, Anchor.Centre))
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Action = () => host?.OpenUrlExternally(yuuki_url)
                                },
                                new FlowContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FlowDirection.Vertical,
                                    Spacing = new Vector2(0, 2),
                                    Children = new Drawable[]
                                    {
                                        aboutLine(versionText(BuildInfo.GameVersion), bold: false),
                                        aboutLine($"{BuildInfo.Configuration} build{built}{commit}", bold: false)
                                    }
                                }
                            }
                        },
                        new FlowContainer
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Vertical,
                            Spacing = new Vector2(0, 4),
                            Children = new Drawable[]
                            {
                                aboutLine("Powered by", bold: true, Anchor.TopRight),
                                new LogoLink(logo("framework-logo.png", new Vector2(566, 98) * 0.36f, Anchor.Centre))
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Action = () => host?.OpenUrlExternally(framework_url)
                                },
                                aboutLine(versionText(BuildInfo.FrameworkVersion), bold: false, Anchor.TopRight)
                            }
                        }
                    }
                }
            }
        };
    }

    private static string versionText(string version) => version == "development" ? "Development build" : $"Version {version}";

    private Drawable logo(string texture, Vector2 size, Anchor anchor = Anchor.CentreLeft) => new Sprite
    {
        Anchor = anchor,
        Origin = anchor,
        Size = size,
        Texture = textureManager?.Get(texture)
    };

    private SpriteText aboutLine(string text, bool bold, Anchor anchor = Anchor.TopLeft) => new SpriteText
    {
        Anchor = anchor,
        Origin = anchor,
        Text = text,
        Font = YuukiFontUsage.GetFontUsage(bold ? Typeface.Quicksand : Typeface.Nunito, bold ? 18 : 15, bold ? FontWeight.Bold : FontWeight.Regular),
        Color = bold ? palette.Text : palette.TextMuted
    };

    #endregion

    public override void Update()
    {
        base.Update();

        // The sidebar follows the scroll: the section whose top has scrolled past (or nearly) is lit. One
        // a search has hidden isn't laid out, so its position is stale.
        float position = scroll.CurrentScroll.Y + 60;
        int active = -1;

        for (int i = 0; i < sections.Count; i++)
        {
            if (sections[i].Body.Alpha > 0 && (active < 0 || sections[i].Body.Y <= position))
                active = i;
        }

        for (int i = 0; i < sections.Count; i++)
            sections[i].Button.Selected = i == active;
    }

    private void showPlayfieldPreview()
    {
        // In quickly, then held and let go of: a drag keeps it showing, and it fades after the last
        // change. The settings dim to let it through, and come back as it goes.
        playfieldPreview.ClearTransforms();
        playfieldPreview.FadeIn(150, Easing.OutQuint).Then().FadeOut(1500, Easing.InQuint);

        settingsContent.ClearTransforms();
        settingsContent.FadeTo(previewing_alpha, 150, Easing.OutQuint).Then().FadeIn(1500, Easing.InQuint);
    }

    protected override void PopIn()
    {
        uiSounds?.Play(UISound.OverlayOpen);
        this.FadeIn(400, Easing.OutQuint);
        settingsBanner.MoveToX(-40, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        backButton.MoveTo(LeftBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);

        foreach (Drawable column in new Drawable[] { searchRow, panel })
        {
            column.ClearTransforms();
            column.X = 120;
            column.MoveToX(0, 600, UserInterfaceConstant.SoftBounce).FadeIn(400, Easing.OutQuint);
        }

        for (int i = 0; i < sections.Count; i++)
            sections[i].Button.SlideIn(i * 60);

        searchBox.HoldFocus = true;
        Scheduler.Add(() =>
        {
            if (State != Visibility.Visible)
                return;

            GetContainingFocusManager()?.ChangeFocus(searchBox);
            searchBox.SelectAll();
        });
    }

    protected override void PopOut()
    {
        keyCapture.CancelCapture();

        // Let go of the keyboard, or the hidden search would go on taking every key.
        searchBox.HoldFocus = false;
        if (searchBox.HasFocus)
            GetContainingFocusManager()?.ChangeFocus(null);

        uiSounds?.Play(UISound.OverlayClose);
        this.FadeOut(400, Easing.OutQuint);
        settingsBanner.MoveToX(-200, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        backButton.MoveTo(new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y), 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        searchRow.MoveToX(120, 400, Easing.InCubic).FadeOut(300, Easing.OutQuint);
        panel.MoveToX(120, 400, Easing.InCubic).FadeOut(300, Easing.OutQuint);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (handleKey(e))
            return true;

        if (!searchBox.HasFocus && isSearchKey(e))
        {
            GetContainingFocusManager()?.ChangeFocus(searchBox);
            return true;
        }

        return base.OnKeyDown(e);
    }

    /// <summary>
    /// The keys the settings answer wherever the keyboard is (the search passes them on): a keycap
    /// waiting for a key takes it, and Escape or Ctrl+O close the settings.
    /// </summary>
    private bool handleKey(KeyEvent e)
    {
        // A keycap waiting for a key takes it (Escape there cancels the rebind, not the settings).
        if (keyCapture.HandleKey(e))
            return true;

        if (e.Key == Key.Escape || e.Key == Key.O && e.ControlPressed)
        {
            Hide();
            return true;
        }

        return false;
    }

    private static bool isSearchKey(KeyEvent e)
    {
        if ((e.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0)
            return false;

        return e.Key is >= Key.A and <= Key.Z or >= Key.Number0 and <= Key.Number9;
    }

    private partial class SearchBox : YuukiTextBox
    {
        private const float text_left = 18;

        /// <summary>The settings' own keys (<see cref="handleKey"/>), offered each key before the box uses it.</summary>
        public Func<KeyEvent, bool>? SettingsKeys { get; init; }

        /// <summary>
        /// While set, the box gets the keyboard back when whatever took it lets go
        /// Set while the settings are open.
        /// </summary>
        public bool HoldFocus { get; set; }

        public override bool RequestsFocus => HoldFocus;

        public SearchBox()
        {
            RelativeSizeAxes = Axes.Both;
            Size = Vector2.One;
            CornerRadius = 12;
            PlaceholderText = "type to search";
            ReleaseFocusOnCommit = false;

            SpriteText.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold);
            PlaceholderSprite.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Regular);
            PlaceholderSprite.Color = Color.White.WithAlpha(0.6f);
            ImeText.Font = SpriteText.Font;

            // Text stops short of the magnifier on the right.
            var margin = new MarginPadding { Left = text_left, Right = text_left + 26 + 12 };
            SpriteText.Margin = margin;
            PlaceholderSprite.Margin = margin;
            ImeText.Margin = margin;

            AddInternal(new IconSprite
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -text_left,
                Icon = IconUsage.Search,
                IconSize = 26,
                Color = Color.White
            });
        }

        public override bool OnKeyDown(KeyEvent e)
        {
            if (!HasFocus)
                return base.OnKeyDown(e);

            // Mid-composition, Enter and Escape are the IME's.
            if (!IsComposing)
            {
                if (e.Key == Key.Escape && Text.Value.Length > 0)
                {
                    Text.Value = string.Empty;
                    return true;
                }

                if (SettingsKeys?.Invoke(e) == true)
                    return true;
            }

            // Tab moves on through the settings.
            if (e.Key == Key.Tab)
                return base.OnKeyDown(e);

            base.OnKeyDown(e);

            // Kept even when the box has no use for it: a key goes on to the screens behind the settings
            // before it reaches the settings, so a letter typed here could start a song.
            return true;
        }
    }

    /// <summary>
    /// Where the playfield will be during play: its highway's outline, drawn through the same
    /// <see cref="PlayfieldAdjustmentContainer"/> (which follows the settings itself)
    /// Hidden until <see cref="showPlayfieldPreview"/>.
    /// </summary>
    private partial class PlayfieldPreview : PlayfieldAdjustmentContainer
    {
        // It's drawn over the settings, so the sliders under it must still get the drag.
        public override bool HandlePositionalInput => false;

        public PlayfieldPreview()
        {
            Alpha = 0;

            var perspective = new LanePerspective();
            perspective.LayOut(BASE_SIZE.X, BASE_SIZE.Y);

            var highway = new LaneBackground
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.White,
                Alpha = 0.5f
            };

            // The same trapezoid the playfield's backdrop fills, at every lane count.
            highway.SetCorners(
                perspective.Project(1, perspective.EdgeOffset(0)),
                perspective.Project(1, perspective.EdgeOffset(perspective.LaneCount)),
                perspective.Project(0, perspective.EdgeOffset(perspective.LaneCount)),
                perspective.Project(0, perspective.EdgeOffset(0)));

            Child = highway;
        }
    }

    /// <summary>A logo that opens a link: it grows a little under the cursor, and clicks like a button.</summary>
    private partial class LogoLink : ClickableContainer
    {
        private readonly Drawable logo;

        public LogoLink(Drawable logo)
        {
            this.logo = logo;
            Size = logo.Size;

            Children = new Drawable[]
            {
                logo,
                new HoverClickSounds()
            };
        }

        public override bool OnHover(MouseEvent e)
        {
            // Cleared first: the framework retargets a running scale in place instead of starting over.
            logo.ClearTransforms();
            logo.ScaleTo(1.06f, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            logo.ClearTransforms();
            logo.ScaleTo(1f, 300, Easing.OutQuint);
            return base.OnHoverLost(e);
        }
    }

    /// <summary>A section in the sidebar: dark and white when it's the one on screen.</summary>
    private partial class SidebarButton : ClickableContainer
    {
        private readonly YuukiColorPalette palette;
        private readonly Container body;
        private readonly Box background;
        private readonly IconSprite iconSprite;
        private readonly SpriteText label;

        private bool selected;
        private bool hovered;
        private bool available = true;

        public bool Selected
        {
            set
            {
                if (selected == value)
                    return;

                selected = value;
                updateState();
            }
        }

        /// <summary>Whether a search leaves anything in its section. A button whose section is hidden dims and can't be clicked.</summary>
        public bool Available
        {
            set
            {
                if (available == value)
                    return;

                available = value;
                Enabled.Value = value;
                this.FadeTo(value ? 1 : 0.35f, 200, Easing.OutQuint);
            }
        }

        public SidebarButton(IconUsage icon, string text, YuukiColorPalette palette)
        {
            this.palette = palette;
            Size = new Vector2(sidebar_width, 52);

            Child = body = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        Shear = UserInterfaceConstant.Shear,
                        CornerRadius = 6,
                        Child = background = new Box
                        {
                            RelativeSizeAxes = Axes.Both
                        }
                    },
                    new FlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 34,
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(10, 0),
                        Children = new Drawable[]
                        {
                            iconSprite = new IconSprite
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = icon,
                                IconSize = 24,
                                Filled = true
                            },
                            label = new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = text,
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold)
                            }
                        }
                    }
                }
            };

            updateState();

            Add(new HoverClickSounds(UISound.TabSelect) { Enabled = () => available });
        }

        public void SlideIn(double delay)
        {
            body.ClearTransforms();
            body.Alpha = 0;
            body.X = -40;
            Scheduler.AddDelayed(() =>
            {
                body.MoveToX(0, 500, UserInterfaceConstant.SoftBounce);
                body.FadeIn(250, Easing.OutQuint);
            }, delay);
        }

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
            Color fill = selected ? palette.Control : palette.Surface.Lighten(hovered ? 0.3f : 0.15f);
            Color foreground = selected ? Color.White : palette.Text;

            background.FadeToColor(fill, 150, Easing.OutQuint);
            background.FadeTo(selected || hovered ? 1f : 0.85f, 150, Easing.OutQuint);
            iconSprite.FadeToColor(foreground, 150, Easing.OutQuint);
            label.FadeToColor(foreground, 150, Easing.OutQuint);
        }
    }
}
