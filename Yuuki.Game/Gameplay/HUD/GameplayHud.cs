// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
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
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Gameplay.HUD;

/// <summary>
/// The gameplay overlay
/// </summary>
public partial class GameplayHud : Container
{
    /// <summary>
    /// The name in the character tab, until characters are something the player picks.
    /// </summary>
    public const string DEFAULT_CHARACTER_NAME = "Yuuki";

    private const float health_bar_width = 560;
    private const float health_bar_height = 26;
    private const float progress_bar_height = 12;

    private const float heart_size = 20;

    private const float combo_size = 120;
    private const float combo_label_size = 32;

    /// <summary>
    /// Space between the number and "COMBO", on top of the font's own.
    /// </summary>
    private const float combo_label_gap = 10;

    /// <summary>
    /// How far the combo's soft shadow drops below it, at its full size.
    /// </summary>
    private const float combo_shadow_offset = 5;

    /// <summary>
    /// Beats shorter than this (ms) pulse the heart every other beat, so a fast song doesn't make it flutter.
    /// </summary>
    private const double min_heartbeat_length = 400;

    /// <summary>
    /// Below this health the heartbeat thumps harder, so a run in danger feels it.
    /// </summary>
    private const double low_health = 0.3;

    private static readonly Color text_color = ColorExtensions.FromHex("#F7F1F5");
    private static readonly Color bar_fill = ColorExtensions.FromHex("#F4E8EE");
    private static readonly Color heart_color = JudgementColors.MISS.Lighten(0.2f);

    // The framework has no text shadow, so the combo's is a dark copy drawn under it.
    // TODO: told me to do this in the framework
    private static readonly Color shadow_color = Color.Black.WithAlpha(0.35f);

    private readonly ScoreProcessor score;
    private readonly HealthProcessor health;
    private readonly SkillProcessor skills;
    private readonly Func<double> currentTime;
    private readonly double songLength;

    private YuukiColorPalette palette;

    private Container healthBar = null!;
    private Box healthFill = null!;
    private Box healthFlash = null!;
    private IconSprite heart = null!;
    private int beatsSinceHeartbeat;
    private SpriteText healthText = null!;
    private SpriteText scoreText = null!;
    private SpriteText accuracyText = null!;
    private Container comboAnchor = null!;
    private Container comboContainer = null!;
    private SpriteText comboText = null!;
    private SpriteText comboShadow = null!;
    private Box progressFill = null!;
    private SpriteText elapsedText = null!;
    private SeekTrack seekTrack = null!;

    private Action<double, bool>? seekRequested;

    /// <summary>
    /// Makes the song progress bar a seek bar (a replay's): clicking or dragging it reports the time
    /// under the pointer, with <c>final</c> false while a drag is still moving. Null (live play) leaves
    /// the bar for show only.
    /// </summary>
    public Action<double, bool>? SeekRequested
    {
        get => seekRequested;
        set
        {
            seekRequested = value;
            if (seekTrack.IsNotNull())
                seekTrack.Enabled = value.IsNotNull();
        }
    }
    /// <summary>
    /// The playfield the combo sits beside (at its <see cref="Playfield.ComboPosition"/>), so the combo
    /// follows its size and place. Null puts the combo right of centre on the screen.
    /// </summary>
    public Playfield? ComboSpace { get; set; }

    /// <summary>
    /// The gameplay clock, for the combo: it bumps and fades in game time, so it freezes with the song
    /// on pause, while the rest of the HUD keeps real time. Set before load; null runs it in real time.
    /// </summary>
    public IFrameBasedClock? ComboClock { get; init; }

    private Container skillPill = null!;
    private SpriteText skillText = null!;

    // The values on screen, easing toward the real ones so score and accuracy roll.
    private double displayedScore;
    private double displayedAccuracy = 1;

    /// <param name="score">Score, combo and accuracy.</param>
    /// <param name="health">The health bar's source.</param>
    /// <param name="skills">Running skills, for the countdown pill.</param>
    /// <param name="currentTime">The gameplay time, read each frame for the progress bar.</param>
    /// <param name="songLength">The length the progress bar spans, in ms.</param>
    public GameplayHud(ScoreProcessor score, HealthProcessor health, SkillProcessor skills, Func<double> currentTime, double songLength)
    {
        this.score = score;
        this.health = health;
        this.skills = skills;
        this.currentTime = currentTime;
        this.songLength = Math.Max(1, songLength);

        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider, [CanBeNull] ScreenPalette? screenPalette)
    {
        palette = ScreenPalette.Resolve(screenPalette, colorProvider);

        Children = new Drawable[]
        {
            createCharacterBlock(),
            createScoreBlock(),
            createComboBlock(),
            createProgressBlock()
        };

        // Before the block loads, so everything in it takes this clock.
        if (ComboClock.IsNotNull())
            comboAnchor.Clock = ComboClock;

        health.Health.BindValueChanged(onHealthChanged, true);
        score.Combo.BindValueChanged(onComboChanged, true);
        skills.BreakToCriticalRemaining.BindValueChanged(e =>
        {
            bool running = e.NewValue > 0;
            skillText.Text = $"BREAK -> CRITICAL  {e.NewValue / 1000:0.0}s";

            if (running && skillPill.Alpha <= 0)
            {
                skillPill.Scale = new Vector2(1.3f);
                skillPill.ScaleTo(1f, 300, UserInterfaceConstant.SoftBounce).FadeIn(150);
            }
            else if (!running && skillPill.Alpha > 0)
                skillPill.FadeOut(200);
        }, true);
    }

    #region Layout

    private Drawable createCharacterBlock() => new FlowContainer
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Position = new Vector2(0, 24),
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 12),
        Children = new Drawable[]
        {
            // Name tab, flush with the left edge like the design's sheared tabs: its slanted end runs off
            // the edge. A margin rather than X, which the flow it's in would put back.
            new Container
            {
                Size = new Vector2(190, 46),
                Margin = new MarginPadding { Left = -24 },
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 6,
                        Shear = UserInterfaceConstant.Shear,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Color = palette.Accent
                        }
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = DEFAULT_CHARACTER_NAME,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 30, FontWeight.Bold),
                        Color = palette.Control
                    }
                }
            },
            new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    // The flow places this one; the bar inside it is what shakes when hurt, since the flow
                    // would put a moved child of its own straight back.
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(health_bar_width, health_bar_height),
                        Child = healthBar = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = true,
                                    CornerRadius = health_bar_height / 2,
                                    Children = new Drawable[]
                                    {
                                        new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Color = Color.Black,
                                            Alpha = 0.3f
                                        },
                                        healthFill = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Color = bar_fill
                                        },
                                        healthFlash = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Color = JudgementColors.MISS,
                                            Alpha = 0
                                        }
                                    }
                                },
                                // Outside the bar's mask so its heartbeat can swell past the bar's edge;
                                // centred so it swells in place.
                                heart = new IconSprite
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.Centre,
                                    X = 10 + heart_size / 2,
                                    Icon = IconUsage.Favorite,
                                    Filled = true,
                                    IconSize = heart_size,
                                    Color = heart_color
                                }
                            }
                        }
                    },
                    healthText = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Regular),
                        Color = text_color
                    }
                }
            },
            // Shows while a timed skill runs, with the time it has left.
            skillPill = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = JudgementColors.CRITICAL
                    },
                    skillText = new SpriteText
                    {
                        Margin = new MarginPadding { Left = 12, Right = 12, Top = 3, Bottom = 4 },
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold),
                        Color = Color.White
                    }
                }
            }
        }
    };

    // Both hang off the right edge, the accuracy under the score.
    private Drawable createScoreBlock() => new FlowContainer
    {
        Anchor = Anchor.TopRight,
        Origin = Anchor.TopRight,
        Position = new Vector2(-28, 14),
        AutoSizeAxes = Axes.Both,
        Direction = FlowDirection.Vertical,
        Children = new Drawable[]
        {
            scoreText = new SpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Text = "0000000",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 52, FontWeight.Regular),
                Color = text_color
            },
            accuracyText = new SpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Text = "100.00%",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 24, FontWeight.Regular),
                Color = text_color
            }
        }
    };

    private Drawable createComboBlock() => comboAnchor = new Container
    {
        Origin = Anchor.Centre,
        Size = new Vector2(480, 240),
        Child = comboContainer = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Alpha = 0,
            Children = new Drawable[]
            {
                comboShadow = comboNumber(shadow_color, combo_shadow_offset),
                comboText = comboNumber(text_color, 0),
                // A font size down puts the label's capitals just under the number's baseline (Nunito's
                // metrics); the gap opens them up.
                comboLabel(shadow_color, combo_size + combo_label_gap + combo_shadow_offset * 0.6f),
                comboLabel(text_color, combo_size + combo_label_gap)
            }
        }
    };

    private static SpriteText comboNumber(Color color, float y) => new SpriteText
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
        Y = y,
        Text = "0",
        // Nunito's digits are all one width, so the number doesn't shuffle about its centre as it counts.
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)combo_size, FontWeight.Bold),
        Color = color
    };

    private static SpriteText comboLabel(Color color, float y) => new SpriteText
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
        Y = y,
        Text = "COMBO",
        Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)combo_label_size, FontWeight.Bold),
        Color = color
    };

    private Drawable createProgressBlock()
    {
        elapsedText = timeLabel(Anchor.CentreLeft);

        return new Container
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            Height = 40,
            Y = -20,
            Padding = new MarginPadding { Left = 36, Right = 36 },
            Children = new Drawable[]
            {
                elapsedText,
                // Taller than the bar it holds, so it's easy to hit when it's a seek bar.
                seekTrack = new SeekTrack
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = 70, Right = 70 },
                    Enabled = seekRequested.IsNotNull(),
                    Seek = (fraction, final) => seekRequested?.Invoke(fraction * songLength, final),
                    Child = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.X,
                        Height = progress_bar_height,
                        Masking = true,
                        CornerRadius = progress_bar_height / 2,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.Black,
                                Alpha = 0.3f
                            },
                            progressFill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0,
                                Color = bar_fill
                            }
                        }
                    }
                },
                timeLabel(Anchor.CentreRight, songLength)
            }
        };
    }

    private static SpriteText timeLabel(Anchor anchor, double? time = null) => new SpriteText
    {
        Anchor = anchor,
        Origin = anchor,
        Text = formatTime(time ?? 0),
        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Regular),
        Color = text_color
    };

    private static string formatTime(double ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"mm\:ss");

    #endregion

    /// <summary>Snaps the rolling numbers and the combo back to a fresh run's (on restart).</summary>
    public void ResetDisplay()
    {
        displayedScore = 0;
        displayedAccuracy = 1;

        comboContainer.ClearTransforms();
        comboContainer.Scale = Vector2.One;
        comboContainer.X = 0;
        comboContainer.Alpha = score.Combo.Value > 0 ? 1 : 0;
        comboText.ClearTransforms();
        comboText.Color = text_color;
        setComboText(score.Combo.Value);

        healthBar.ClearTransforms();
        healthBar.X = 0;
        healthFlash.ClearTransforms();
        healthFlash.Alpha = 0;
        heart.ClearTransforms();
        heart.Scale = Vector2.One;
        heart.Color = heart_color;
        beatsSinceHeartbeat = 0;
    }

    /// <summary>Shows the current score and accuracy at once, without rolling up to them (after a replay seek).</summary>
    public void SnapDisplay()
    {
        ResetDisplay();
        displayedScore = score.Score.Value;
        displayedAccuracy = score.Accuracy.Value;
    }

    public override void Update()
    {
        base.Update();

        placeCombo();

        // Roll the numbers toward their targets, quickly enough to keep up with dense streams.
        double blend = 1 - Math.Exp(-Clock.ElapsedFrameTime / 60);
        displayedScore += (score.Score.Value - displayedScore) * blend;
        displayedAccuracy += (score.Accuracy.Value - displayedAccuracy) * blend;

        scoreText.Text = ((long)Math.Round(displayedScore)).ToString("D7");
        accuracyText.Text = $"{displayedAccuracy * 100:0.00}%";

        // While a seek drag is moving, the bar follows the pointer rather than the (catching-up) clock.
        double time = seekTrack.DragFraction is float dragged ? dragged * songLength : currentTime();
        progressFill.Width = (float)Math.Clamp(time / songLength, 0, 1);
        elapsedText.Text = formatTime(time);
    }

    private void onHealthChanged(ValueChangedEvent<double> e)
    {
        healthFill.ResizeTo(new Vector2((float)e.NewValue, 1), 150, Easing.OutQuint);
        healthText.Text = $"{e.NewValue * 100:0}%";

        if (e.NewValue < e.OldValue - 0.001)
        {
            // Hurt: a red flash through the bar and a short shake.
            healthFlash.ClearTransforms();
            healthFlash.Alpha = 0.6f;
            healthFlash.FadeOut(300, Easing.OutQuint);

            healthBar.ClearTransforms();
            healthBar.X = 6;
            healthBar.MoveToX(0, 300, Easing.OutElastic);
        }
    }

    /// <summary>
    /// A heartbeat on the song's beat: a "lub-dub", a strong swell then a softer one. It beats every
    /// other beat on a fast song, and harder (and redder) while health is low.
    /// </summary>
    public void PulseHeart(double beatLength)
    {
        int every = beatLength < min_heartbeat_length ? 2 : 1;
        if (++beatsSinceHeartbeat < every)
            return;

        beatsSinceHeartbeat = 0;

        bool danger = health.Health.Value < low_health;
        float lub = danger ? 1.45f : 1.25f;
        float dub = danger ? 1.25f : 1.12f;
        // The dub follows quickly, but never later than the beat can fit.
        double gap = Math.Min(140, beatLength * every * 0.3);

        heart.ClearTransforms();
        heart.Scale = new Vector2(lub);
        heart.ScaleTo(1f, gap, Easing.OutQuad)
             .Then().ScaleTo(dub, 40, Easing.OutQuad)
             .Then().ScaleTo(1f, 260, Easing.OutQuint);

        heart.Color = danger ? JudgementColors.MISS.Lighten(0.6f) : JudgementColors.MISS.Lighten(0.45f);
        heart.FadeToColor(heart_color, 300, Easing.OutQuint);
    }

    /// <summary>
    /// Puts the combo at the playfield's <see cref="Playfield.ComboPosition"/>, as big as the playfield is
    /// drawn, by mapping that point (and one a unit across) through screen space into the HUD's.
    /// </summary>
    private void placeCombo()
    {
        if (ComboSpace is Playfield playfield && playfield.DrawSize.X > 0 && playfield.DrawSize.Y > 0)
        {
            Vector2 centre = ToLocalSpace(playfield.ToScreenSpace(playfield.ComboPosition));
            Vector2 across = ToLocalSpace(playfield.ToScreenSpace(playfield.ComboPosition + new Vector2(1, 0)));

            comboAnchor.Position = centre;
            comboAnchor.Scale = new Vector2(Vector2.Distance(centre, across));
            return;
        }

        comboAnchor.Position = new Vector2(DrawSize.X * 0.8f, DrawSize.Y * 0.42f);
        comboAnchor.Scale = Vector2.One;
    }

    /// <summary>Where the combo is drawn, in the HUD's space (tests).</summary>
    public Vector2 ComboCentre => comboAnchor.Position;

    /// <summary>How big the combo is drawn, following the playfield's size (tests).</summary>
    public float ComboScale => comboAnchor.Scale.X;

    /// <summary>Whether the combo is up (tests).</summary>
    public bool ComboShown => comboContainer.Alpha > 0;

    /// <summary>The number the combo shows (tests).</summary>
    public string ComboText => comboText.Text;

    private void setComboText(int combo)
    {
        comboText.Text = combo.ToString();
        comboShadow.Text = comboText.Text;
    }

    private void onComboChanged(ValueChangedEvent<int> e)
    {
        comboContainer.ClearTransforms();

        if (e.NewValue > 0)
        {
            setComboText(e.NewValue);
            // Straight back in, even from the middle of a break's shake.
            comboContainer.X = 0;
            comboContainer.Alpha = 1;
        }

        if (e.NewValue > e.OldValue)
        {
            // Each hit bumps the combo; every 50 it lands bigger, with a gold flash.
            comboContainer.Scale = new Vector2(e.NewValue % 50 == 0 ? 1.35f : 1.12f);
            comboContainer.ScaleTo(1f, 220, UserInterfaceConstant.SoftBounce);

            if (e.NewValue % 50 == 0)
            {
                comboText.Color = JudgementColors.CRITICAL;
                comboText.FadeToColor(text_color, 600, Easing.OutQuint);
            }
        }
        else if (e.OldValue > 0 && e.NewValue == 0)
        {
            // Broken: the old number flashes red, shakes and goes, since there's no combo to show
            comboText.Color = JudgementColors.MISS;
            comboText.FadeToColor(text_color, 500, Easing.OutQuint);
            comboContainer.Scale = Vector2.One;
            comboContainer.X = 10;
            comboContainer.MoveToX(0, 400, Easing.OutElastic);
            comboContainer.FadeOut(450, Easing.InQuint);
        }
    }

    /// <summary>The progress bar's hit area, which turns clicks and drags into seeks while enabled.</summary>
    private partial class SeekTrack : Container
    {
        public bool Enabled;

        /// <summary>Called with the fraction (0–1) under the pointer, and whether the gesture has ended.</summary>
        public Action<float, bool>? Seek;

        /// <summary>Where a drag in progress is, as a fraction of the bar; null when not dragging.</summary>
        public float? DragFraction { get; private set; }

        public override bool OnDragStart(MouseButtonEvent e)
        {
            if (!Enabled)
                return false;

            DragFraction = fractionAt(e.ScreenSpaceMousePosition);
            Seek?.Invoke(DragFraction.Value, false);
            return true;
        }

        public override bool OnDrag(MouseEvent e)
        {
            if (DragFraction.IsNull())
                return false;

            DragFraction = fractionAt(e.ScreenSpaceMousePosition);
            Seek?.Invoke(DragFraction.Value, false);
            return true;
        }

        public override bool OnDragEnd(MouseButtonEvent e)
        {
            if (DragFraction.IsNull())
                return false;

            float fraction = fractionAt(e.ScreenSpaceMousePosition);
            DragFraction = null;
            Seek?.Invoke(fraction, true);
            return true;
        }

        private float fractionAt(Vector2 screenPosition)
        {
            float width = DrawSize.X - Padding.Left - Padding.Right;
            if (width <= 0)
                return 0;

            return Math.Clamp((ToLocalSpace(screenPosition).X - Padding.Left) / width, 0, 1);
        }
    }
}
