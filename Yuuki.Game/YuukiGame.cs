// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
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
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Cursor;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Drawables;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface.Dialog;
using Yuuki.Game.Graphics.UserInterface.Overlay;
using Yuuki.Game.Resources;
using Yuuki.Game.Utilities;

namespace Yuuki.Game;

public partial class YuukiGame : App
{
    private DrawSizePreservingFillContainer mainContainer = null!;

    /// <summary>
    /// The canvas the interface is laid out on at UI scale 1.
    /// </summary>
    private static readonly Vector2 base_draw_size = new Vector2(1920, 1080);

    /// <summary>
    /// UI scale snaps to steps of this, so a dragged slider settles on round values.
    /// </summary>
    private const double ui_scale_step = 0.05;

    private Reactive<double> uiScale = null!;
    private double displayedUIScale = 1;
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

        Add(new SafeAreaContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = mainContainer = new DrawSizePreservingFillContainer
            {
                RelativeSizeAxes = Axes.Both
            }
        });

        Cache(audioManager = new YuukiAudioManager());
        mainContainer.Add(audioManager);

        var uiSounds = new UISoundPlayer();
        Cache(uiSounds);
        mainContainer.Add(uiSounds);

        var volumeFader = new InactiveVolumeFader();
        Cache(volumeFader);
        mainContainer.Add(volumeFader);

        mainContainer.Add(background = new YuukiBackground
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
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

        uiScale = config.Get<double>(YuukiSetting.UIScale);
        displayedUIScale = targetUIScale;
        applyUIScale();
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

        double target = targetUIScale;
        if (displayedUIScale == target)
            return;

        // Eased, so dragging the slider glides the interface rather than stepping it.
        displayedUIScale += (target - displayedUIScale) * (1 - Math.Exp(-Clock.ElapsedFrameTime / 80));
        if (Math.Abs(target - displayedUIScale) < 0.001)
            displayedUIScale = target;

        applyUIScale();
    }

    private double targetUIScale => Math.Clamp(Math.Round(uiScale.Value / ui_scale_step) * ui_scale_step, YuukiConfigManager.MIN_UI_SCALE, YuukiConfigManager.MAX_UI_SCALE);

    private void applyUIScale()
    {
        float scale = (float)displayedUIScale;
        mainContainer.TargetDrawSize = new Vector2(base_draw_size.X / scale, base_draw_size.Y / scale);
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

        LatestInputType.Value = InputType.Keyboard;
        return base.OnKeyDown(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e) => exiting || base.OnMouseDown(e);

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
        footer.MoveToY(footer.DrawSize.Y + 30, 500, Easing.OutQuint);
        screenStack.Padding = new MarginPadding(0);
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
