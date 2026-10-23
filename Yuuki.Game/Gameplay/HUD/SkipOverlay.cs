// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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
using Yuuki.Game.Audio;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;
using FontWeights = Sakura.Framework.Graphics.Text.FontWeights;

namespace Yuuki.Game.Gameplay.HUD;

/// <summary>
/// skip overlay
/// </summary>
public partial class SkipOverlay : Container
{
    private const float band_height = 140;
    private const float button_size = 84;
    private const float bar_height = 7;

    private const double fade_time = 300;

    /// <summary>The bar's catch-up half-life, in ms: each beat's step eases in over about a tenth of a second.</summary>
    private const double bar_half_life = 40;

    private readonly double startTime;
    private readonly double skipTime;
    private readonly double beatLength;
    private readonly Func<double> currentTime;

    private SkipButton button = null!;
    private Container bar = null!;
    private Box barFlash = null!;

    private bool shown;
    private double lastRemaining = double.NaN;

    /// <summary>Called when the player asks to skip, by clicking the band or <see cref="TriggerSkip"/>.</summary>
    public Action? RequestSkip;

    /// <summary>Whether play is running. While it isn't (paused, failing, cleared) the overlay hides and can't skip.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Whether a skip is possible right now: play is running and the skip time is still ahead.</summary>
    public bool CanSkip => Active && currentTime() < skipTime;

    /// <summary>How much of the count-down is left, 1 down to 0: what the bar is heading to.</summary>
    public double Remaining => remainingFraction(currentTime());

    /// <param name="startTime">The gameplay time the count-down starts from (the run's start); the bar is full here.</param>
    /// <param name="skipTime">Where a skip lands, and when the bar runs out. Must sit on the beat grid when the tempo is known.</param>
    /// <param name="beatLength">The song's beat, in ms; 0 when the tempo is unknown.</param>
    /// <param name="currentTime">The gameplay time, read each frame.</param>
    public SkipOverlay(double startTime, double skipTime, double beatLength, Func<double> currentTime)
    {
        this.startTime = startTime;
        this.skipTime = skipTime;
        this.beatLength = beatLength;
        this.currentTime = currentTime;

        RelativePositionAxes = Axes.Both;
        RelativeSizeAxes = Axes.X;
        Position = new Vector2(0.5f, 0.7f);
        Height = band_height;
        Origin = Anchor.Centre;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        YuukiColorPalette palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        Children = new Drawable[]
        {
            button = new SkipButton(palette.Accent, palette.Control)
            {
                Action = () => RequestSkip?.Invoke()
            },
            bar = new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                RelativeSizeAxes = Axes.X,
                Height = bar_height,
                Masking = true,
                CornerRadius = bar_height / 2,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = palette.Accent
                    },
                    barFlash = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.White,
                        Alpha = 0
                    }
                }
            }
        };
    }

    /// <summary>Asks for the skip as a click on the button would, if one is possible. Returns whether it skipped.</summary>
    public bool TriggerSkip()
    {
        if (!CanSkip)
            return false;

        button.Confirm();
        RequestSkip?.Invoke();
        return true;
    }

    /// <summary>Back to a fresh run's look: a full bar and a resting button (on restart).</summary>
    public void Reset()
    {
        lastRemaining = double.NaN;
        bar.Width = 1;
        barFlash.ClearTransforms();
        barFlash.Alpha = 0;
        button.Reset();
    }

    public override void Update()
    {
        base.Update();

        double time = currentTime();
        double remaining = Remaining;
        bool show = Active && time < skipTime;

        button.Enabled.Value = show;

        if (show != shown)
        {
            shown = show;
            ClearTransforms();

            if (show)
                this.FadeIn(fade_time);
            else
                this.FadeOut(fade_time);
        }

        // A beat has gone by: the bar has just stepped down, so it flashes with the step.
        if (beatLength > 0 && remaining < lastRemaining && remaining > 0)
        {
            barFlash.ClearTransforms();
            barFlash.Alpha = 0.6f;
            barFlash.FadeOut(Math.Min(beatLength * 0.8, 400), Easing.OutQuint);
        }

        lastRemaining = remaining;

        double blend = 1 - Math.Pow(0.5, Math.Abs(Clock.ElapsedFrameTime) / bar_half_life);
        bar.Width += (float)((remaining - bar.Width) * blend);
    }

    /// <summary>
    /// How much of the bar is left at <paramref name="time"/>, 1 at the start down to 0 at the skip
    /// time. With a tempo it counts whole beats, so it drops by the same step on every beat.
    /// </summary>
    private double remainingFraction(double time)
    {
        if (time >= skipTime)
            return 0;

        if (beatLength <= 0)
            return Math.Clamp((skipTime - time) / (skipTime - startTime), 0, 1);

        return Math.Clamp(beatsLeft(time) / Math.Max(1, beatsLeft(startTime)), 0, 1);
    }

    // The beats still to come before the skip time, counting the one it lands on. The tolerance keeps
    // rounding error from counting a beat twice when the time sits exactly on one.
    private double beatsLeft(double time) => Math.Ceiling((skipTime - time) / beatLength - 1e-6);

    private partial class SkipButton : ClickableContainer
    {
        private const float chevron_step = 11;
        private const float chevron_spread = 7;

        private readonly Color normalColor;
        private readonly Color hoverColor;

        private readonly Box background;
        private readonly Container square;
        private readonly Box squareFill;
        private readonly IconSprite[] chevrons = new IconSprite[3];

        [Resolved(canBeNull: true)]
        private UISoundPlayer? sounds { get; set; }

        public SkipButton(Color color, Color contentColor)
        {
            normalColor = color;
            hoverColor = color.Lighten(0.2f);

            RelativeSizeAxes = Axes.Both;

            for (int i = 0; i < chevrons.Length; i++)
            {
                chevrons[i] = new IconSprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    X = (i - 1) * chevron_step,
                    Icon = IconUsage.ChevronRight,
                    IconSize = 24,
                    IconWeight = FontWeights.Bold,
                    Color = contentColor
                };
            }

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Black,
                    Alpha = 0.2f
                },
                square = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(button_size),
                    Masking = true,
                    CornerRadius = 21,
                    Children = new Drawable[]
                    {
                        squareFill = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Color = normalColor
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            ColorInfo = ColorInfo.GradientVertical(Color.White.WithAlpha(0.25f), Color.White.WithAlpha(0f))
                        },
                        new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.Centre,
                            RelativePositionAxes = Axes.Y,
                            Y = 0.4f,
                            Size = new Vector2(button_size, 30),
                            Children = chevrons
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.Centre,
                            RelativePositionAxes = Axes.Y,
                            Y = 0.7f,
                            Text = "SKIP",
                            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 17, FontWeight.Bold),
                            Color = contentColor
                        }
                    }
                }
            };
        }

        // The lanes behind the band take the fingers that land on them before a click could come of one, so the
        // button takes its own: pressed as the finger lands, skipping as it lifts on the band.
        private TouchSource? finger;

        public override bool OnTouchDown(TouchEvent e)
        {
            if (!Enabled.Value || finger != null)
                return false;

            finger = e.Source;
            square.ClearTransforms();
            square.ScaleTo(0.75f, 2000, Easing.OutQuint);
            return true;
        }

        public override bool OnTouchUp(TouchEvent e)
        {
            if (finger != e.Source)
                return false;

            finger = null;
            square.ClearTransforms();
            square.ScaleTo(1f, 1000, Easing.OutElastic);

            if (!e.Cancelled && Enabled.Value && Contains(e.ScreenSpacePosition))
            {
                Confirm();
                Action?.Invoke();
            }

            return true;
        }

        /// <summary>The press feedback: the confirm sound, a white flash and the square swelling.</summary>
        public void Confirm()
        {
            sounds?.Play(UISound.Select);

            squareFill.FlashColor(Color.White, 500, Easing.OutQuint);
            square.ClearTransforms();
            square.ScaleTo(1.2f, 2000, Easing.OutQuint);
        }

        public void Reset()
        {
            square.ClearTransforms();
            square.Scale = Vector2.One;
            squareFill.ClearTransforms();
            squareFill.Color = normalColor;
        }

        public override bool OnHover(MouseEvent e)
        {
            spreadChevrons(chevron_spread);
            squareFill.FadeToColor(hoverColor, 500, Easing.OutQuint);
            background.FadeTo(0.4f, 500, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            spreadChevrons(0);
            squareFill.FadeToColor(normalColor, 500, Easing.OutQuint);
            background.FadeTo(0.2f, 500, Easing.OutQuint);
            return base.OnHoverLost(e);
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            square.ClearTransforms();
            square.ScaleTo(0.75f, 2000, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        public override bool OnMouseUp(MouseButtonEvent e)
        {
            square.ClearTransforms();
            square.ScaleTo(1f, 1000, Easing.OutElastic);
            return base.OnMouseUp(e);
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            if (!Enabled.Value)
                return false;

            Confirm();
            return base.OnClick(e);
        }

        private void spreadChevrons(float spread)
        {
            for (int i = 0; i < chevrons.Length; i++)
            {
                chevrons[i].ClearTransforms();
                chevrons[i].MoveToX((i - 1) * (chevron_step + spread), 500, Easing.OutQuint);
            }
        }
    }
}
