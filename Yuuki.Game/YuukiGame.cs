// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sakura.Framework;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Edit;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Cursor;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Drawables;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Dialog;
using Yuuki.Game.Graphics.UserInterface.Overlay;
using Yuuki.Game.Resources;
using Yuuki.Game.Utilities;

namespace Yuuki.Game;

public partial class YuukiGame : App
{
    private DrawSizePreservingFillContainer mainContainer = null!;


    /// <summary>
    /// UI scale snaps to steps of this, so a dragged slider settles on round values.
    /// </summary>
    private const double ui_scale_step = 0.05;

    private Reactive<double> uiScale = null!;

    // The zoom shown: eased toward the target while the UI scale slider moves.
    private double displayedZoom = 1;

    // Whether the window is a phone's, kept between frames for the hysteresis.
    private bool phoneWindow;

    private readonly GameLayout layout = new GameLayout();
    private YuukiScreenStack screenStack = null!;
    private YuukiBackground background = null!;
    private ScreenFooter footer = null!;
    private SettingsOverlay settingsOverlay = null!;
    private MusicPlayerOverlay musicOverlay = null!;
    private DialogOverlay dialogOverlay = null!;

    private NotificationOverlay notificationOverlay = null!;

    private BeatmapImportManager importManager = null!;
    private HoldToExitGameOverlay holdToExit = null!;
    private VolumeOverlay volumeOverlay = null!;
    private TouchEffects touchEffects = null!;

    /// <summary>
    /// How long the screen and music take to go out once quitting is settled.
    /// </summary>
    private const double exit_fade_duration = 800;
    private const double goodbye_exit_duration = 3000;

    // Set once quitting is settled; from then on the window may close.
    private bool exiting;

    private EmbeddedResourceStorage resourceStorage = null!;

    private YuukiConfigManager config = null!;
    private BeatmapsCollection beatmapsCollection = null!;
    private WorkingBeatmap workingBeatmap = null!;
    private BeatmapCollectionReader beatmapCollectionReader = null!;
    private BeatmapNoteStore beatmapNoteStore = null!;
    private YuukiAudioManager audioManager = null!;

    protected override Assembly ResourceAssembly => YuukiResourceAssembly.ResourceAssembly;

    protected override string ResourceRootNamespace => "Yuuki.Game.Resources";

    public ReactiveBool ShowFooter = new ReactiveBool(false);
    public Reactive<InputType> LatestInputType = new Reactive<InputType>(InputType.Keyboard);

    /// <summary>
    /// How the platform opens the files it hands over as links (a phone's), for importing; null where every file
    /// has a path. Set by the platform's head before load.
    /// </summary>
    public IExternalFiles? ExternalFiles { get; init; }

    public override void Load()
    {
        base.Load();

        Host.ScheduleToMainThread(() => Window.Title = "yuuki." + (DebugUtils.IsDebugBuild ? " (development)" : string.Empty));

        resourceStorage = new EmbeddedResourceStorage(YuukiResourceAssembly.ResourceAssembly, "Yuuki.Game.Resources");

        var fontStore = resourceStorage.GetStorageForDirectory("Fonts");

        FontStore.AddFontFamily(fontStore, "Quicksand");
        FontStore.AddFontFamily(fontStore, "Nunito", hasItalics: true);
        FontStore.AddFontFamily(fontStore, "MPLUSRounded1c", script: FontScript.Japanese);
        FontStore.AddFontFamily(fontStore, "Sarabun", script: FontScript.Thai);

        Cache(this);

        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(new CoverColorStore());
        Cache(layout);

        config = new YuukiConfigManager(Host.Storage);
        config.Load();
        Cache(config);

        beatmapsCollection = new BeatmapsCollection();
        workingBeatmap = new WorkingBeatmap();
        Cache(beatmapsCollection);

        Cache(new ScoreStore(Host.Storage));
        Cache(workingBeatmap);

        Cache(new EditorClipboard());

        if (Host.Storage != null)
        {
            var importer = new InternalBeatmapImporter(resourceStorage, Host.Storage);

            if (!config.Get<bool>(YuukiSetting.FirstImport).Value)
            {
                importer.Import();
                config.Get<bool>(YuukiSetting.FirstImport).Value = true;
            }

            beatmapCollectionReader = new BeatmapCollectionReader(Host.Storage, beatmapsCollection);
            beatmapCollectionReader.Read();

            beatmapNoteStore = new BeatmapNoteStore(Host.Storage);
            Cache(beatmapNoteStore);

            Cache(new BeatmapWriter(Host.Storage, beatmapsCollection));

            if (beatmapsCollection.GetBeatmapSetById(BeatmapsCollection.THEME_BEATMAP_SET_ID) == null)
                beatmapsCollection.BeatmapSets.Add(new BeatmapTestUtility().ThemeSet);

            workingBeatmap.BeatmapSet = getInitialBeatmapSet();
        }

        volumeOverlay = new VolumeOverlay();
        Cache(volumeOverlay);

        Add(new YuukiKeyBindingContainer()
        {
            Child = new YuukiGlobalActionContainer()
        });

        // In the safe area, as everything added to the app is: clear of a notch and the system bars. What
        // should reach the screen's edges under them (backgrounds, dims, bars) bleeds out of it.
        Add(mainContainer = new DrawSizePreservingFillContainer
        {
            RelativeSizeAxes = Axes.Both
        });

        Cache(audioManager = new YuukiAudioManager());
        mainContainer.Add(audioManager);

        var uiSounds = new UISoundPlayer();
        Cache(uiSounds);
        mainContainer.Add(uiSounds);

        var volumeFader = new InactiveVolumeFader();
        Cache(volumeFader);
        mainContainer.Add(volumeFader);

        // Covers let go of a moment ago, kept to show again at once.
        var recentCovers = new RecentCoverTextures();
        Cache(recentCovers);
        mainContainer.Add(recentCovers);

        mainContainer.Add(background = new YuukiBackground
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Bleed = Edges.All,
            Alpha = 0
        });
        Cache(background);

        screenStack = new YuukiScreenStack()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        };
        Cache(screenStack);

        notificationOverlay = new NotificationOverlay();
        Cache(notificationOverlay);

        footer = new ScreenFooter()
        {
            Y = 150
        };
        Cache(footer);

        var cursor = new YuukiCursorContainer();
        Cache(cursor);

        mainContainer.Add(screenStack);
        mainContainer.Add(footer);

        musicOverlay = new MusicPlayerOverlay();
        mainContainer.Add(musicOverlay);

        ShowFooter.ValueChanged += _ =>
        {
            toggleFooter();
        };

        settingsOverlay = new SettingsOverlay()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Alpha = 0
        };
        Cache(settingsOverlay);
        mainContainer.Add(settingsOverlay);

        mainContainer.Add(notificationOverlay);

        if (ExternalFiles != null)
            Cache(ExternalFiles);

        importManager = new BeatmapImportManager();
        Cache(importManager);
        mainContainer.Add(importManager);

        dialogOverlay = new DialogOverlay();
        Cache(dialogOverlay);
        mainContainer.Add(dialogOverlay);

        holdToExit = new HoldToExitGameOverlay
        {
            Action = PerformExit
        };
        Cache(holdToExit);
        mainContainer.Add(holdToExit);

        mainContainer.Add(volumeOverlay);
        mainContainer.Add(new VolumeScrollCatcher());

        mainContainer.Add(cursor);

        // Over everything, at the window's own scale (dp on a phone), in the screen's colors, and off in play.
        Add(touchEffects = new TouchEffects { RelativeSizeAxes = Axes.Both });
        touchEffects.Palette.BindTo(footer.ColorPalette);
        touchEffects.Suppressed.BindTo(cursor.Hidden);

        uiScale = config.Get<double>(YuukiSetting.UIScale);
        updateLayout();
        displayedZoom = targetZoom;
        applyZoom();
    }

    private BeatmapSet getInitialBeatmapSet()
    {
        if (config.Get<bool>(YuukiSetting.UseYuukiTheme).Value)
        {
            var theme = beatmapsCollection.GetBeatmapSetById(BeatmapsCollection.THEME_BEATMAP_SET_ID);
            if (theme != null)
                return theme;
        }

        return beatmapsCollection.GetRandomBeatmapSet();
    }

    public override void Update()
    {
        base.Update();

        // The footer is shorter on a phone: the screens above it follow its height.
        if (ShowFooter.Value && Math.Abs(screenStack.Padding.Bottom - footer.DrawSize.Y) > 0.5f)
            screenStack.Padding = new MarginPadding { Bottom = footer.DrawSize.Y };

        // Out of sight it stays out of sight as the window changes shape, which moves the screen's edge away from the
        // safe area's.
        if (!ShowFooter.Value && footer.GetLatestTransformEndTime() <= Clock.CurrentTime && Math.Abs(footer.Y - hiddenFooterY()) > 0.5f)
            footer.Y = hiddenFooterY();

        if (updateLayout())
        {
            // A new canvas, straight away: easing between two layouts would show neither properly.
            displayedZoom = targetZoom;
            applyZoom();
        }

        double target = targetZoom;
        if (displayedZoom == target)
            return;

        // Eased, so dragging the slider glides the interface rather than stepping it.
        displayedZoom += (target - displayedZoom) * (1 - Math.Exp(-Clock.ElapsedFrameTime / 80));
        if (Math.Abs(target - displayedZoom) < 0.001)
            displayedZoom = target;

        applyZoom();
    }

    /// <summary>
    /// How far the interface should be zoomed (see <see cref="GameLayout.Zoom"/>).
    /// </summary>
    private double targetZoom
    {
        get
        {
            double scale = Math.Clamp(Math.Round(uiScale.Value / ui_scale_step) * ui_scale_step, YuukiConfigManager.MIN_UI_SCALE, YuukiConfigManager.MAX_UI_SCALE);
            return GameLayout.Zoom(phoneWindow, scale);
        }
    }

    /// <summary>
    /// Picks the layout for the window and UI scale as they are now (see <see cref="GameLayout.Choose"/>).
    /// True when it changed.
    /// </summary>
    private bool updateLayout()
    {
        phoneWindow = GameLayout.IsPhoneWindow(Window?.Height ?? GameLayout.DESKTOP_ABOVE + 1, phoneWindow);
        layout.Touch.Value = InputManager.CurrentState.LastPointerWasTouch;

        var chosen = GameLayout.Choose(phoneWindow, targetZoom);

        if (chosen == layout.Current.Value)
            return false;

        layout.Current.Value = chosen;
        return true;
    }

    private void applyZoom()
    {
        float zoom = (float)displayedZoom;
        mainContainer.TargetDrawSize = new Vector2(GameLayout.DESKTOP_CANVAS.X / zoom, GameLayout.DESKTOP_CANVAS.Y / zoom);
    }

    public override void OnReady()
    {
        base.OnReady();
        Scheduler.AddDelayed(() =>
        {
            background.FadeIn(500, Easing.OutQuint);

            background.ShowVideo();
        }, 500);
        Scheduler.AddDelayed(() => screenStack.Push(new StartScreen()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }), 1250);
    }

    public void ToggleSettingsOverlay() => settingsOverlay.ToggleVisibility();

    public void ToggleMusicOverlay() => musicOverlay.ToggleVisibility();

    public void ToggleNotificationOverlay() => notificationOverlay.ToggleVisibility();

    /// <summary>
    /// Imports .osz files dropped on the window; anything else is left alone.
    /// </summary>
    public override bool OnDragDropFile(DragDropFileEvent e)
    {
        if (!BeatmapImportManager.CanImport(e.FilePath))
            return base.OnDragDropFile(e);

        importManager.Gather(e.FilePath);
        return true;
    }

    /// <summary>
    /// Imports the .osz files another app hands the game, as a phone's "open with" does. Safe from any thread:
    /// they wait for the game to have loaded.
    /// </summary>
    public void ImportShared(IReadOnlyList<string> paths) => Scheduler.Add(() => importManager.Import(paths.Where(importManager.Accepts).ToList()));

    /// <summary>
    /// Whether the settings, the music player or a dialog is open over the screen.
    /// </summary>
    public bool IsOverlayOpen => settingsOverlay.State == Visibility.Visible
                                 || musicOverlay.State == Visibility.Visible
                                 || notificationOverlay.State == Visibility.Visible
                                 || dialogOverlay.State == Visibility.Visible;

    /// <summary>
    /// The window's close button (or the system's quit): rather than closing
    /// there and then, the game backs out to the main menu and asks first. Once quitting is settled the
    /// window is let go. Two requests in quick succession close it regardless (the framework's doing).
    /// </summary>
    protected override bool OnExitRequested()
    {
        if (exiting)
            return false;

        AttemptExit();
        return true;
    }

    /// <summary>
    /// Asks whether to quit, from the main menu
    /// </summary>
    public void AttemptExit()
    {
        if (exiting)
            return;

        if (dialogOverlay.CurrentDialog is ConfirmExitDialog asking)
        {
            asking.PerformOkAction();
            return;
        }

        // A screen that would lose work (the editor) asks first, and picks the exit back up after.
        foreach (var screen in screenStack.Children.OfType<YuukiScreen>().Where(s => s.Stack == screenStack).ToArray())
        {
            if (screen.InterceptGameExit(AttemptExit))
                return;
        }

        settingsOverlay.Hide();
        musicOverlay.Hide();
        screenStack.ExitTo<MainMenuScreen>();

        dialogOverlay.Push(new ConfirmExitDialog(PerformExit, ongoingOperations: notificationOverlay.OngoingOperations.Select(n => n.Text).ToArray()));
    }

    public void PerformExit()
    {
        if (exiting)
            return;

        exiting = true;

        // A goodbye over a slower fade
        bool goodbye = holdToExit.PlayGoodbye();
        holdToExit.FadeOutGame(goodbye ? goodbye_exit_duration : exit_fade_duration, Host.Exit, goodbye);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        // Nothing more to do on the way out.
        if (exiting)
            return true;

        if (e.Key == Key.SystemBack)
            return onSystemBack(e);

        LatestInputType.Value = InputType.Keyboard;
        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e) => base.OnKeyUp(e.Key == Key.SystemBack ? asEscape(e) : e);

    /// <summary>
    /// A phone's Back (its button, or the swipe in from an edge) does what Escape does: closes what's open,
    /// pauses a song, goes back a screen. At the main menu with nothing open it's left to the system, which
    /// puts the game in the background as Back does any app, rather than Escape's hold to quit.
    /// </summary>
    private bool onSystemBack(KeyEvent e)
    {
        if (screenStack.CurrentScreen is MainMenuScreen && !IsOverlayOpen)
            return false;

        return base.OnKeyDown(asEscape(e));
    }

    private static KeyEvent asEscape(KeyEvent e) => new KeyEvent(Key.Escape, e.Modifiers, e.IsRepeat, e.Timestamp);

    public override bool OnMouseDown(MouseButtonEvent e) => exiting || base.OnMouseDown(e);

    // The touch effects see every finger, whatever takes it.

    public override bool OnTouchDown(TouchEvent e)
    {
        touchEffects.FingerDown(e.Source, e.ScreenSpacePosition);
        return base.OnTouchDown(e);
    }

    public override bool OnTouchMove(TouchEvent e)
    {
        touchEffects.FingerMove(e.Source, e.ScreenSpacePosition);
        return base.OnTouchMove(e);
    }

    public override bool OnTouchUp(TouchEvent e)
    {
        touchEffects.FingerUp(e.Source);
        return base.OnTouchUp(e);
    }

    public override void OnGamepadConnected(GamepadConnectedEvent e)
    {
        LatestInputType.Value = InputType.Gamepad;
        base.OnGamepadConnected(e);
    }

    public override void OnGamepadDisconnected(GamepadDisconnectedEvent e)
    {
        LatestInputType.Value = InputType.Keyboard;
        base.OnGamepadDisconnected(e);
    }

    public override bool OnGamepadAxisMotion(GamepadAxisEvent e)
    {
        LatestInputType.Value = InputType.Gamepad;
        return base.OnGamepadAxisMotion(e);
    }

    public override bool OnGamepadButtonDown(GamepadButtonEvent e)
    {
        LatestInputType.Value = InputType.Gamepad;
        return base.OnGamepadButtonDown(e);
    }

    private void hideFooter()
    {
        footer.MoveToY(hiddenFooterY(), 500, Easing.OutQuint);
        screenStack.Padding = new MarginPadding(0);
    }

    /// <summary>
    /// How far down the footer goes to be out of sight: past the bottom of the screen, not just of the safe area it
    /// sits on. On a phone the screen goes on below that, by the home indicator's room or a rounded corner's, and the
    /// footer's background reaches down there.
    /// </summary>
    private float hiddenFooterY()
    {
        float screenBottom = mainContainer.ToLocalSpace(ToScreenSpace(new Vector2(0, DrawSize.Y))).Y;
        return Math.Max(0, screenBottom - mainContainer.DrawSize.Y) + footer.DrawSize.Y + 30;
    }

    private void showFooter()
    {
        footer.MoveToY(0, 500, Easing.OutQuint);
        screenStack.Padding = new MarginPadding()
        {
            Bottom = footer.DrawSize.Y
        };
    }

    private void toggleFooter()
    {
        if (ShowFooter.Value)
        {
            showFooter();
        }
        else
        {
            hideFooter();
        }
    }
}

public enum InputType
{
    Keyboard,
    Gamepad
}
