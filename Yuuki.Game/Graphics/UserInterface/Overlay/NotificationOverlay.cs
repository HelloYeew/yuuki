// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface.Notifications;

namespace Yuuki.Game.Graphics.UserInterface.Overlay;

public partial class NotificationOverlay : FocusedOverlayContainer
{
    private const float panel_width = 380;
    private const float panel_max_height = 560;
    private const float header_height = 60;
    private const float edge_margin = 20;

    /// <summary>
    /// The music player popup's distance from the bottom, so the two panels open from the same place.
    /// </summary>
    private const float footer_margin = 116;

    private const float toast_width = 340;

    /// <summary>
    /// How many notifications haven't been seen yet.
    /// </summary>
    public Reactive<int> UnreadCount { get; } = new Reactive<int>(0);

    /// <summary>
    /// Every notification still around, toasts included.
    /// </summary>
    public IEnumerable<Notification> AllNotifications => toasts.Notifications.Concat(tasks.Notifications).Concat(earlier.Notifications);

    private readonly ConcurrentQueue<Notification> pending = new ConcurrentQueue<Notification>();

    private Box dim = null!;
    private Container panel = null!;
    private YuukiScrollableContainer scroll = null!;
    private Container listContent = null!;
    private Container emptyState = null!;
    private PillButton clearAllButton = null!;
    private ToastStack toasts = null!;
    private NotificationGroup tasks = null!;
    private NotificationGroup earlier = null!;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        var colors = NotificationColors.From(colorProvider);

        RelativeSizeAxes = Axes.Both;
        Size = Vector2.One;
        AlwaysPresent = true;

        Children = new Drawable[]
        {
            dim = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
                Color = Color.Black,
                Alpha = 0,
                AlwaysPresent = true
            },
            panel = new Container
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-edge_margin, -footer_margin),
                Size = new Vector2(panel_width, 220),
                Masking = true,
                CornerRadius = 12,
                Alpha = 0,
                AlwaysPresent = true,
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
                        Size = Vector2.One,
                        Color = colors.Panel
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.CentreLeft,
                        Position = new Vector2(edge_margin, header_height / 2),
                        Text = "Notifications",
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Bold),
                        Color = Color.White
                    },
                    clearAllButton = new PillButton(Color.White.WithAlpha(0.15f), Color.White)
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.CentreRight,
                        Position = new Vector2(-16, header_height / 2),
                        Text = "Clear all",
                        Action = clearAll
                    },
                    scroll = new YuukiScrollableContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        Y = header_height,
                        Masking = true,
                        Child = listContent = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Width = 1,
                            Padding = new MarginPadding { Left = 12, Right = 12, Bottom = 12 },
                            AlwaysPresent = true,
                            Children = new Drawable[]
                            {
                                tasks = new NotificationGroup("In progress"),
                                earlier = new NotificationGroup("Earlier")
                            }
                        }
                    },
                    emptyState = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        Y = header_height,
                        Height = 140,
                        Children = new Drawable[]
                        {
                            new IconSprite
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Y = 14,
                                Icon = IconUsage.Notifications,
                                IconSize = 40,
                                Color = Color.White.WithAlpha(0.35f)
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Y = 66,
                                Text = "You're all caught up",
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold),
                                Color = Color.White
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Y = 92,
                                Text = "Messages and background tasks show up here.",
                                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 14, FontWeight.Bold),
                                Color = Color.White.WithAlpha(0.6f)
                            }
                        }
                    }
                }
            },
            toasts = new ToastStack(addToCentre)
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-edge_margin, edge_margin),
                Width = toast_width
            }
        };
    }

    /// <summary>
    /// Shows <paramref name="notification"/>. Safe from any thread.
    /// </summary>
    public void Post(Notification notification) => pending.Enqueue(notification);

    /// <summary>
    /// The background tasks still running (queued or active), for the exit question to warn about.
    /// </summary>
    public IEnumerable<ProgressNotification> OngoingOperations => AllNotifications.OfType<ProgressNotification>().Where(n => n.Ongoing);

    public override void Update()
    {
        base.Update();

        while (pending.TryDequeue(out var notification))
            postNow(notification);

        UnreadCount.Value = AllNotifications.Count(n => !n.WasClosed && !n.Read);

        layOutCentre();
    }

    private void postNow(Notification notification)
    {
        if (notification is ProgressNotification progress)
            progress.CompletionTarget ??= Post;

        if (notification.PopInSound is UISound sound)
            uiSounds?.Play(sound);

        if (State == Visibility.Hidden)
            toasts.Add(notification);
        else
            addToCentre(notification);
    }

    private void addToCentre(Notification notification)
    {
        if (notification.Transient)
        {
            notification.Close(false);
            return;
        }

        notification.IsInToastTray = false;
        (notification is ProgressNotification ? tasks : earlier).Add(notification);
    }

    private void clearAll()
    {
        foreach (var notification in tasks.Notifications.Concat(earlier.Notifications).ToArray())
        {
            if (notification is not ProgressNotification { Ongoing: true })
                notification.Close(true);
        }
    }

    /// <summary>
    /// Stacks the two groups and sizes the panel to them, up to a height that leaves the screen visible
    /// above it; past that the list scrolls.
    /// </summary>
    private void layOutCentre()
    {
        tasks.Y = 0;
        earlier.Y = tasks.Height;

        float contentHeight = tasks.Height + earlier.Height;
        bool empty = contentHeight <= 0;

        listContent.Height = contentHeight + 12;
        emptyState.Alpha = empty ? 1 : 0;
        clearAllButton.Alpha = empty ? 0 : 1;

        float maxHeight = Math.Min(panel_max_height, DrawHeight - footer_margin - edge_margin * 2);
        float wanted = header_height + (empty ? emptyState.Height : listContent.Height);

        panel.Height = Math.Clamp(wanted, header_height, Math.Max(header_height, maxHeight));
        scroll.Height = panel.Height - header_height;
    }

    #region Visibility and input

    // Closed, only the toasts take input; open, the whole screen does, so a click on the dim closes it.
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
        State == Visibility.Visible || toasts.ReceivePositionalInputAt(screenSpacePos);

    public override bool OnClick(MouseButtonEvent e)
    {
        if (State != Visibility.Visible || panel.Contains(e.ScreenSpaceMousePosition))
            return false;

        Hide();
        return true;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (State == Visibility.Visible && e.Key == Key.N && e.ControlPressed)
        {
            Hide();
            return true;
        }

        return base.OnKeyDown(e);
    }

    protected override void PopIn()
    {
        uiSounds?.Play(UISound.OverlayOpen);

        dim.FadeTo(0.4f, 300, Easing.OutQuint);

        panel.ClearTransforms();
        panel.Y = -footer_margin + 30;
        panel.MoveToY(-footer_margin, 400, UserInterfaceConstant.SoftBounce);
        panel.FadeIn(200, Easing.OutQuint);

        toasts.MoveAllToCentre();
    }

    protected override void PopOut()
    {
        if (IsLoaded)
            uiSounds?.Play(UISound.OverlayClose);

        foreach (var notification in AllNotifications)
            notification.Read = true;

        dim.FadeOut(300, Easing.OutQuint);

        panel.ClearTransforms();
        panel.MoveToY(-footer_margin + 30, 300, Easing.InCubic);
        panel.FadeOut(200, Easing.OutQuint);
    }

    #endregion

    /// <summary>
    /// A labelled group of cards in the centre, newest on top (or, for running tasks, oldest on top so
    /// they keep their place). Hidden while empty.
    /// </summary>
    private partial class NotificationGroup : Container
    {
        private const float label_height = 32;

        private readonly List<Notification> items = new List<Notification>();
        private readonly Container list;
        private readonly SpriteText label;

        public IEnumerable<Notification> Notifications => items.Where(n => !n.WasClosed);

        public NotificationGroup(string title)
        {
            RelativeSizeAxes = Axes.X;
            Width = 1;
            AlwaysPresent = true;

            Children = new Drawable[]
            {
                label = new SpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.CentreLeft,
                    Position = new Vector2(8, label_height / 2),
                    Text = title.ToUpperInvariant(),
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                    Color = Color.White.WithAlpha(0.6f)
                },
                list = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Width = 1,
                    Y = label_height,
                    AlwaysPresent = true
                }
            };
        }

        public void Add(Notification notification)
        {
            if (notification.DisplayOnTop)
                items.Insert(0, notification);
            else
                items.Add(notification);

            notification.Position = Vector2.Zero;
            list.Add(notification);
        }

        public override void Update()
        {
            base.Update();

            items.RemoveAll(n => n.Parent != list);

            float y = 0;

            foreach (var notification in items)
            {
                if (notification.WasClosed)
                    continue;

                if (notification.Y != y)
                    notification.MoveToY(y, 200, Easing.OutQuint);

                y += notification.DrawHeight + Notification.SPACING;
            }

            bool empty = y <= 0;

            label.Alpha = empty ? 0 : 1;
            list.Height = y;
            Height = empty ? 0 : label_height + y;
        }
    }

    /// <summary>
    /// New notifications while the centre is closed, floating in the corner. Each moves into the centre
    /// when its time is up, or straight away when the centre opens.
    /// </summary>
    private partial class ToastStack : Container
    {
        private readonly Action<Notification> moveToCentre;
        private readonly List<Notification> items = new List<Notification>();

        public IEnumerable<Notification> Notifications => items.Where(n => !n.WasClosed);

        public ToastStack(Action<Notification> moveToCentre)
        {
            this.moveToCentre = moveToCentre;
            AlwaysPresent = true;
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
            Notifications.Any(n => n.Contains(screenSpacePos));

        public void Add(Notification notification)
        {
            if (notification.DisplayOnTop)
                items.Insert(0, notification);
            else
                items.Add(notification);

            notification.IsInToastTray = true;
            notification.Position = Vector2.Zero;
            base.Add(notification);

            scheduleTimeout(notification);
        }

        private void scheduleTimeout(Notification notification) => Scheduler.AddDelayed(() =>
        {
            if (notification.WasClosed || notification.Parent != this)
                return;

            if (notification.IsHovered)
            {
                scheduleTimeout(notification);
                return;
            }

            leave(notification);
        }, notification.IsImportant ? 10000 : 4000);

        public void MoveAllToCentre()
        {
            foreach (var notification in Notifications.ToArray())
                leave(notification, immediately: true);
        }

        /// <summary>
        /// Slides a toast away and hands it to the centre.
        /// </summary>
        private void leave(Notification notification, bool immediately = false)
        {
            if (!items.Remove(notification))
                return;

            if (immediately)
            {
                Remove(notification, false);
                moveToCentre(notification);
                return;
            }

            notification.MoveToX(60, 300, Easing.InCubic);
            notification.TransformSequence().FadeOut(300, Easing.InCubic).OnComplete(_ =>
            {
                Remove(notification, false);
                notification.ClearTransforms();
                notification.Alpha = 1;
                moveToCentre(notification);
            });
        }

        public override void Update()
        {
            base.Update();

            items.RemoveAll(n => n.Parent != this);

            float y = 0;

            foreach (var notification in items)
            {
                if (notification.WasClosed)
                    continue;

                if (notification.Y != y)
                    notification.MoveToY(y, 200, Easing.OutQuint);

                y += notification.DrawHeight + Notification.SPACING;
            }

            Height = y;
        }
    }
}
