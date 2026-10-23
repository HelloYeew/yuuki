// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Graphics.Screens;

public partial class FinalSetupScreen : YuukiScreen, IHaveScreenAction
{
    /// <summary>
    /// The narrowest the settings panel gets on a small canvas (a large UI scale).
    /// </summary>
    private const float settings_min_width = 600;

    /// <summary>
    /// The room kept on the left for the song panel, gap included.
    /// </summary>
    private const float song_panel_room = 804;

    /// <summary>
    /// Space between the settings panel and the window's top and right edges.
    /// </summary>
    private const float panel_margin = 48;

    /// <summary>
    /// Space below the settings panel: it stops 24 short of the Start button (66 tall, 16 up).
    /// </summary>
    private const float panel_bottom_margin = 106;

    /// <summary>
    /// How far right of its place the settings panel starts, off the screen's edge.
    /// </summary>
    private const float panel_slide = 448;

    /// <summary>
    /// Where the song panel sits, and its size at scale 1 showing the Detail tab (the Ranking tab fits
    /// itself to the buttons below).
    /// </summary>
    private static readonly Vector2 info_panel_position = new Vector2(-30, 48);

    private static readonly Vector2 info_panel_size = new Vector2(680, 498);

    /// <summary>
    /// Where a keycap row starts, right of its title, while the row has the room.
    /// </summary>
    private const float keycap_x = 160;

    /// <summary>
    /// The lanes this beatmap is played on, which picks the key layout shown and rebound here.
    /// </summary>
    private int laneCount => LaneKeyLayouts.Clamp(workingBeatmap?.Beatmap?.LaneCount ?? LaneKeyLayouts.MIN_LANES);

    // The lane and swipe keycaps, which take turns waiting for a key to rebind to.
    private readonly KeycapCaptureGroup keyCapture = new KeycapCaptureGroup();

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool AllowTrackChange => false;

    public override bool LoopPreview => true;

    // Dim the background like gameplay does, so the dim slider previews live behind the settings.
    public override bool DimBackground => true;

    [Resolved]
    private YuukiScreenStack screenStack { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

    [Resolved(canBeNull: true)]
    private OffsetManager? offsets { get; set; }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    // Set while a saved score opened from the Ranking tab is showing, so coming back from it doesn't
    // count as coming back from a play.
    private bool viewingScore;

    private YuukiColorPalette palette;

    private BeatmapInfoPanel infoPanel = null!;
    private Container settingsArea = null!;
    private Container settingsPanel = null!;
    private LeftBottomShearButton backButton = null!;
    private RightBottomShearButton startButton = null!;

    private readonly List<KeycapStrip> keycapRows = new List<KeycapStrip>();

    public FinalSetupScreen()
    {
        ScreenActions = new List<ScreenAction>
        {
            new ScreenAction
            {
                Name = "Back",
                Action = onBack,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape))
            },
            new ScreenAction
            {
                Name = "Start",
                Action = onStart,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Enter))
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = "Setup";
        ScreenHelperText.Value = "Tune your settings, then start!";

        palette = Palette;

        AddRange(new Drawable[]
        {
            infoPanel = createInfoPanel(),
            // The panel fills what the song panel leaves, as the settings overlay's fills what its sidebar
            // leaves, so it follows the window by itself; whatever doesn't fit scrolls.
            settingsArea = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding
                {
                    Left = song_panel_room,
                    Right = panel_margin,
                    Top = panel_margin,
                    Bottom = panel_bottom_margin
                },
                Child = settingsPanel = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    X = panel_slide,
                    Masking = true,
                    CornerRadius = 12,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Color = palette.Surface,
                            Alpha = 0.92f
                        },
                        new YuukiScrollableContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = new FlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FlowDirection.Vertical,
                                Spacing = new Vector2(0, 36),
                                Padding = new MarginPadding { Top = 18, Bottom = 22, Left = 28, Right = 28 },
                                Children = new Drawable[]
                                {
                                    createPlaySettings(),
                                    createControls()
                                }
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
                Action = onBack,
                BackgroundColor = palette.Surface
            },
            startButton = new RightBottomShearButton
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(200, RightBottomShearButton.DEFAULT_POSITION.Y),
                Alpha = 0,
                Text = "Start",
                Icon = IconUsage.PlayArrow,
                Action = onStart,
                BackgroundColor = palette.Surface
            }
        });
    }

    #region Beatmap information

    private BeatmapInfoPanel createInfoPanel()
    {
        // TODO: this should be shared
        var panel = new BeatmapInfoPanel("Solo Play", palette, showCover: true)
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = info_panel_position
        };

        if (workingBeatmap?.Beatmap != null)
            panel.ShowBeatmap(workingBeatmap.Beatmap);
        else
            panel.ShowSet(workingBeatmap?.BeatmapSet);

        panel.RankingBottom = () => backButton.DrawRectangle.Y;
        panel.RankingMaxRight = () => settingsPanel.DrawRectangle.X;

        panel.ScoreSelected += score =>
        {
            viewingScore = true;
            screenStack.Push(ScoreScreen.ForSavedScore(score, scoreStore));
        };

        return panel;
    }

    #endregion

    #region Settings

    private FlowContainer createPlaySettings() => new FlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 22),
        Children = new Drawable?[]
        {
            sectionTitle("Play settings"),
            settingRow("Scroll speed",
                "How long a note is on screen before it reaches the line. Lower is faster.",
                YuukiSetting.ScrollSpeed, 750, 300, 1200, 10, v => $"{v} ms"),
            toggleRow("Follow song BPM",
                "Scroll speed becomes the time at 120 BPM, so faster songs scroll faster.",
                YuukiSetting.ScrollSpeedFollowsBpm),
            settingRow("Audio offset",
                forDevice(offsets?.AudioDevice, OffsetManager.AudioDeviceLabel)
                + "Hitting late on average? Lower it. Early? Raise it. The results screen can work it out for you.",
                YuukiSetting.AudioOffset, 0, YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_AUDIO_OFFSET, 1,
                OffsetManager.Signed),
            settingRow("Visual offset",
                forDevice(offsets?.Display, OffsetManager.DisplayLabel)
                + "Notes meeting the line after the music? Raise it. Before? Lower it. Settings can calibrate both.",
                YuukiSetting.VisualOffset, 0, YuukiConfigManager.MIN_VISUAL_OFFSET, YuukiConfigManager.MAX_VISUAL_OFFSET, 1,
                OffsetManager.Signed),
            createSongOffsetRow(),
            // All three together, as they'll be played with.
            new OffsetStack(palette)
            {
                RelativeSizeAxes = Axes.X,
                Compact = true,
                BeatmapId = offsets != null ? workingBeatmap?.Beatmap?.ID : null,
                DifficultyName = workingBeatmap?.Beatmap?.DifficultyName
            },
            settingRow("Background dim",
                "How dark the background is during play — previewed behind this screen.",
                YuukiSetting.BackgroundDim, 50, 0, 100, 5, v => $"{v}%"),
            settingRow("Background blur",
                "How blurred the background is during play — previewed behind this screen.",
                YuukiSetting.BackgroundBlur, 0, 0, 100, 5, v => $"{v}%"),
            toggleRow("Disable video",
                "Show only the background image during play. Song select keeps the video.",
                YuukiSetting.DisableGameplayVideo)
        }.OfType<Drawable>().ToArray()
    };

    // Which device an offset is kept for, ahead of what it does.
    private static string forDevice(string? device, Func<string, string> label) => device != null ? $"For {label(device)}. " : string.Empty;

    /// <summary>
    /// This difficulty's song offset, and its last play to set it from as osu! does; null for none to set (a generated
    /// chart, or no offsets to keep it in).
    /// </summary>
    private Drawable? createSongOffsetRow()
    {
        if (offsets == null || workingBeatmap?.Beatmap is not Beatmap beatmap)
            return null;

        int id = beatmap.ID;
        var current = new Reactive<int>(0) { Value = offsets.GetSongOffset(id) };
        current.ValueChanged += e => offsets.SetSongOffset(id, e.NewValue);

        string keys = config != null
            ? $" In play, {KeyLabels.For(config.Get<Key>(YuukiSetting.SongOffsetDownKey).Value)} and {KeyLabels.For(config.Get<Key>(YuukiSetting.SongOffsetUpKey).Value)} nudge it."
            : string.Empty;

        string difficulty = string.IsNullOrEmpty(beatmap.DifficultyName) ? "this difficulty" : beatmap.DifficultyName;

        var row = (FlowContainer)settingRow($"Song offset · {difficulty}",
            $"Only {difficulty}, on top of your devices' offsets, for a song out of time with its music: moves its notes with their "
            + "timing. Hitting it late? Lower it." + keys,
            current, 0, OffsetManager.MIN_SONG_OFFSET, OffsetManager.MAX_SONG_OFFSET, 1, OffsetManager.Signed);

        // osu!'s "calibrate using last play": what the last play of it leaned, taken off.
        ScoreInfo? lastPlay = scoreStore?.Scores.LastOrDefault(s => s.BeatmapId == id && s.MeanError != null);

        if (lastPlay != null)
        {
            var history = new TimingHistory(offsets.AudioOffsetFor, offsets.GetSongOffset);
            int lean = (int)Math.Round(history.LeanNow(lastPlay) ?? 0);
            PillButton useLastPlay = null!;

            row.Add(useLastPlay = new PillButton(palette.Control, palette.TextLight)
            {
                Text = lean == 0 ? "Last play was on time" : $"Use last play ({Math.Abs(lean)} ms {(lean > 0 ? "late" : "early")})",
                Action = () =>
                {
                    current.Value -= lean;
                    useLastPlay.Text = "Taken from last play";
                    useLastPlay.Enabled.Value = false;
                }
            });

            useLastPlay.Enabled.Value = lean != 0;
        }

        return row;
    }

    private Drawable settingRow(string title, string description, YuukiSetting setting, int defaultValue,
                                int min, int max, int keyboardStep, Func<int, string> format)
        => settingRow(title, description, config?.Get<int>(setting), defaultValue, min, max, keyboardStep, format);

    /// <param name="current">What the slider sets, or null to show the default and set nothing.</param>
    private Drawable settingRow(string title, string description, Reactive<int>? current, int defaultValue,
                                int min, int max, int keyboardStep, Func<int, string> format)
    {
        SpriteText value;
        Container titleLine;

        var slider = new YuukiSliderBar<int>(palette.Control, palette.Text.WithAlpha(0.25f))
        {
            MinValue = min,
            MaxValue = max,
            KeyboardStep = keyboardStep,
            DefaultValue = defaultValue
        };

        var row = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new Drawable[]
            {
                titleLine = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 30,
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = title,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                            Color = palette.Text
                        },
                        value = new SpriteText
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                            Color = palette.Text
                        }
                    }
                },
                descriptionText(description),
                slider
            }
        };

        slider.Current.ValueChanged += e => value.Text = format(e.NewValue);
        value.Text = format(defaultValue);

        if (current != null)
        {
            slider.Current.BindTo(current);
            slider.Current.ValueChanged += e => current.Value = e.NewValue;

            titleLine.Add(revertButton(RevertToDefaultButton.For(current), value));
        }
        else
            slider.Current.Value = defaultValue;

        return row;
    }

    /// <summary>
    /// A revert button sitting left of <paramref name="beside"/>, a right-anchored control or readout.
    /// </summary>
    private static RevertToDefaultButton revertButton(RevertToDefaultButton button, Drawable beside)
    {
        button.Anchor = Anchor.CentreRight;
        button.Origin = Anchor.CentreRight;
        button.LeftOf = beside;
        return button;
    }

    private Drawable toggleRow(string title, string description, YuukiSetting setting)
    {
        Container titleLine;

        var toggle = new YuukiCheckbox
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight
        };

        var row = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new Drawable[]
            {
                titleLine = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 32,
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = title,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                            Color = palette.Text
                        },
                        toggle
                    }
                },
                descriptionText(description)
            }
        };

        if (config != null)
        {
            Reactive<bool> current = config.Get<bool>(setting);
            toggle.Current.BindTo(current);
            toggle.Current.ValueChanged += e => current.Value = e.NewValue;

            titleLine.Add(revertButton(RevertToDefaultButton.For(current), toggle));
        }

        return row;
    }

    private Drawable createControls()
    {
        var lanes = new KeycapStrip(palette, LaneKeyLayouts.Parse(config?.Get<string>(YuukiConfigManager.LaneKeysSetting(laneCount)).Value, laneCount),
            LaneKeyLayouts.Default(laneCount), keyCapture);

        var swipes = new KeycapStrip(palette, new[] { swipeKey(YuukiSetting.SwipeLeftKey), swipeKey(YuukiSetting.SwipeRightKey) },
            new[] { Playfield.DEFAULT_SWIPE_LEFT_KEY, Playfield.DEFAULT_SWIPE_RIGHT_KEY }, keyCapture)
        {
            Format = (side, key) => side == 0 ? $"{KeyLabels.For(key)}  «" : $"»  {KeyLabels.For(key)}"
        };

        // A lane on a swipe key would never fire (swipes are checked first); a swipe on a lane key would
        // take the lane's presses. Enter starts the song from here, ` is quick retry, and the song offset keys
        // nudge the song's offset.
        lanes.IsReserved = key => key is Key.Enter or Key.Grave || swipes.Keys.Contains(key) || isSongOffsetKey(key);
        swipes.IsReserved = key => key is Key.Enter or Key.Grave || lanes.Keys.Contains(key) || isSongOffsetKey(key);

        if (config != null)
        {
            Reactive<string> laneSetting = config.Get<string>(YuukiConfigManager.LaneKeysSetting(laneCount));
            Reactive<Key> swipeLeft = config.Get<Key>(YuukiSetting.SwipeLeftKey);
            Reactive<Key> swipeRight = config.Get<Key>(YuukiSetting.SwipeRightKey);

            lanes.KeysChanged += keys => laneSetting.Value = LaneKeyLayouts.Format(keys);
            swipes.KeysChanged += keys =>
            {
                swipeLeft.Value = keys[0];
                swipeRight.Value = keys[1];
            };

            BindValueChanged(laneSetting, e => lanes.SetKeys(LaneKeyLayouts.Parse(e.NewValue, laneCount)));
            BindValueChanged(swipeLeft, _ => swipes.SetKeys(new[] { swipeLeft.Value, swipeRight.Value }));
            BindValueChanged(swipeRight, _ => swipes.SetKeys(new[] { swipeLeft.Value, swipeRight.Value }));
        }

        return new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 14),
            Children = new Drawable[]
            {
                sectionTitle($"Controls  ·  {laneCount} lanes"),
                controlRow("Lanes", lanes),
                controlRow("Swipe", swipes),
                descriptionText("Click a key, then press the key you want for it. Every key is in Settings › Controls.")
            }
        };
    }

    private bool isSongOffsetKey(Key key) =>
        config != null && (key == config.Get<Key>(YuukiSetting.SongOffsetDownKey).Value || key == config.Get<Key>(YuukiSetting.SongOffsetUpKey).Value);

    private Key swipeKey(YuukiSetting setting) => config?.Get<Key>(setting).Value ?? (setting == YuukiSetting.SwipeLeftKey ? Playfield.DEFAULT_SWIPE_LEFT_KEY : Playfield.DEFAULT_SWIPE_RIGHT_KEY);

    private Drawable controlRow(string title, KeycapStrip caps)
    {
        caps.Anchor = Anchor.CentreLeft;
        caps.Origin = Anchor.CentreLeft;
        caps.X = keycap_x;
        keycapRows.Add(caps);

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 40,
            Children = new Drawable[]
            {
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = title,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 22, FontWeight.Bold),
                    Color = palette.Text
                },
                caps,
                new RevertToDefaultButton(() => caps.IsDefault, caps.ResetToDefault)
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight
                }
            }
        };
    }

    public override bool OnKeyDown(KeyEvent e) => keyCapture.HandleKey(e) || base.OnKeyDown(e);

    #endregion

    /// <summary>
    /// A setting's explanation, wrapping to the panel's width; bigger on a phone, where the desktop size
    /// comes out too small to read.
    /// </summary>
    private TextFlowContainer descriptionText(string text) => new TextFlowContainer(t =>
    {
        t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, gameLayout?.IsPhone == true ? 19 : 15, FontWeight.Regular);
        t.Color = palette.TextMuted;
    })
    {
        RelativeSizeAxes = Axes.X,
        Width = 1,
        AutoSizeAxes = Axes.Y,
        Text = text
    };

    private SpriteText sectionTitle(string text) => new SpriteText
    {
        Text = text.ToUpperInvariant(),
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 16, FontWeight.Bold),
        Color = palette.TextMuted
    };

    public override void Update()
    {
        base.Update();

        // The canvas shrinks as the UI scale grows past a point the settings panel keeps its width and
        // reaches into the song panel's room rather than squeezing its rows.
        float left = Math.Min(song_panel_room, DrawSize.X - panel_margin - settings_min_width);
        settingsArea.Padding = new MarginPadding
        {
            Left = left,
            Right = panel_margin,
            Top = panel_margin,
            Bottom = panel_bottom_margin
        };

        // The song panel shrinks to what the settings and the buttons leave it (on a phone's canvas, or
        // a big UI scale).
        float roomRight = left - 16 - info_panel_position.X;
        float roomDown = DrawSize.Y + LeftBottomShearButton.DEFAULT_POSITION.Y - backButton.DrawHeight - 12 - info_panel_position.Y;
        float scale = Math.Clamp(Math.Min(roomRight / info_panel_size.X, roomDown / info_panel_size.Y), 0.5f, 1);

        if (Math.Abs(infoPanel.Scale.X - scale) > 0.001f)
            infoPanel.Scale = new Vector2(scale);

        // A long keycap row (seven or eight lanes) in a narrow panel starts closer to its title, short
        // of the revert button.
        foreach (KeycapStrip caps in keycapRows)
        {
            float room = (caps.Parent?.DrawWidth ?? 0) - caps.DrawWidth - RevertToDefaultButton.SIZE - RevertToDefaultButton.SPACING;
            caps.X = Math.Clamp(room, 96, keycap_x);
        }
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        showAnimation();
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);

        if (viewingScore)
        {
            viewingScore = false;
            showAnimation();
            return;
        }

        // Returning here means gameplay + results are done — fall straight back to song selection.
        Exit();
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);
        hideAnimation();
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        hideAnimation();
    }

    private void showAnimation()
    {
        infoPanel.AnimateIn();
        settingsPanel.ClearTransforms();
        settingsPanel.MoveToX(0, 800, UserInterfaceConstant.SoftBounce).FadeIn(400, Easing.OutQuint);
        // The shear buttons hug the screen edges, so any overshoot would show their slanted ends.
        backButton.MoveTo(LeftBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        startButton.MoveTo(RightBottomShearButton.DEFAULT_POSITION, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        this.FadeIn(400, Easing.OutQuint);
    }

    private void hideAnimation()
    {
        infoPanel.AnimateOut();
        settingsPanel.MoveToX(panel_slide, 400, Easing.InCubic).FadeOut(300, Easing.InQuint);
        backButton.MoveTo(new Vector2(-200, LeftBottomShearButton.DEFAULT_POSITION.Y), 400, Easing.OutQuint).FadeOut(400, Easing.OutQuint);
        startButton.MoveTo(new Vector2(200, RightBottomShearButton.DEFAULT_POSITION.Y), 400, Easing.OutQuint).FadeOut(400, Easing.OutQuint);
        this.FadeOut(400, Easing.OutQuint);
    }

    private void onStart()
    {
        // Mid-rebind, Enter just cancels the rebind.
        if (keyCapture.Capturing != null)
        {
            keyCapture.CancelCapture();
            return;
        }

        startButton.Flash(Color.White);
        screenStack.Push(new GameplayScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });
    }

    private void onBack()
    {
        // Mid-rebind, Escape just cancels the rebind.
        if (keyCapture.Capturing != null)
        {
            keyCapture.CancelCapture();
            return;
        }

        Exit();
    }
}
