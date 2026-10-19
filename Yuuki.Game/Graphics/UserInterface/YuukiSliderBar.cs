// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Numerics;
using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Yuuki.Game.Audio;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// The game's setting slider
/// </summary>
public partial class YuukiSliderBar<T> : SliderBar<T> where T : struct, INumber<T>, IMinMaxValue<T>
{
    private const float track_height = 8;
    private const float knob_size = 20;

    private readonly Box fill;
    private readonly Box zeroMark;
    private readonly Circle knob;

    private bool hovered;
    private bool focused;

    public YuukiSliderBar(Color fillColor, Color trackColor)
    {
        RelativeSizeAxes = Axes.X;
        Height = 28;

        Children = new Drawable[]
        {
            new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                Height = track_height,
                Masking = true,
                CornerRadius = track_height / 2,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = trackColor
                    },
                    fill = new Box
                    {
                        RelativePositionAxes = Axes.X,
                        RelativeSizeAxes = Axes.Both,
                        Width = 0,
                        Color = fillColor
                    }
                }
            },
            zeroMark = new Box
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.X,
                Size = new Vector2(2, track_height + 8),
                Color = trackColor,
                Alpha = 0
            },
            knob = new Circle
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.X,
                Size = new Vector2(knob_size),
                Color = fillColor
            }
        };
    }

    /// <summary>
    /// Where zero sits along the track (0–1), or the left edge when the range doesn't span it.
    /// </summary>
    private float zeroFraction
    {
        get
        {
            float min = float.CreateTruncating(MinValue);
            float max = float.CreateTruncating(MaxValue);
            return min < 0 && max > 0 ? -min / (max - min) : 0;
        }
    }

    [Resolved(canBeNull: true)]
    private UISoundPlayer? uiSounds { get; set; }

    // Off until loaded: the first value comes from the setting, not a drag.
    private bool soundsArmed;

    protected override void LoadComplete()
    {
        base.LoadComplete();
        soundsArmed = true;

        zeroMark.X = zeroFraction;
        zeroMark.Alpha = zeroFraction > 0 ? 1 : 0;
    }

    protected override void OnValueChanged(T value)
    {
        float fraction = GetFillFraction();

        // A notch, higher up the bar the further along it is, and pitched down at either en.
        if (soundsArmed)
            uiSounds?.Play(UISound.SliderTick, fraction <= 0 || fraction >= 1 ? 0.7 : 1 + fraction * 0.2);
        float zero = zeroFraction;

        fill.X = Math.Min(zero, fraction);
        fill.Width = Math.Abs(fraction - zero);
        knob.X = fraction;
    }

    protected override void OnHovered()
    {
        uiSounds?.Play(UISound.Hover);
        hovered = true;
        updateKnob();
    }

    protected override void OnHoverLost()
    {
        uiSounds?.Play(UISound.HoverLost);
        hovered = false;
        updateKnob();
    }

    protected override void OnFocusGained()
    {
        focused = true;
        updateKnob();
    }

    protected override void OnFocusLost()
    {
        focused = false;
        updateKnob();
    }

    private void updateKnob() => knob.ScaleTo(hovered || focused ? 1.3f : 1f, 150, Easing.OutQuint);
}
