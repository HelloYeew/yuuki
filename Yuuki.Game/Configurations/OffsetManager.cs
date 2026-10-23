// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Database;
using Yuuki.Game.Edit;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Configurations;

/// <summary>
/// Keeps an audio offset for each audio device and a visual offset for each display, and the settings' offsets
/// (<see cref="YuukiSetting.AudioOffset"/>, <see cref="YuukiSetting.VisualOffset"/>) those of the ones in use; and
/// each difficulty's song offset.
/// </summary>
/// <remarks>
/// The settings' offsets stay what everything plays by, and what the sliders, calibrating and the results screen
/// change. What they're changed to is kept for the devices in use, and they're put back as the devices change: each
/// offset covers its own hardware's delay, the audio offset the sound's way out and the visual offset the picture's.
/// </remarks>
public partial class OffsetManager : Container
{
    public const int MIN_SONG_OFFSET = -100;
    public const int MAX_SONG_OFFSET = 100;

    // How long after a change the offsets are saved, so dragging a slider writes once rather than every step.
    private const double save_delay = 1000;

    // What's explained once (OffsetData.Explained), the first time it happens.
    private const string song_offset_hint = "song offset";
    private const string shift_hint = "shift timing";

    private readonly OffsetStore store;

    [Resolved(canBeNull: true)]
    private IAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private IWindow? window { get; set; }

    [Resolved(canBeNull: true)]
    private NotificationOverlay? notifications { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiScreenStack? screenStack { get; set; }

    private Reactive<int> audioOffset = null!;
    private Reactive<int> visualOffset = null!;

    // Set while this writes the settings, so it doesn't take its own writes for the player's.
    private bool applying;

    private bool unsaved;
    private double sinceChange;

    // Explanations waiting for the song to end, not to pop up over it.
    private readonly Queue<Notification> heldBack = new Queue<Notification>();

    /// <summary>
    /// The audio device sound plays on, as Sakura names it; empty where nothing names it.
    /// </summary>
    public string AudioDevice { get; private set; } = string.Empty;

    /// <summary>
    /// The display the window is on, as Sakura names it; empty where nothing names it.
    /// </summary>
    public string Display { get; private set; } = string.Empty;

    /// <summary>
    /// Raised when <see cref="AudioDevice"/> or <see cref="Display"/> changes, or anything kept for one does.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised with a difficulty's ID when its song offset changes.
    /// </summary>
    public event Action<int>? SongOffsetChanged;

    private OffsetData data => store.Data;

    public OffsetManager(OffsetStore store)
    {
        this.store = store;
    }

    [BackgroundDependencyLoader]
    private void load(YuukiConfigManager config)
    {
        audioOffset = config.Get<int>(YuukiSetting.AudioOffset);
        visualOffset = config.Get<int>(YuukiSetting.VisualOffset);

        AudioDevice = CurrentAudioDevice;
        Display = CurrentDisplay;

        if (!store.Loaded)
        {
            // The first time: the offsets the settings had are the devices' in use now, and where new ones start.
            data.StartAudioDevice = AudioDevice;
            data.StartDisplay = Display;
            data.AudioDevices[AudioDevice] = new DeviceOffset { Offset = audioOffset.Value };
            data.Displays[Display] = new DeviceOffset { Offset = visualOffset.Value };
            data.SharedAudio.Offset = audioOffset.Value;
            data.SharedDisplay.Offset = visualOffset.Value;
        }

        use(data.AudioDevices, AudioDevice, data.SharedAudio, data.StartAudioDevice);
        use(data.Displays, Display, data.SharedDisplay, data.StartDisplay);
        apply();
        save();

        BindValueChanged(audioOffset, e =>
        {
            if (applying)
                return;

            audioEntry.Offset = e.NewValue;
            changed();
        });

        BindValueChanged(visualOffset, e =>
        {
            if (applying)
                return;

            displayEntry.Offset = e.NewValue;
            changed();
        });
    }

    /// <summary>
    /// The audio device sound plays on now, read every frame.
    /// </summary>
    protected virtual string CurrentAudioDevice => audioManager?.PlayingDevice ?? string.Empty;

    /// <summary>
    /// The display the window is on now, read every frame.
    /// </summary>
    protected virtual string CurrentDisplay => window?.DisplayName.Value ?? string.Empty;

    public override void Update()
    {
        base.Update();

        string audio = CurrentAudioDevice;

        if (audio != AudioDevice)
        {
            AudioDevice = audio;
            bool isNew = use(data.AudioDevices, audio, data.SharedAudio, data.StartAudioDevice);
            apply();
            changed();
            announce(isNew, IconUsage.Headphones,
                $"New audio device: {AudioDeviceLabel(audio)}. Its audio offset starts at {Signed(audioOffset.Value)}; click to calibrate for it.",
                $"Playing on {AudioDeviceLabel(audio)}, with its audio offset ({Signed(audioOffset.Value)}).");
        }

        string display = CurrentDisplay;

        if (display != Display)
        {
            Display = display;
            bool isNew = use(data.Displays, display, data.SharedDisplay, data.StartDisplay);
            apply();
            changed();
            announce(isNew, IconUsage.Monitor,
                $"New display: {DisplayLabel(display)}. Its visual offset starts at {Signed(visualOffset.Value)}; click to calibrate for it.",
                $"Now on {DisplayLabel(display)}, with its visual offset ({Signed(visualOffset.Value)}).");
        }

        if (heldBack.Count > 0 && notifications != null && !inSong)
        {
            while (heldBack.TryDequeue(out Notification? hint))
                notifications.Post(hint);
        }

        if (unsaved && (sinceChange += Clock.ElapsedFrameTime) >= save_delay)
            save();
    }

    #region Devices

    /// <summary>
    /// Whether each audio device and display keeps its own offset. Turned off, the ones in use now are kept for all.
    /// </summary>
    public bool PerDevice
    {
        get => data.PerDevice;
        set
        {
            if (value == data.PerDevice)
                return;

            if (!value)
            {
                data.SharedAudio.Offset = audioEntry.Offset;
                data.SharedDisplay.Offset = displayEntry.Offset;
            }

            data.PerDevice = value;
            apply();
            changed();
        }
    }

    /// <summary>
    /// Moves the judging <paramref name="shift"/> ms later on every device (earlier where it's negative), the notes
    /// staying where they're seen: every audio offset goes down by it and every visual offset up, as a lean on every song
    /// is the player's own, or their keys', and goes wherever they play. Moved less where either offset in use would
    /// pass its limit.
    /// </summary>
    /// <returns>How far it moved.</returns>
    public int ShiftTiming(int shift)
    {
        int furthestLater = Math.Min(audioOffset.Value - YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_VISUAL_OFFSET - visualOffset.Value);
        int furthestEarlier = Math.Max(audioOffset.Value - YuukiConfigManager.MAX_AUDIO_OFFSET, YuukiConfigManager.MIN_VISUAL_OFFSET - visualOffset.Value);
        shift = Math.Clamp(shift, Math.Min(furthestEarlier, 0), Math.Max(furthestLater, 0));

        if (shift == 0)
            return 0;

        // One that goes by another device's offset moves with that one.
        foreach (DeviceOffset entry in data.AudioDevices.Values)
        {
            if (entry.SameAs == null)
                entry.Offset -= shift;
        }

        foreach (DeviceOffset entry in data.Displays.Values)
        {
            if (entry.SameAs == null)
                entry.Offset += shift;
        }

        data.SharedAudio.Offset -= shift;
        data.SharedDisplay.Offset += shift;

        apply();
        changed();

        explainOnce(shift_hint, IconUsage.Tune,
            "Shifted on every device: each audio device and display keeps its own offset, and they all moved together. "
            + "See them in Settings › Offsets by device.");

        return shift;
    }

    /// <summary>
    /// The audio offset <paramref name="device"/> would play with now, or the one in use where nothing's kept for it.
    /// </summary>
    public int AudioOffsetFor(string? device)
    {
        DeviceOffset? entry = device == null ? null : find(data.AudioDevices, device, data.SharedAudio);
        return entry == null ? audioOffset.Value : Math.Clamp(entry.Offset, YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_AUDIO_OFFSET);
    }

    /// <summary>The audio devices with an offset kept, by name.</summary>
    public IReadOnlyDictionary<string, DeviceOffset> AudioDevices => data.AudioDevices;

    /// <summary>The displays with an offset kept, by name.</summary>
    public IReadOnlyDictionary<string, DeviceOffset> Displays => data.Displays;

    /// <summary>The audio device and display a new one starts from.</summary>
    public string? StartAudioDevice => data.StartAudioDevice;

    public string? StartDisplay => data.StartDisplay;

    /// <summary>
    /// Sets the audio offset kept for an audio device; the one in use takes it at once.
    /// </summary>
    public void SetAudioDeviceOffset(string device, int offset) => setOffset(data.AudioDevices, device, offset);

    /// <summary>
    /// Sets the visual offset kept for a display; the one in use takes it at once.
    /// </summary>
    public void SetDisplayOffset(string display, int offset) => setOffset(data.Displays, display, offset);

    /// <summary>
    /// Forgets what's kept for an audio device, and anything said to be the same device as it. One in use starts again
    /// from <see cref="StartAudioDevice"/>'s.
    /// </summary>
    public void ForgetAudioDevice(string device) => forget(data.AudioDevices, device, isAudio: true);

    public void ForgetDisplay(string display) => forget(data.Displays, display, isAudio: false);

    /// <summary>
    /// Says an audio device is the same as another (the same headphones by another name), so it goes by that one's
    /// offset; null to keep its own again, starting from that one's.
    /// </summary>
    public void SetAudioDeviceSameAs(string device, string? sameAs) => setSameAs(data.AudioDevices, device, sameAs);

    public void SetDisplaySameAs(string display, string? sameAs) => setSameAs(data.Displays, display, sameAs);

    /// <summary>Picks the audio device a new one starts from.</summary>
    public void SetStartAudioDevice(string device)
    {
        data.StartAudioDevice = device;
        changed();
    }

    public void SetStartDisplay(string display)
    {
        data.StartDisplay = display;
        changed();
    }

    /// <summary>
    /// The name an audio device goes by to the player: its own, or what stands in where it has none.
    /// </summary>
    public static string AudioDeviceLabel(string device) => device.Length > 0 ? device : "Unnamed audio device";

    /// <remarks>Android names a display by its number alone, which is said as one.</remarks>
    public static string DisplayLabel(string display) => display.Length == 0 ? "Unnamed display"
        : display.All(char.IsAsciiDigit) ? $"Display {display}"
        : display;

    /// <summary>An offset as the settings show one: "+12 ms", "-8 ms", "0 ms".</summary>
    public static string Signed(int offset) => offset > 0 ? $"+{offset} ms" : $"{offset} ms";

    private DeviceOffset audioEntry => find(data.AudioDevices, AudioDevice, data.SharedAudio) ?? data.SharedAudio;

    private DeviceOffset displayEntry => find(data.Displays, Display, data.SharedDisplay) ?? data.SharedDisplay;

    /// <summary>
    /// What's kept for a device, following what it's said to be the same as; <paramref name="shared"/> where there's
    /// one for every device; null where nothing's kept for it.
    /// </summary>
    private DeviceOffset? find(Dictionary<string, DeviceOffset> devices, string name, DeviceOffset shared)
    {
        if (!data.PerDevice)
            return shared;

        // A few steps at most, so names said to be each other's can't go round forever.
        for (int i = 0; i < 8; i++)
        {
            if (!devices.TryGetValue(name, out DeviceOffset? entry))
                return null;

            if (entry.SameAs == null || entry.SameAs == name)
                return entry;

            name = entry.SameAs;
        }

        return devices.GetValueOrDefault(name);
    }

    /// <summary>
    /// Marks a device as in use now, keeping an offset for it from now on if it had none.
    /// </summary>
    /// <returns>Whether it's one the game hadn't played on.</returns>
    private bool use(Dictionary<string, DeviceOffset> devices, string name, DeviceOffset shared, string? start)
    {
        bool isNew = !devices.TryGetValue(name, out DeviceOffset? entry);

        if (entry == null)
        {
            DeviceOffset from = (start != null ? find(devices, start, shared) : null) ?? shared;
            devices[name] = entry = new DeviceOffset { Offset = from.Offset };
        }

        entry.LastUsed = DateTimeOffset.Now;
        return isNew;
    }

    private void setOffset(Dictionary<string, DeviceOffset> devices, string name, int offset)
    {
        DeviceOffset entry = find(devices, name, devices == data.AudioDevices ? data.SharedAudio : data.SharedDisplay)
                             ?? (devices[name] = new DeviceOffset());

        entry.Offset = offset;
        apply();
        changed();
    }

    private void forget(Dictionary<string, DeviceOffset> devices, string name, bool isAudio)
    {
        devices.Remove(name);

        foreach (DeviceOffset entry in devices.Values)
        {
            if (entry.SameAs == name)
                entry.SameAs = null;
        }

        // The one in use keeps going, from where a new one starts.
        if (isAudio)
            use(data.AudioDevices, AudioDevice, data.SharedAudio, data.StartAudioDevice);
        else
            use(data.Displays, Display, data.SharedDisplay, data.StartDisplay);

        apply();
        changed();
    }

    private void setSameAs(Dictionary<string, DeviceOffset> devices, string name, string? sameAs)
    {
        if (!devices.TryGetValue(name, out DeviceOffset? entry) || sameAs == name)
            return;

        // Keeping its own again starts from the one it went by.
        if (sameAs == null && entry.SameAs != null && find(devices, entry.SameAs, entry) is DeviceOffset was)
            entry.Offset = was.Offset;

        entry.SameAs = sameAs;
        apply();
        changed();
    }

    #endregion

    #region Songs

    /// <summary>
    /// A difficulty's song offset, in ms: 0 where it has none.
    /// </summary>
    public int GetSongOffset(int beatmapId) => data.Songs.GetValueOrDefault(beatmapId);

    /// <summary>
    /// Sets a difficulty's song offset, kept within <see cref="MIN_SONG_OFFSET"/> to <see cref="MAX_SONG_OFFSET"/>.
    /// </summary>
    public void SetSongOffset(int beatmapId, int offset)
    {
        offset = Math.Clamp(offset, MIN_SONG_OFFSET, MAX_SONG_OFFSET);

        if (offset == GetSongOffset(beatmapId))
            return;

        if (offset == 0)
            data.Songs.Remove(beatmapId);
        else
            data.Songs[beatmapId] = offset;

        changed();
        SongOffsetChanged?.Invoke(beatmapId);

        if (offset != 0)
        {
            explainOnce(song_offset_hint, IconUsage.MusicNote,
                "Song offsets change just one difficulty, on top of your devices' offsets. "
                + "Settings › Offsets by device lists every one.");
        }
    }

    /// <summary>The difficulties with a song offset, by ID.</summary>
    public IReadOnlyDictionary<int, int> SongOffsets => data.Songs;

    #endregion

    /// <summary>
    /// Puts the offsets kept for the devices in use into the settings.
    /// </summary>
    private void apply()
    {
        applying = true;

        try
        {
            audioOffset.Value = Math.Clamp(audioEntry.Offset, YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_AUDIO_OFFSET);
            visualOffset.Value = Math.Clamp(displayEntry.Offset, YuukiConfigManager.MIN_VISUAL_OFFSET, YuukiConfigManager.MAX_VISUAL_OFFSET);
        }
        finally
        {
            applying = false;
        }
    }

    private void changed()
    {
        unsaved = true;
        sinceChange = 0;
        Changed?.Invoke();
    }

    private void save()
    {
        unsaved = false;
        store.Save();
    }

    // A song being played or watched, which nothing should pop up over unasked.
    private bool inSong => screenStack?.CurrentScreen is GameplayScreen or ReplayScreen;

    /// <summary>
    /// Says which offset the game's now playing with, after a device changed; for one it hadn't played on, offers to
    /// calibrate for it.
    /// </summary>
    private void announce(bool isNew, IconUsage icon, string newText, string knownText)
    {
        if (notifications == null || !data.PerDevice)
            return;

        notifications.Post(new SimpleNotification
        {
            Icon = icon,
            Text = isNew ? newText : knownText,
            IsImportant = isNew,
            Transient = !isNew,
            Activated = isNew ? () => calibrate() : null
        });
    }

    /// <summary>
    /// Explains something the first time it happens, and never again: after the song, where it happens during one.
    /// </summary>
    private void explainOnce(string hint, IconUsage icon, string text)
    {
        if (!data.Explained.Add(hint))
            return;

        changed();
        heldBack.Enqueue(new SimpleNotification { Icon = icon, Text = text });
    }

    private bool calibrate()
    {
        // Not over a song, nor over calibrating or the editor, where it would take their place.
        if (screenStack?.CurrentScreen is null or GameplayScreen or ReplayScreen or CalibrationScreen or EditorScreen)
            return true;

        screenStack.Push(new CalibrationScreen());
        return true;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (unsaved)
            save();

        base.Dispose(isDisposing);
    }
}
