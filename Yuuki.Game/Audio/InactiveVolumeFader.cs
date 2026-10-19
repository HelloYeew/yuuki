// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Configurations;

namespace Yuuki.Game.Audio;

/// <summary>
/// Turns the game down while its window isn't the active one
/// </summary>
public partial class InactiveVolumeFader : Component
{
    public const double FADE_OUT_DURATION = 4000;
    public const double FADE_IN_DURATION = 400;

    [Resolved(canBeNull: true)]
    private IAudioManager? audio { get; set; }

    [Resolved(canBeNull: true)]
    private IWindow? window { get; set; }

    [Resolved(canBeNull: true)]
    private FrameworkConfigManager? frameworkConfig { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    private Reactive<double>? masterVolume;
    private Reactive<double>? inactiveVolume;

    private bool active = true;
    private double fadeFrom = 1;
    private double fadeTo = 1;
    private double fadeStart;
    private double fadeDuration;

    /// <summary>
    /// Whether the window counts as active, can override by test
    /// </summary>
    public Func<bool>? IsWindowActive { get; set; }

    /// <summary>
    /// The share of the master volume being let through right now, 1 while active.
    /// </summary>
    public double Fade { get; private set; } = 1;

    /// <summary>
    /// Whether the game is muted
    /// </summary>
    public readonly ReactiveBool Muted = new ReactiveBool();

    [BackgroundDependencyLoader]
    private void load()
    {
        masterVolume = frameworkConfig?.Get<double>(FrameworkSetting.MasterVolume);
        inactiveVolume = config?.Get<double>(YuukiSetting.InactiveVolume);
    }

    public override void Update()
    {
        base.Update();

        bool nowActive = IsWindowActive?.Invoke() ?? window?.IsActive ?? true;
        double now = Clock.CurrentTime;

        if (nowActive != active)
        {
            active = nowActive;
            fadeFrom = Fade;
            fadeStart = now;
            fadeDuration = active ? FADE_IN_DURATION : FADE_OUT_DURATION;
        }

        // while away, the setting can still change (or come in late from the config), just follow it.
        fadeTo = active ? 1 : Math.Clamp(inactiveVolume?.Value ?? 0.25, 0, 1);

        double progress = fadeDuration > 0 ? Math.Clamp((now - fadeStart) / fadeDuration, 0, 1) : 1;
        double eased = 1 - Math.Pow(1 - progress, 5);
        Fade = fadeFrom + (fadeTo - fadeFrom) * eased;

        apply();
    }

    protected override void Dispose(bool isDisposing)
    {
        // gone, so nothing is turned down anymore
        if (audio != null && masterVolume != null)
            audio.MasterVolume.Value = masterVolume.Value;

        base.Dispose(isDisposing);
    }

    private void apply()
    {
        if (audio == null || masterVolume == null)
            return;

        double target = Muted.Value ? 0 : masterVolume.Value * Fade;

        if (Math.Abs(audio.MasterVolume.Value - target) > 1e-4)
            audio.MasterVolume.Value = target;
    }
}
