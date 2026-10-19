// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.TransformSequenceExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface.Notifications;

/// <summary>
/// One entry in the <see cref="Overlay.NotificationOverlay"/>
/// </summary>
/// <remarks>
/// Clicking runs <see cref="Activated"/> and closes the notification, unless that returns false; any
/// other button closes it outright. <see cref="Close"/> is safe from any thread.
/// </remarks>
public abstract partial class Notification : Container
{
    public const float CORNER_RADIUS = 12;

    /// <summary>
    /// The gap between stacked cards.
    /// </summary>
    public const float SPACING = 8;

    private const float padding = 12;
    private const float badge_size = 36;
    private const float close_size = 28;
    private const float min_height = badge_size + padding * 2;

    /// <summary>
    /// Fired once, when the notification is dismissed (by the user or by finishing).
    /// </summary>
    public event Action? Closed;

    public abstract string Text { get; set; }

    /// <summary>
    /// How long it stays a toast: 10 s for important ones, 4 s for the rest (and as long as it's hovered).
    /// </summary>
    public bool IsImportant { get; init; } = true;

    /// <summary>
    /// Whether to drop it once its toast has gone rather than keep it in the notification center.
    /// </summary>
    public bool Transient { get; init; }

    /// <summary>
    /// Run on click. Return false to keep the notification open.
    /// </summary>
    public Func<bool>? Activated { get; set; }

    /// <summary>
    /// Whether a new one goes above the ones already listed or below them (running tasks).
    /// </summary>
    public virtual bool DisplayOnTop => true;

    public virtual UISound? PopInSound => UISound.NotificationDefault;

    public bool WasClosed { get; private set; }

    private bool read;

    /// <summary>
    /// Whether it has been seen. The overlay marks everything read when the centre closes.
    /// </summary>
    public virtual bool Read
    {
        get => read;
        set
        {
            if (read == value)
                return;

            read = value;
            updateDot();
        }
    }

    private bool isInToastTray;

    public bool IsInToastTray
    {
        get => isInToastTray;
        set
        {
            isInToastTray = value;
            updateDot();
        }
    }

    protected virtual IconUsage CloseButtonIcon => IconUsage.Close;

    /// <summary>
    /// The badge's contents (an icon, a spinner). The badge itself takes <see cref="AccentColor"/>.
    /// </summary>
    protected readonly Container IconContent;

    /// <summary>
    /// The message column, stacked vertically. Subclasses fill it.
    /// </summary>
    protected readonly FlowContainer TextContent;

    /// <summary>
    /// Room under the message for something along the card's width (a progress bar). Give it a height
    /// and the card makes space for it.
    /// </summary>
    protected readonly Container Footer;

    private readonly Container card;
    private readonly Box background;
    private readonly Box badgeFill;
    private readonly Box flash;
    private readonly Circle unreadDot;
    private readonly CloseButton closeButton;

    protected NotificationColors Colors { get; private set; }

    private Color? accent;

    /// <summary>
    /// The badge's color, which also colors the unread dot. The palette's own until set.
    /// </summary>
    protected Color AccentColor
    {
        get => accent ?? Colors.Accent;
        set
        {
            accent = value;

            if (IsLoaded)
                applyAccent();
        }
    }

    protected Notification()
    {
        RelativeSizeAxes = Axes.X;
        Width = 1;
        AlwaysPresent = true;

        Child = card = new Container
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            Masking = true,
            CornerRadius = CORNER_RADIUS,
            // Always drawn: a card culled while its list was out of view mustn't stay culled
            // once the list comes back.
            AlwaysPresent = true,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = Color.Black.WithAlpha(0.25f),
                Radius = 10
            },
            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One
                },
                new Container
                {
                    Position = new Vector2(padding),
                    Size = new Vector2(badge_size),
                    Masking = true,
                    CornerRadius = 10,
                    Children = new Drawable[]
                    {
                        badgeFill = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Size = Vector2.One
                        },
                        IconContent = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Size = Vector2.One
                        }
                    }
                },
                TextContent = new FlowContainer
                {
                    Position = new Vector2(padding * 2 + badge_size, padding + 2),
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 6)
                },
                Footer = new Container
                {
                    X = padding * 2 + badge_size
                },
                unreadDot = new Circle
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    Position = new Vector2(-padding - close_size / 2 + 4, padding + close_size / 2 - 4),
                    Size = new Vector2(8),
                    Alpha = 0
                },
                closeButton = new CloseButton(() => Close(true), CloseButtonIcon)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Position = new Vector2(-padding + 4, padding - 4),
                    Size = new Vector2(close_size),
                    Alpha = 0
                },
                flash = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Color = Color.White,
                    Alpha = 0
                }
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        Colors = NotificationColors.From(colorProvider);
        background.Color = Colors.Card;
        applyAccent();
    }

    private void applyAccent()
    {
        badgeFill.FadeToColor(AccentColor, 200, Easing.OutQuint);
        unreadDot.Color = AccentColor;
    }

    public override void Update()
    {
        base.Update();

        if (closeRequested is bool byUser)
        {
            closeRequested = null;
            playClose(byUser);
        }

        // Laid out by hand: the message wraps to whatever width is left beside the badge and the close
        // button, and the card grows to fit it and whatever is under it.
        TextContent.Width = Math.Max(0, DrawWidth - TextContent.X - padding - close_size);
        Footer.Width = Math.Max(0, DrawWidth - Footer.X - padding);
        Footer.Y = TextContent.Y + TextContent.DrawHeight + (Footer.Height > 0 ? 8 : 0);

        float cardHeight = Math.Max(min_height, Footer.Y + Footer.Height + padding);

        card.Height = cardHeight;
        Height = cardHeight;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        this.FadeInFromZero(250, Easing.OutQuint);
        card.X = 40;
        card.MoveToX(0, 500, UserInterfaceConstant.SoftBounce);

        flash.Alpha = 0.35f;
        flash.FadeOut(800, Easing.OutQuint);
    }

    private void updateDot() => unreadDot.FadeTo(!read && !isInToastTray && !IsHovered ? 1 : 0, 150, Easing.OutQuint);

    public override bool OnHover(MouseEvent e)
    {
        background.FadeToColor(Colors.CardHover, 150, Easing.OutQuint);
        closeButton.FadeIn(150, Easing.OutQuint);
        unreadDot.FadeOut(150, Easing.OutQuint);
        return true;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        background.FadeToColor(Colors.Card, 150, Easing.OutQuint);
        closeButton.FadeOut(150, Easing.OutQuint);
        updateDot();
        return true;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (e.Button != MouseButton.Left)
        {
            Close(true);
            return true;
        }

        base.OnMouseDown(e);
        return true;
    }

    public override bool OnClick(MouseButtonEvent e)
    {
        if (e.Button != MouseButton.Left)
            return true;

        if (Activated?.Invoke() == false)
            return true;

        Close(false);
        return true;
    }

    // A close asked for (from any thread), played out on the next update.
    private volatile object? closeRequested;

    /// <summary>
    /// Dismisses the notification. <paramref name="byUser"/> picks the slide away of a deliberate close.
    /// Safe from any thread.
    /// </summary>
    public virtual void Close(bool byUser)
    {
        if (WasClosed)
            return;

        WasClosed = true;
        Closed?.Invoke();
        closeRequested = byUser;
    }

    private void playClose(bool byUser)
    {
        if (byUser)
            card.MoveToX(60, 300, Easing.InCubic);

        this.TransformSequence().FadeOut(byUser ? 300 : 150, Easing.InCubic).OnComplete(_ => Expire());
    }

    private partial class CloseButton : Container
    {
        private readonly Action action;
        private readonly Circle hover;

        public CloseButton(Action action, IconUsage iconUsage)
        {
            this.action = action;

            Children = new Drawable[]
            {
                hover = new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Color = Color.White,
                    Alpha = 0.1f
                },
                new IconSprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = iconUsage,
                    IconSize = 16,
                    Color = Color.White
                }
            };
        }

        public override bool OnHover(MouseEvent e)
        {
            hover.FadeTo(0.25f, 150, Easing.OutQuint);
            return true;
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            hover.FadeTo(0.1f, 150, Easing.OutQuint);
            return true;
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            base.OnMouseDown(e);
            return true;
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            action();
            return true;
        }
    }
}

public readonly record struct NotificationColors(Color Panel, Color Card, Color CardHover, Color Accent)
{
    public static readonly Color GREEN = ColorExtensions.FromHex("7BD389");
    public static readonly Color BLUE = ColorExtensions.FromHex("6CC7F5");
    public static readonly Color YELLOW = ColorExtensions.FromHex("F5C46C");
    public static readonly Color RED = ColorExtensions.FromHex("F07178");

    public static NotificationColors From(IYuukiColorProvider? provider)
    {
        var palette = provider?.GetPalette(YuukiScreenPart.Play) ?? DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

        return new NotificationColors(
            Panel: palette.Surface.Darken(0.4f),
            Card: palette.Surface.Darken(0.25f),
            CardHover: palette.Surface.Darken(0.15f),
            Accent: palette.Accent);
    }
}
