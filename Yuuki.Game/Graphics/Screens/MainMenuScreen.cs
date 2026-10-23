// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.Screens;

public partial class MainMenuScreen : YuukiScreen, IHaveScreenAction
{
    /// <summary>
    /// Where the menu bar and its buttons sit while shown, from the bottom.
    /// </summary>
    private const float menu_shown_y = -98;

    /// <summary>
    /// Where they go while idle: below the bottom edge.
    /// </summary>
    private const float menu_idle_y = 220;

    /// <summary>
    /// The least room kept either side of the buttons; on a narrower canvas they shrink to keep it.
    /// </summary>
    private const float menu_side_margin = 24;

    private const float logo_margin = 48;
    private static readonly Vector2 logo_size = new Vector2(340, 138);

    private const double go_idle_duration = 1400;
    private const double wake_duration = 500;

    private Box menuBox = null!;
    private FlowContainer buttonFlow = null!;

    private MenuShearButton playButton = null!;
    private MenuShearButton editButton = null!;
    private MenuShearButton listButton = null!;
    private MenuShearButton moreButton = null!;
    private MenuShearButton? exitButton;

    [Resolved]
    private YuukiScreenStack screenStack { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiGame? game { get; set; }

    [Resolved(canBeNull: true)]
    private HoldToExitGameOverlay? holdToExit { get; set; }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    private NowPlayingToast nowPlaying = null!;
    private BeatSyncingLogo logo = null!;
    private SpriteText versionText = null!;

    private IdleTracker idleTracker = null!;
    private Container idleLayer = null!;
    private Box idleShade = null!;
    private NowPlayingPanel idlePanel = null!;

    // Whether this is the screen showing (between entering/resuming and suspending/exiting).
    private bool active;

    // How idle the menu looks, 0 (the menu) to 1 (idle), eased from idleFrom to idleTo.
    private float idleAmount;
    private float idleFrom;
    private float idleTo;
    private double idleChangeTime = double.NegativeInfinity;
    private double idleDuration = 1;
    private Easing idleEasing = Easing.OutQuint;

    /// <summary>
    /// The "now playing" card (diagnostics/tests).
    /// </summary>
    public NowPlayingToast NowPlaying => nowPlaying;

    /// <summary>
    /// What decides when the menu goes idle (diagnostics/tests).
    /// </summary>
    public IdleTracker Idle => idleTracker;

    /// <summary>
    /// The song panel shown while idle (diagnostics/tests).
    /// </summary>
    public NowPlayingPanel IdlePanel => idlePanel;

    /// <summary>
    /// How far into the idle look the menu is, 0 to 1 (diagnostics/tests).
    /// </summary>
    public float IdleAmount => idleAmount;

    /// <summary>
    /// The logo's position (diagnostics/tests).
    /// </summary>
    public Vector2 LogoPosition => logo.Position;

    public override bool HideVideo => false;

    public MainMenuScreen()
    {
        ScreenActions = new List<ScreenAction>()
        {
            new ScreenAction()
            {
                Name = "Play",
                Action = onPlayButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.P))
            },
            new ScreenAction()
            {
                Name = "Edit",
                Action = onEditButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.E))
            },
            new ScreenAction()
            {
                Name = "List",
                Action = onListButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.L))
            },
            new ScreenAction()
            {
                Name = "More",
                Action = onMoreButtonClick,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.M))
            }
        };
    }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.MainMenu;

    [BackgroundDependencyLoader]
    private void load(ITextureManager textureManager, [CanBeNull] IYuukiColorProvider? colorProvider)
    {
        ScreenName.Value = "Main Menu";
        ScreenHelperText.Value = "Let’s have some fun!";

        Add(new MenuSideFlashes());

        YuukiColorPalette palette = Palette;

        // While idle: a soft shade along the bottom, so the logo and the song read on any background,
        // and the song itself in the corner. Laid out against the whole window (see Update).
        Add(idleLayer = new Container
        {
            RelativeSizeAxes = Axes.X,
            Children = new Drawable[]
            {
                idleShade = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 420,
                    ColorInfo = ColorInfo.GradientVertical(Color.Black.WithAlpha(0), Color.Black.WithAlpha(0.65f)),
                    Alpha = 0
                },
                idlePanel = new NowPlayingPanel(palette)
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Y = -logo_margin,
                    Alpha = 0
                }
            }
        });

        Add(logo = new BeatSyncingLogo()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = logo_size,
            Position = new Vector2(logo_margin),
            Texture = textureManager.Get("logo.png")
        });

        Add(nowPlaying = new NowPlayingToast
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-48, 48)
        });

        Add(menuBox = new Box()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Position = new Vector2(0, 98),
            Size = new Vector2(1, 150),
            Color = ColorExtensions.FromHex("#DFCFBC")
        });

        Add(buttonFlow = new FlowContainer()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            AutoSizeAxes = Axes.Both,
            Position = new Vector2(0, 98),
            Spacing = new Vector2(8, 0),
            Children = new Drawable[]
            {
                playButton = new MenuShearButton()
                {
                    Icon = IconUsage.PlayArrow,
                    Text = "Play",
                    BackgroundColor = colorProvider.IsNotNull() ? colorProvider.GetPalette(YuukiScreenPart.Play).Accent : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play).Accent,
                    Action = onPlayButtonClick
                },
                editButton = new MenuShearButton()
                {
                    Icon = IconUsage.Edit,
                    Text = "Edit",
                    BackgroundColor = colorProvider.IsNotNull() ? colorProvider.GetPalette(YuukiScreenPart.Edit).Accent : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Edit).Accent,
                    Action = onEditButtonClick
                },
                listButton = new MenuShearButton()
                {
                    Icon = IconUsage.List,
                    Text = "List",
                    BackgroundColor = colorProvider.IsNotNull() ? colorProvider.GetPalette(YuukiScreenPart.List).Accent : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.List).Accent,
                    Action = onListButtonClick
                },
                moreButton = new MenuShearButton()
                {
                    Icon = IconUsage.Book,
                    Text = "More",
                    BackgroundColor = colorProvider.IsNotNull() ? colorProvider.GetPalette(YuukiScreenPart.More).Accent : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.More).Accent,
                    Action = onMoreButtonClick
                }
            }
        });

        // Apart from the menu's own buttons, in the corner opposite the version, as quitting is. Not on a phone or tablet,
        // where only the player ends an app, from the system.
        if (!RuntimeInfo.IsMobile)
        {
            Add(exitButton = new MenuShearButton
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-36, -18),
                ButtonSize = new Vector2(190, 62),
                Icon = IconUsage.PowerSettingsNew,
                Text = "Exit",
                BackgroundColor = UserInterfaceConstant.DangerColor,
                Action = onExitButtonClick
            });
        }

        Add(versionText = new SpriteText()
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Text = DebugUtils.IsDebugBuild
                ? "Development Build"
                : $"Version {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}",
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
            Color = DebugUtils.IsDebugBuild ? Color.Red : ColorExtensions.FromHex("#E4C4A4"),
            Margin = new MarginPadding(10)
        });

        // In front of everything, so a press that wakes the menu reaches it first and stops there.
        Add(idleTracker = new IdleTracker
        {
            CanIdle = () => active && !(game?.IsOverlayOpen ?? false)
        });
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // A new song while the menu's up announces itself. Drawable's BindValueChanged, not the
        // reactive's own: the working beatmap outlives this screen and would keep it alive.
        // While idle, the song panel shows it instead.
        if (workingBeatmap != null)
            BindValueChanged(workingBeatmap.ReactiveWorkingBeatmapSet, e =>
            {
                if (IsCurrentScreen && !idleTracker.IsIdle.Value)
                    nowPlaying.Show(e.NewValue);
            });

        BindValueChanged(idleTracker.IsIdle, e => onIdleChanged(e.NewValue));
    }

    private void onIdleChanged(bool idle)
    {
        idleFrom = idleAmount;
        idleTo = idle ? 1 : 0;
        idleChangeTime = Clock.CurrentTime;
        idleDuration = idle ? go_idle_duration : wake_duration;
        idleEasing = idle ? Easing.InOutQuint : Easing.OutQuint;

        CursorHidden.Value = idle;

        // Cleared first: the framework retargets a transform that's still running instead of starting over.
        menuBox.ClearTransforms();
        buttonFlow.ClearTransforms();

        if (!active)
        {
            // Woken by leaving: back in place for when the menu returns. The footer is the next screen's call.
            menuBox.Y = buttonFlow.Y = menu_shown_y;
            return;
        }

        game?.ShowFooter.Value = !idle;

        if (idle)
        {
            nowPlaying.HideNow();
            menuBox.MoveToY(menu_idle_y, 600, Easing.InQuint).FadeOut(600, Easing.InQuint);
            buttonFlow.MoveToY(menu_idle_y, 600, Easing.InQuint).FadeOut(600, Easing.InQuint);
        }
        else
        {
            menuBox.MoveToY(menu_shown_y, 500, Easing.OutBack).FadeIn(300, Easing.OutQuint);
            buttonFlow.MoveToY(menu_shown_y, 500, Easing.OutBack).FadeIn(300, Easing.OutQuint);
        }
    }

    public override void Update()
    {
        base.Update();

        double progress = Math.Clamp((Clock.CurrentTime - idleChangeTime) / idleDuration, 0, 1);
        idleAmount = idleFrom + (idleTo - idleFrom) * (float)EasingFunctions.Apply(idleEasing, progress);

        // The screen stops above the footer, which goes away while idle. The idle look is laid out
        // against the whole window instead, so nothing jumps when the footer comes or goes.
        float fullHeight = DrawSize.Y + (Parent?.Padding.Bottom ?? 0);
        idleLayer.Height = fullHeight;

        // The song card keeps clear of the logo.
        nowPlaying.MaxCardWidth = DrawWidth - (logo_margin + logo_size.X + logo_margin) - logo_margin;

        // On a narrow canvas (a phone's, or a big UI scale) the buttons shrink to fit, the bar with them.
        float menuScale = MenuShearButton.ScaleToFit(buttonFlow.DrawSize, DrawWidth - 2 * menu_side_margin);
        buttonFlow.Scale = new Vector2(menuScale);
        menuBox.Height = buttonFlow.DrawHeight * menuScale;

        // The logo glides from the top-left to the bottom-left corner.
        float idleLogoY = fullHeight - logo_size.Y - logo_margin;
        logo.Position = new Vector2(logo_margin, logo_margin + (idleLogoY - logo_margin) * idleAmount);

        // The panel comes in once the menu is mostly out of the way.
        const float panel_from = 0.35f;

        idleShade.Alpha = settled(idleAmount);
        idlePanel.Alpha = settled((idleAmount - panel_from) / (1 - panel_from));
        idlePanel.X = -logo_margin + (1 - idleAmount) * 60;

        // Bigger on a phone, so its text comes out big enough to read.
        float panelScale = gameLayout?.IsPhone == true ? 1.2f : 1;
        if (idlePanel.Scale.X != panelScale)
            idlePanel.Scale = new Vector2(panelScale);
        versionText.Alpha = settled(1 - idleAmount);
        if (exitButton != null)
            exitButton.Alpha = versionText.Alpha;
    }

    /// <summary>
    /// An eased alpha, clamped and snapped to its ends once it's nearly there. The framework skips an
    /// alpha change under 0.0001, so the last tiny steps of an ease would leave it a hair short of fully
    /// shown, or (worse) a hair short of fully hidden and so still drawn.
    /// </summary>
    private static float settled(float alpha) => alpha >= 0.999f ? 1 : alpha <= 0.001f ? 0 : alpha;

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        active = true;
        idleTracker.Wake();
        nowPlaying.Show(workingBeatmap?.BeatmapSet, force: true);
        menuBox.MoveToY(menu_shown_y, 500, Easing.OutBack);
        buttonFlow.MoveToY(menu_shown_y, 500, Easing.OutBack);
        this.FadeIn(500, Easing.OutQuint);
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        active = false;
        holdToExit?.AbortHold();
        idleTracker.Wake();
        nowPlaying.HideNow();
        menuBox.MoveToX(-2000, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        buttonFlow.MoveToX(-2000, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        this.FadeOut(500, Easing.OutQuint);
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);
        active = false;
        holdToExit?.AbortHold();
        idleTracker.Wake();
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);
        active = true;
        idleTracker.Wake();
        nowPlaying.Show(workingBeatmap?.BeatmapSet, force: true);
        menuBox.MoveToX(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        buttonFlow.MoveToX(0, 500, Easing.OutQuint).FadeIn(500, Easing.OutQuint);
        this.FadeIn(500, Easing.OutQuint);
    }

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    /// <summary>
    /// Holding Escape quits: the screen darkens while it's held, and the game
    /// goes once it's dark. Letting go early backs out. Not while something's open over the menu, which
    /// has Escape for closing itself.
    /// </summary>
    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.Escape && !e.IsRepeat && active && holdToExit != null && !(game?.IsOverlayOpen ?? false) && !RuntimeInfo.IsMobile)
        {
            holdToExit.BeginHold();
            return true;
        }

        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (e.Key == Key.Escape)
            holdToExit?.AbortHold();

        return base.OnKeyUp(e);
    }

    private void onPlayButtonClick()
    {
        playButton.Flash(Color.White);
        screenStack.Push(new PlayScreen()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        });
    }

    private void onEditButtonClick()
    {
        editButton.Flash(Color.White);
        screenStack.Push(new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            EditMode = true
        });
    }

    private void onListButtonClick()
    {
        listButton.Flash(Color.White);
    }

    private void onMoreButtonClick()
    {
        moreButton.Flash(Color.White);
    }

    private void onExitButtonClick() => game?.AttemptExit();
}
