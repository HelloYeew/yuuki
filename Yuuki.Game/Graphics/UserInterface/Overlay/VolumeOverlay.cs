// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Configurations;
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
using Sakura.Framework.Reactive;
using Sakura.Framework.Timing;
using Yuuki.Game.Audio;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface.Overlay;

public partial class VolumeOverlay : VisibilityContainer
{
    /// <summary>
    /// How long it stays up after the last change.
    /// </summary>
    public const double STAY_DURATION = 1000;

    private const float card_x = 24;

    private Container card = null!;
    private readonly List<VolumeMeter> meters = new List<VolumeMeter>();
    private VolumeMeter master = null!;
    private ScheduledTask? hideTask;

    [Resolved(canBeNull: true)]
    private InactiveVolumeFader? fader { get; set; }

    /// <summary>
    /// Whether the game is muted (master let through at nothing, the setting kept as it is).
    /// </summary>
    public ReactiveBool Muted { get; } = new ReactiveBool();

    /// <summary>
    /// The ring the keys and Alt+scroll turn.
    /// </summary>
    public VolumeMeter Selected { get; private set; } = null!;

    public IReadOnlyList<VolumeMeter> Meters => meters;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] FrameworkConfigManager? frameworkConfig, [CanBeNull] YuukiConfigManager? config, [CanBeNull] IYuukiColorProvider? colorProvider)
    {
        RelativeSizeAxes = Axes.Both;

        YuukiColorPalette palette = colorProvider.IsNotNull()
            ? colorProvider!.GetPalette(YuukiScreenPart.Play)
            : DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

        Reactive<double> setting(FrameworkSetting key) => frameworkConfig?.Get<double>(key) ?? new Reactive<double>(1);

        master = new VolumeMeter("Master", 120, setting(FrameworkSetting.MasterVolume), palette);
        master.Muted.BindTo(Muted);
        var music = new VolumeMeter("Music", 76, setting(FrameworkSetting.TrackVolume), palette);
        var effects = new VolumeMeter("Effects", 76, setting(FrameworkSetting.SampleVolume), palette);
        var hitsounds = new VolumeMeter("Hitsounds", 76, config?.Get<double>(YuukiSetting.HitsoundVolume) ?? new Reactive<double>(1), palette);

        meters.AddRange(new[] { master, music, effects, hitsounds });

        MuteButton muteButton;

        Add(card = new Container
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            X = card_x,
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 16,
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Color = Color.Black.WithAlpha(0.3f),
                Radius = 12,
                Offset = new Vector2(0, 4)
            },
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = palette.Backdrop,
                    Alpha = 0.88f
                },
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 14),
                    Padding = new MarginPadding(20),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                master,
                                muteButton = new MuteButton(palette)
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.Centre,
                                    // On the ring's lower right, kept inside it so it doesn't widen the card.
                                    Position = new Vector2(-16, 104),
                                    Action = ToggleMute
                                }
                            }
                        },
                        new FlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Horizontal,
                            Spacing = new Vector2(16, 0),
                            Children = new Drawable[] { music, effects, hitsounds }
                        }
                    }
                }
            }
        });

        muteButton.Muted.BindTo(Muted);

        foreach (var meter in meters)
        {
            VolumeMeter picked = meter;
            meter.Hovered = () => select(picked);
            BindValueChanged(meter.Volume, _ => Show());
        }

        Selected = master;
        master.Selected = true;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        BindValueChanged(Muted, e =>
        {
            if (fader != null)
                fader.Muted.Value = e.NewValue;

            Show();
        });
    }

    /// <summary>
    /// Turns the picked ring by <paramref name="amount"/> notches (a scroll's delta, or 1 for a key),
    /// or only brings the control up if it was hidden.
    /// </summary>
    public bool Adjust(double amount)
    {
        if (!IsLoaded)
            return false;

        if (State == Visibility.Hidden)
        {
            Show();
            return true;
        }

        Selected.Adjust(amount);
        Show();
        return true;
    }

    /// <summary>
    /// Picks the next ring (or previous, with <paramref name="direction"/> -1) while it's up.
    /// </summary>
    public bool SelectNext(int direction = 1)
    {
        if (State != Visibility.Visible)
            return false;

        int index = meters.IndexOf(Selected);
        select(meters[((index + direction) % meters.Count + meters.Count) % meters.Count]);
        Show();
        return true;
    }

    public void ToggleMute() => Muted.Value = !Muted.Value;

    private void select(VolumeMeter meter)
    {
        if (meter == Selected)
            return;

        Selected.Selected = false;
        Selected = meter;
        meter.Selected = true;
    }

    public override void Show()
    {
        // Opened fresh, it starts on master.
        if (State == Visibility.Hidden)
            select(master);

        base.Show();
        scheduleHide();
    }

    protected override void PopIn()
    {
        card.ClearTransforms();
        card.X = card_x - 30;
        card.MoveToX(card_x, 350, UserInterfaceConstant.SoftBounce);
        this.FadeIn(120, Easing.OutQuint);
    }

    protected override void PopOut()
    {
        card.MoveToX(card_x - 30, 250, Easing.InCubic);
        this.FadeOut(250, Easing.OutQuint);
    }

    /// <summary>
    /// Hides it <see cref="STAY_DURATION"/> after the last thing done to it, unless it's being pointed at then.
    /// </summary>
    private void scheduleHide()
    {
        if (hideTask != null)
            Scheduler.Cancel(hideTask);

        hideTask = Scheduler.AddDelayed(() =>
        {
            if (card.IsHovered)
                scheduleHide();
            else
                Hide();
        }, STAY_DURATION);
    }

    // Only the card takes the pointer; the rest of the screen stays usable underneath.
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => State == Visibility.Visible && card.ReceivePositionalInputAt(screenSpacePos);

    public override bool OnMouseMove(MouseEvent e)
    {
        // Keep it up while the pointer moves about it.
        if (State == Visibility.Visible)
            scheduleHide();

        return base.OnMouseMove(e);
    }

    public partial class VolumeMeter : Container
    {
        private const double step = 0.01;
        private const double max_acceleration = 5;
        private const double acceleration_multiplier = 1.8;

        public readonly string Label;
        public readonly Reactive<double> Volume;

        public ReactiveBool Muted { get; } = new ReactiveBool();

        public Action? Hovered;

        private readonly YuukiColorPalette palette;
        private readonly float size;

        private Container ringBody = null!;
        private RingArc fill = null!;
        private SpriteText valueText = null!;

        private double displayed = -1;
        private int lastTickValue = -1;
        private double lastTickTime;
        private double acceleration = 1;
        private double lastAdjustTime = double.NegativeInfinity;
        private double accumulated;
        private float dragAccumulated;
        private bool selected;

        [Resolved(canBeNull: true)]
        private UISoundPlayer? uiSounds { get; set; }

        private static readonly Random random = new Random();

        public VolumeMeter(string label, float size, Reactive<double> volume, YuukiColorPalette palette)
        {
            Label = label;
            Volume = volume;
            this.palette = palette;
            this.size = size;
            AutoSizeAxes = Axes.Both;
        }

        /// <summary>
        /// The value on show, which eases after the setting (tests).
        /// </summary>
        public string ShownText => valueText.Text;

        public bool Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;

                selected = value;

                if (!IsLoaded)
                    return;

                updateSelected();

                // Picked by the pointer or the keys alike
                if (selected)
                    uiSounds?.Play(UISound.Hover);
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                ringBody = new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.Centre,
                    Y = size / 2,
                    Size = new Vector2(size),
                    Children = new Drawable[]
                    {
                        new RingArc
                        {
                            RelativeSizeAxes = Axes.Both,
                            InnerRadius = 0.84f,
                            Color = palette.Control
                        },
                        fill = new RingArc
                        {
                            RelativeSizeAxes = Axes.Both,
                            InnerRadius = 0.84f,
                            Progress = 0,
                            Color = palette.TextLight
                        },
                        valueText = new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, (int)(size * 0.26f), FontWeight.Bold),
                            Color = palette.TextLight
                        }
                    }
                },
                new SpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = size + 6,
                    Text = Label.ToUpperInvariant(),
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
                    Color = palette.TextLight.WithAlpha(0.8f)
                }
            };

            updateSelected();
        }

        public override void Update()
        {
            base.Update();

            // The ring and number ease after the setting rather than jumping.
            double target = Volume.Value;
            displayed = displayed < 0 ? target : displayed + (target - displayed) * (1 - Math.Exp(-Clock.ElapsedFrameTime / 40));
            if (Math.Abs(displayed - target) < 0.0005)
                displayed = target;

            fill.Progress = (float)displayed;

            // Muted, it keeps its setting but dims.
            float alpha = Muted.Value ? 0.35f : 1;
            fill.Alpha = alpha;
            valueText.Alpha = alpha;

            int value = (int)Math.Round(displayed * 100);
            valueText.Text = value >= 100 ? "MAX" : value.ToString();

            if (value != lastTickValue)
            {
                if (lastTickValue >= 0)
                    tick(value);

                lastTickValue = value;
            }

            // Repeated turns speed up; a pause starts over.
            if (Clock.CurrentTime - lastAdjustTime > 150)
                acceleration = 1;
        }

        private void tick(int value)
        {
            if (Clock.CurrentTime - lastTickTime <= 30)
                return;

            lastTickTime = Clock.CurrentTime;

            double pitch = 0.99 + random.NextDouble() * 0.02 + value / 100.0 * 0.1;
            if (value is 0 or 100)
                pitch -= 0.5;

            uiSounds?.Play(UISound.SliderTick, pitch);
        }

        /// <summary>
        /// Turns it by <paramref name="amount"/> notches of 1%, faster when turned again quickly.
        /// </summary>
        public void Adjust(double amount)
        {
            if (amount == 0)
                return;

            // A trackpad scrolls in fractions of a notch (the framework doesn't flag precise scrolls, so
            // a whole number is a wheel's or a key's).
            bool precise = amount % 1 != 0;

            lastAdjustTime = Clock.CurrentTime;
            amount *= acceleration;
            acceleration = Math.Min(max_acceleration, acceleration * acceleration_multiplier);

            if (precise)
            {
                // Gathered, a whole 1% at a time.
                accumulated += amount * step;

                while (Math.Abs(accumulated) >= step - 1e-9)
                {
                    set(Volume.Value + Math.Sign(accumulated) * step);
                    accumulated -= Math.Sign(accumulated) * step;
                }

                return;
            }

            set(Volume.Value + Math.Sign(amount) * Math.Max(step, Math.Abs(amount) * step));
        }

        private void set(double value) => Volume.Value = Math.Round(Math.Clamp(value, 0, 1), 2);

        private void updateSelected()
        {
            ringBody.ClearTransforms();
            ringBody.ScaleTo(selected ? 1.06f : 1f, 300, Easing.OutQuint);
            fill.FadeToColor(selected ? palette.AccentLight : palette.TextLight, 200, Easing.OutQuint);
        }

        public override bool OnHover(MouseEvent e)
        {
            Hovered?.Invoke();
            return base.OnHover(e);
        }

        public override bool OnScroll(ScrollEvent e)
        {
            Hovered?.Invoke();
            Adjust(e.ScrollDelta.Y);
            return true;
        }

        public override bool OnMouseDown(MouseButtonEvent e) => true;

        public override bool OnDragStart(MouseButtonEvent e)
        {
            dragAccumulated = 0;
            return true;
        }

        public override bool OnDrag(MouseEvent e)
        {
            // Up turns it up: a 2px drag is a notch.
            dragAccumulated += -e.Delta.Y / 2;

            while (Math.Abs(dragAccumulated) >= 1)
            {
                set(Volume.Value + Math.Sign(dragAccumulated) * step);
                dragAccumulated -= Math.Sign(dragAccumulated);
            }

            return true;
        }
    }

    /// <summary>A round speaker button by the master ring that mutes and unmutes (its <see cref="Muted"/> follows the overlay's).</summary>
    private partial class MuteButton : ClickableContainer
    {
        public ReactiveBool Muted { get; } = new ReactiveBool();

        private readonly YuukiColorPalette palette;
        private IconSprite icon = null!;
        private Box background = null!;

        public MuteButton(YuukiColorPalette palette)
        {
            this.palette = palette;
            Size = new Vector2(30);
            Masking = true;
            CornerRadius = 15;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Color = palette.Control },
                icon = new IconSprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    IconSize = 18,
                    Filled = true,
                    Color = palette.TextLight
                },
                new HoverClickSounds()
            };

            BindValueChanged(Muted, e =>
            {
                icon.Icon = e.NewValue ? IconUsage.VolumeOff : IconUsage.VolumeUp;
                background.FadeToColor(e.NewValue ? palette.AccentLight.Darken(0.4f) : palette.Control, 150, Easing.OutQuint);
            }, true);
        }

        public override bool OnHover(MouseEvent e)
        {
            background.FadeTo(0.8f, 150, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            background.FadeTo(1, 150, Easing.OutQuint);
            return base.OnHoverLost(e);
        }
    }
}

/// <summary>
/// Alt+scroll anywhere turns the volume. A full-screen layer in front of everything that
/// takes a scroll only while Alt is held (and Ctrl, Shift aren't), passing every other one on, and
/// leaving it to anything under the pointer that is an <see cref="IBlockGlobalAltScrollVolume"/>.
/// </summary>
public partial class VolumeScrollCatcher : Container
{
    [Resolved(canBeNull: true)]
    private VolumeOverlay? volume { get; set; }

    public VolumeScrollCatcher()
    {
        RelativeSizeAxes = Axes.Both;
    }

    public override bool OnScroll(ScrollEvent e)
    {
        KeyModifiers modifiers = GetContainingInputManager()?.CurrentState.Modifiers ?? KeyModifiers.None;

        if ((modifiers & KeyModifiers.Alt) == 0 || (modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0 || e.ScrollDelta.Y == 0)
            return false;

        if (GetContainingInputManager()?.HoveredDrawables.Any(d => d is IBlockGlobalAltScrollVolume) == true)
            return false;

        return volume?.Adjust(e.ScrollDelta.Y) ?? false;
    }
}
