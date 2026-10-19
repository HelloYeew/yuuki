// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Drawables;

namespace Yuuki.Game.Audio;

/// <summary>
/// The interface's sounds (<c>Samples/UI/</c>)
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public enum UISound
{
    Hover,
    HoverLost,
    Click,
    Select,
    Back,
    ToggleOn,
    ToggleOff,
    DropdownOpen,
    DropdownClose,
    SliderTick,
    TabSelect,
    OverlayOpen,
    OverlayClose,
    ScreenForward,
    ScreenBack,
    Denied,
    Type,
    TypeDelete,
    CursorTap,
    NotificationDefault,
    NotificationDone,
    NotificationCancel,
    NotificationError,
    DialogDangerousTick,
    DialogDangerousSelect,
    MetronomeTick,
    MetronomeTickDownbeat,
    MetronomeLatch
}

/// <summary>
/// Plays the interface's sounds, cached game-wide so every control can resolve it with debounce support
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class UISoundPlayer : Component
{
    [Resolved(canBeNull: true)]
    private IAudioStore<ISample>? sampleStore { get; set; }

    private readonly Dictionary<UISound, ISample?> samples = new Dictionary<UISound, ISample?>();
    private readonly Dictionary<UISound, double> lastPlayed = new Dictionary<UISound, double>();
    private readonly Random random = new Random();

    /// <summary>
    /// Every sound that plays (after debouncing), with its pitch. For testing
    /// </summary>
    public event Action<UISound, double>? Played;

    [BackgroundDependencyLoader]
    private void load()
    {
        if (sampleStore.IsNull())
            return;

        // loaded up front, so the first hover doesn't wait on a disk read.
        foreach (UISound sound in Enum.GetValues<UISound>())
            samples[sound] = sampleStore!.Get($"UI/{fileName(sound)}.wav");
    }

    /// <summary>
    /// Plays <paramref name="sound"/>, unless it played too recently. <paramref name="pitch"/> scales
    /// its frequency (a slider notch follows the value); <paramref name="volume"/> its loudness.
    /// </summary>
    public void Play(UISound sound, double pitch = 1, double volume = 1)
    {
        double now = Clock.CurrentTime;

        // too soon after the same sound. A clock that went back (a new one, as in a fresh test scene)
        // isn't "too soon": that would mute the sound until the clock caught up.
        if (lastPlayed.TryGetValue(sound, out double last) && now >= last && now - last < debounceFor(sound))
            return;

        lastPlayed[sound] = now;

        double spread = pitchSpreadFor(sound);
        double frequency = pitch * (1 - spread / 2 + random.NextDouble() * spread);

        Played?.Invoke(sound, frequency);

        if (!samples.TryGetValue(sound, out ISample? sample) || sample == null)
            return;

        // Backends hand back null when they're out of voices; a skipped hover is fine.
        IAudioChannel channel = sample.GetChannel();
        if (channel.IsNull())
            return;

        channel.AutoDispose = true;
        channel.Frequency.Value = frequency;
        channel.Volume.Value = volume;
        channel.Play();
    }

    /// <summary>
    /// Forgets when each sound last played, so the next of each play whatever the timing (for a test
    /// starting from silence; in the game the player lives as long as the session's clock).
    /// </summary>
    public void ForgetRecent() => lastPlayed.Clear();

    /// <summary>
    /// How soon the same sound may play again, in ms
    /// </summary>
    private static double debounceFor(UISound sound) => sound switch
    {
        UISound.Hover or UISound.HoverLost => 50,
        UISound.SliderTick => 30,
        UISound.DialogDangerousTick => 40,
        UISound.Type or UISound.TypeDelete => 15,
        _ => 20
    };

    /// <summary>
    /// The random pitch range for sounds that repeat a lot.
    /// </summary>
    private static double pitchSpreadFor(UISound sound) => sound switch
    {
        UISound.Hover or UISound.HoverLost => 0.06,
        UISound.SliderTick => 0.02,
        UISound.Type or UISound.TypeDelete => 0.12,
        UISound.CursorTap => 0.04,
        UISound.MetronomeTick or UISound.MetronomeTickDownbeat => 0.02,
        _ => 0
    };

    private static string fileName(UISound sound) => sound switch
    {
        UISound.Hover => "hover",
        UISound.HoverLost => "hover-lost",
        UISound.Click => "click",
        UISound.Select => "select",
        UISound.Back => "back",
        UISound.ToggleOn => "toggle-on",
        UISound.ToggleOff => "toggle-off",
        UISound.DropdownOpen => "dropdown-open",
        UISound.DropdownClose => "dropdown-close",
        UISound.SliderTick => "slider-tick",
        UISound.TabSelect => "tab-select",
        UISound.OverlayOpen => "overlay-open",
        UISound.OverlayClose => "overlay-close",
        UISound.ScreenForward => "screen-forward",
        UISound.NotificationDefault => "notification-default",
        UISound.NotificationDone => "notification-done",
        UISound.NotificationCancel => "notification-cancel",
        UISound.NotificationError => "notification-error",
        UISound.DialogDangerousTick => "dialog-dangerous-tick",
        UISound.DialogDangerousSelect => "dialog-dangerous-select",
        UISound.MetronomeTick => "metronome-tick",
        UISound.MetronomeTickDownbeat => "metronome-tick-downbeat",
        UISound.MetronomeLatch => "metronome-latch",
        UISound.ScreenBack => "screen-back",
        UISound.Denied => "denied",
        UISound.Type => "type",
        UISound.TypeDelete => "type-delete",
        UISound.CursorTap => "cursor-tap",
        _ => throw new ArgumentOutOfRangeException(nameof(sound), sound, null)
    };
}
