// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Edit.Timing;

public partial class MetronomeDisplay : Container
{
    private const float swing_angle = 27.5f;
    private const float body_width = 90;
    private const float body_height = 120;
    private const float pivot_height = 23;
    private const float lower_cover = 32;

    // After a long swing home, the latch lands this long after the song stops).
    private const double latch_delay = 380;

    [Resolved]
    private EditorSession session { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    private Container swing = null!;
    private Box stick = null!;
    private Container weight = null!;
    private SpriteText bpmText = null!;
    private EditorColors colors;

    /// <summary>
    /// Whether it runs at all: the Timing tab is showing.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// Whether it makes sound. Off while tapping a tempo, so the taps are heard on their own.
    /// </summary>
    public bool ClickingEnabled { get; set; } = true;

    /// <summary>
    /// Beats ticked since it last started, for tests.
    /// </summary>
    public int TicksPlayed { get; private set; }

    public bool IsSwinging { get; private set; }

    // The timing point and beat (and half-beat, for the swing) last seen, to tell a beat passing from
    // a seek.
    private double lastPointTime = double.NaN;
    private long lastBeat = long.MinValue;
    private long lastSwing = long.MinValue;
    private double latchAt = double.NaN;
    private string? shownBpm;
    private float weightTarget = float.NaN;

    [BackgroundDependencyLoader]
    private void load([CanBeNull] IYuukiColorProvider? colorProvider)
    {
        colors = EditorColors.From(colorProvider);

        Size = new Vector2(160, body_height + 34);

        Children = new Drawable[]
        {
            // The body, the centre mark behind the pendulum, the pendulum, then the cover over its foot.
            new Triangle
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Size = new Vector2(body_width, body_height),
                Color = colors.Control
            },
            new Box
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.BottomCentre,
                Y = body_height - lower_cover - 3,
                Size = new Vector2(2, body_height * 0.55f),
                Color = Color.Black.WithAlpha(0.3f)
            },
            swing = new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.BottomCentre,
                Y = body_height - pivot_height,
                Size = new Vector2(14, (body_height - pivot_height) * 0.95f),
                Children = new Drawable[]
                {
                    stick = new Box
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        RelativeSizeAxes = Axes.Y,
                        Size = new Vector2(4, 1),
                        Color = colors.Accent
                    },
                    weight = new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(14, 10),
                        Masking = true,
                        CornerRadius = 3,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = colors.AccentStrong }
                    }
                }
            },
            new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = body_height - lower_cover,
                Size = new Vector2(body_width * lower_cover / body_height + 46, lower_cover),
                Masking = true,
                CornerRadius = 4,
                Child = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = colors.Panel.Lighten(0.1f) }
            },
            new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.Centre,
                Y = body_height - pivot_height,
                Size = new Vector2(8),
                Masking = true,
                CornerRadius = 4,
                Child = new Box { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Color = Color.White }
            },
            bpmText = new SpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = body_height + 8,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold),
                Color = Color.White
            }
        };
    }

    public override void Update()
    {
        base.Update();

        var timing = session.Beatmap.ControlPoints;
        var clock = session.Clock;

        if (!timing.HasTiming)
        {
            showBpm("No timing");
            settle();
            return;
        }

        double now = clock.CurrentTime;
        TimingPoint point = timing.TimingPointAt(now);

        // ctrl to half the beat
        var keys = GetContainingInputManager()?.CurrentState;
        int divisor = keys?.IsPressed(Key.ControlLeft) == true || keys?.IsPressed(Key.ControlRight) == true ? 2 : 1;
        double beatLength = point.BeatLength / divisor;

        showBpm($"{point.Bpm * divisor:0.##} BPM");
        placeWeight(point.Bpm * divisor);

        long beat = (long)Math.Floor((now - point.Time) / beatLength + 1e-6);

        // The pendulum crosses the middle on the beat, so it sets off half a beat ahead of it.
        long swingBeat = (long)Math.Floor((now - point.Time) / beatLength + 0.5 + 1e-6);

        bool samePoint = point.Time.Equals(lastPointTime);

        if (Active && clock.IsRunning && !clock.IsSeeking)
        {
            latchAt = double.NaN;

            // Only a beat that has just gone by, not one jumped to by a seek.
            if (samePoint && beat == lastBeat + 1)
                tick(beat, divisor, point);

            if (!samePoint || swingBeat != lastSwing)
                swingTo(swingBeat, beatLength / Math.Max(0.05, clock.Rate));
        }
        else
        {
            settle();
        }

        lastPointTime = point.Time;
        lastBeat = beat;
        lastSwing = swingBeat;

        if (!double.IsNaN(latchAt) && Clock.CurrentTime >= latchAt)
        {
            latchAt = double.NaN;
            playLatch();
        }
    }

    private void tick(long beat, int divisor, TimingPoint point)
    {
        // A half beat is never the bar's first.
        bool downbeat = beat % divisor == 0 && ControlPointInfo.IsDownbeat(point, beat / divisor);

        TicksPlayed++;
        stick.FlashColor(Color.White, point.BeatLength / divisor, Easing.OutQuint);

        if (ClickingEnabled)
            uiSounds?.Play(downbeat ? UISound.MetronomeTickDownbeat : UISound.MetronomeTick);
    }

    private void swingTo(long swingBeat, double duration)
    {
        IsSwinging = true;

        swing.ClearTransforms();
        swing.RotateTo(swingBeat % 2 == 0 ? swing_angle : -swing_angle, duration, Easing.InOutQuad);
    }

    private void settle()
    {
        if (!IsSwinging)
            return;

        IsSwinging = false;
        swing.ClearTransforms();

        if (Math.Abs(swing.Rotation) < 1)
        {
            swing.RotateTo(0, 60, Easing.OutQuint);
            playLatch();
        }
        else
        {
            swing.RotateTo(0, 1000, Easing.OutQuint);
            latchAt = Clock.CurrentTime + latch_delay;
        }
    }

    private void playLatch()
    {
        if (ClickingEnabled)
            uiSounds?.Play(UISound.MetronomeLatch);
    }

    private void showBpm(string text)
    {
        if (text == shownBpm)
            return;

        shownBpm = text;
        bpmText.Text = text;
    }

    // the weight slides down the stick as the tempo rises, as on a real metronome.
    private void placeWeight(double bpm)
    {
        float ratio = (float)Math.Clamp((bpm - 30) / 480, 0, 1);
        ratio = 1 - (1 - ratio) * (1 - ratio);

        float target = (0.1f + (0.83f - 0.1f) * ratio) * swing.DrawHeight;

        if (Math.Abs(target - weightTarget) < 0.5f)
            return;

        weightTarget = target;
        weight.ClearTransforms();
        weight.MoveToY(target, 600, Easing.OutQuint);
    }
}

/// <summary>
/// The metronome in its panel, with what Ctrl does.
/// </summary>
public partial class MetronomePanel : EditorPanel
{
    public MetronomeDisplay Metronome { get; }

    public MetronomePanel()
        : base("Metronome")
    {
        AddRow(new Container
        {
            Height = 160,
            Child = Metronome = new MetronomeDisplay
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre
            }
        });

        AddRow(new SpriteText
        {
            Text = "Plays with the song. Hold Ctrl for half beats.",
            Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 13, FontWeight.Bold),
            Color = Color.White.WithAlpha(0.5f)
        });
    }
}
