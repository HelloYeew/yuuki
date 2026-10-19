// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Containers;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class MenuSideFlashes : BeatSyncContainer
{
    /// <summary>
    /// How far each glow reaches inward from its screen edge, in pixels.
    /// </summary>
    private const float box_width = 250f;

    /// <summary>
    /// How quickly the flash ramps up to its target before falling off.
    /// </summary>
    private const double box_fade_in_time = 65;

    /// <summary>
    /// Amplitude below this contributes nothing; above it scales the flash up toward full.
    /// </summary>
    private const float amplitude_dead_zone = 0.75f;

    private const float alpha_multiplier = (1 - amplitude_dead_zone) / 0.55f;

    /// <summary>
    /// Minimum flash strength, so the downbeat is always clearly visible even with no amplitude data.
    /// </summary>
    private const float min_flash = 0.55f;

    /// <summary>
    /// Alpha of the gradient at the bright (edge) end; the transparent end fades to nothing.
    /// </summary>
    private const float gradient_alpha = 0.85f;

    /// <summary>
    /// The color of the glow (blended additively over whatever is behind it).
    /// </summary>
    public Color FlashColor { get; set; } = Color.White;

    private Box leftBox = null!;
    private Box rightBox = null!;

    public MenuSideFlashes()
    {
        // Pre-fire slightly so the quick fade-in peaks right on the audible beat.
        EarlyActivationMilliseconds = box_fade_in_time;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;

        Color bright = FlashColor.WithAlpha(gradient_alpha);
        Color transparent = FlashColor.WithAlpha(0f);

        // Height 1.5x so the glow always covers the screen height
        Add(leftBox = new Box
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.Y,
            Height = 1.5f,
            Width = box_width,
            Alpha = 0,
            Blending = BlendingMode.Additive,
            // Bright at the screen edge, fading toward the center.
            ColorInfo = ColorInfo.GradientHorizontal(bright, transparent)
        });

        Add(rightBox = new Box
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            RelativeSizeAxes = Axes.Y,
            Height = 1.5f,
            Width = box_width,
            Alpha = 0,
            Blending = BlendingMode.Additive,
            // Fading in from the center, bright at the screen edge.
            ColorInfo = ColorInfo.GradientHorizontal(transparent, bright)
        });
    }

    protected override void OnNewBeat(int beat, double beatLength, TimeSignature timeSignature, ChannelAmplitudes amplitudes)
    {
        base.OnNewBeat(beat, beatLength, timeSignature, amplitudes);

        if (beat < 0)
            return;

        if (CurrentBeatInBar(beat) != 0)
            return;

        flash(leftBox, beatLength, amplitudes.AmplitudeLeft);
        flash(rightBox, beatLength, amplitudes.AmplitudeRight);
    }

    private void flash(Box box, double beatLength, float amplitude)
    {
        float target = Math.Clamp(min_flash + (amplitude - amplitude_dead_zone) / alpha_multiplier, min_flash, 1f);

        box.FadeTo(target, box_fade_in_time)
           .Then()
           .FadeOut(beatLength, Easing.InQuad);
    }
}
