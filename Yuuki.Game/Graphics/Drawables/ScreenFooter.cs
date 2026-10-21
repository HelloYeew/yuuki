// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
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
using Sakura.Framework.Reactive;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.Drawables;

public partial class ScreenFooter : Container
{
    /// <summary>
    /// How much bigger the footer's buttons are on a phone, where they're tapped; still inside the bar.
    /// </summary>
    private const float phone_button_scale = 1.25f;

    private static readonly Vector2 buttons_position = new Vector2(-16, -14);

    private SpriteText screenNameText = null!;
    private ScrollingText screenDescriptionText = null!;
    private Box footerBackgroundBoxLower = null!;
    private Box footerBackgroundBoxMiddle = null!;
    private Box footerBackgroundBoxUpper = null!;
    private Box screenNameBackground = null!;
    private Container screenNameBackgroundContainer = null!;
    private FlowContainer footerKeybindContainer = null!;
    private FooterMusicButton musicButton = null!;
    private FooterMusicButton settingsButton = null!;
    private FooterNotificationButton notificationButton = null!;
    private FooterClock clock = null!;
    private FlowContainer buttons = null!;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    public ScreenFooter()
    {
        Anchor = Anchor.BottomCentre;
        Origin = Anchor.BottomCentre;
        RelativeSizeAxes = Axes.X;
        Size = new Vector2(1, 100);
    }

    public readonly Reactive<string> ScreenName = new Reactive<string>("Main Menu");

    public readonly Reactive<string> ScreenDescription = new Reactive<string>("Let's have some fun");

    public readonly Reactive<YuukiColorPalette> ColorPalette = new Reactive<YuukiColorPalette>(DefaultYuukiColorProvider.GetFallbackDefaultPalette());

    public readonly Reactive<IReadOnlyList<ScreenAction>> ScreenActions = new Reactive<IReadOnlyList<ScreenAction>>(new List<ScreenAction>());

    [BackgroundDependencyLoader]
    private void load(YuukiGame? game)
    {
        Add(footerBackgroundBoxLower = new Box()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, 60),
            Alpha = 0.75f
        });

        Add(footerBackgroundBoxMiddle = new Box()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, 8),
            Position = new Vector2(0, -60)
        });

        Add(footerBackgroundBoxUpper = new Box()
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, 34),
            Position = new Vector2(0, -68),
            Alpha = 0.5f
        });

        Add(screenNameBackgroundContainer = new Container()
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            AutoSizeAxes = Axes.X,
            Height = 53,
            AutoSizeMinimum = new Vector2(100, 0),
            AutoSizeDuration = 250,
            AutoSizeEasing = Easing.OutQuint,
            Position = new Vector2(0, -60),
            Children = new Drawable[]
            {
                new Container()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Shear = new Vector2(-0.67f, 0),
                    CornerRadius = 5,
                    Position = new Vector2(28, 0),
                    Child = screenNameBackground = new Box()
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both
                    }
                },
                screenNameText = new SpriteText()
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Position = new Vector2(-16, 0),
                    // A margin rather than the container's padding, which would shrink the background with it.
                    Margin = new MarginPadding
                    {
                        Left = 59
                    },
                    Text = ScreenName.Value,
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 32, FontWeight.Bold)
                }
            }
        });

        Add(screenDescriptionText = new ScrollingText()
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Position = new Vector2(16, -72),
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(0.6f, 60),
            Text = ScreenDescription.Value,
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 20, FontWeight.Regular)
        });

        Add(footerKeybindContainer = new FlowContainer()
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            Size = new Vector2(1, 60),
            Padding = new MarginPadding(16),
            Spacing = new Vector2(16, 0),
            Direction = FlowDirection.Horizontal
        });

        Add(buttons = new FlowContainer()
        {
            Size = new Vector2(400, 38),
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Position = buttons_position,
            Spacing = new Vector2(8, 0),
            Alignment = FlowAlignment.End,
            Children = new Drawable[]
            {
                musicButton = new FooterMusicButton(() => game?.ToggleMusicOverlay(), IconUsage.MusicNote)
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight
                },
                notificationButton = new FooterNotificationButton(() => game?.ToggleNotificationOverlay())
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight
                },
                clock = new FooterClock
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight
                },
                settingsButton = new FooterMusicButton(() => game?.ToggleSettingsOverlay(), IconUsage.Settings)
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight
                },
            }
        });

        ScreenName.ValueChanged += e => screenNameText.Text = e.NewValue;

        ScreenDescription.ValueChanged += e => Scheduler.Add(() => screenDescriptionText.Text = e.NewValue);

        ScreenActions.ValueChanged += e =>
        {
            footerKeybindContainer.Clear();
            foreach (var action in e.NewValue)
            {
                footerKeybindContainer.Add(new FooterKeybindItem(action.Name, action.Action, action.KeyCombination));
            }
        };

        ColorPalette.ValueChanged += e =>
        {
            footerBackgroundBoxLower.FadeToColor(e.NewValue.Surface, 250, Easing.OutQuint);
            footerBackgroundBoxMiddle.FadeToColor(e.NewValue.Surface, 250, Easing.OutQuint);
            footerBackgroundBoxUpper.FadeToColor(e.NewValue.Surface.Lighten(0.5f), 250, Easing.OutQuint);
            screenNameBackground.FadeToColor(e.NewValue.Surface, 250, Easing.OutQuint);
            screenNameText.FadeToColor(e.NewValue.Text, 250, Easing.OutQuint);
            screenDescriptionText.Color = e.NewValue.Text.Darken(0.2f);
            musicButton.SetAccent(e.NewValue.Text);
            notificationButton.SetAccent(e.NewValue.Text);
            clock.SetAccent(e.NewValue.Text, e.NewValue.TextMuted, e.NewValue.Surface);
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (gameLayout != null)
            BindValueChanged(gameLayout.Current, e => applyLayout(e.NewValue == DisplayLayout.Phone), true);
    }

    /// <summary>
    /// Scales the action buttons and the right-hand buttons up on a phone, each about its bottom corner,
    /// with the right-hand ones lowered to stay centred in the bar.
    /// </summary>
    private void applyLayout(bool phone)
    {
        float scale = phone ? phone_button_scale : 1;

        footerKeybindContainer.Scale = new Vector2(scale);
        buttons.Scale = new Vector2(scale);
        buttons.Position = new Vector2(buttons_position.X, buttons_position.Y + (scale - 1) * buttons.Height / 2);
    }
}

public partial class FooterMusicButton : Container
{
    private Box background = null!;
    private IconSprite icon = null!;
    private readonly Action? action;
    private readonly IconUsage iconUsage;

    public FooterMusicButton(Action? action, IconUsage iconUsage)
    {
        this.action = action;
        this.iconUsage = iconUsage;
        Size = new Vector2(38);
        Masking = true;
        CornerRadius = 8;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.White,
                Alpha = 0.15f
            },
            icon = new IconSprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = iconUsage,
                IconSize = 22,
                Color = Color.Black
            },
            new HoverClickSounds()
        };
    }

    /// <summary>
    /// Recolors the button to match the current screen palette.
    /// </summary>
    public void SetAccent(Color accent)
    {
        if (icon != null)
            icon.FadeToColor(accent, 250, Easing.OutQuint);
    }

    public override bool OnHover(MouseEvent e)
    {
        background.FadeTo(0.35f, 150, Easing.OutQuint);
        return true;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        background.FadeTo(0.15f, 150, Easing.OutQuint);
        return true;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        action?.Invoke();
        background.FlashColor(Color.White, 250, Easing.OutQuint);
        return true;
    }
}

public partial class FooterNotificationButton : Container
{
    private readonly FooterMusicButton button;
    private Container badge = null!;
    private SpriteText countText = null!;

    [Resolved(canBeNull: true)]
    private NotificationOverlay? notifications { get; set; }

    public FooterNotificationButton(Action? action)
    {
        Size = new Vector2(38);
        Child = button = new FooterMusicButton(action, IconUsage.Notifications);
    }

    public void SetAccent(Color accent) => button.SetAccent(accent);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Add(badge = new Container
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.Centre,
            Position = new Vector2(-4, 4),
            // Round at one digit, a pill as the count grows.
            AutoSizeAxes = Axes.X,
            Height = 18,
            AutoSizeMinimum = new Vector2(18, 0),
            Masking = true,
            CornerRadius = 9,
            Alpha = 0,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Color = ColorExtensions.FromHex("ff66aa")
                },
                countText = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Margin = new MarginPadding { Left = 4, Right = 4 },
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 12, FontWeight.Bold),
                    Color = Color.White
                }
            }
        });

        notifications?.UnreadCount.BindValueChanged(e => showCount(e.NewValue), true);
    }

    private void showCount(int count)
    {
        countText.Text = count > 99 ? "99+" : count.ToString();

        if (count > 0)
        {
            if (badge.Alpha < 1)
            {
                badge.Scale = new Vector2(0.5f);
                badge.ScaleTo(Vector2.One, 400, UserInterfaceConstant.SoftBounce);
            }

            badge.FadeIn(200, Easing.OutQuint);
        }
        else
        {
            badge.FadeOut(200, Easing.OutQuint);
        }
    }
}
