// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.Screens;

/// <summary>
/// Everything <see cref="OffsetManager"/> keeps, to look over and change. What's played on now comes first, with its two
/// offsets and calibrating. Then every audio device, display and song offset kept, a line each: what can be done to one
/// opens under it when asked for, each thing said in a sentence, so the page stays a list however many there are. Last,
/// what goes for every device.
/// </summary>
public partial class OffsetDevicesScreen : YuukiScreen, IHaveScreenAction
{
    // The page's widest, read comfortably; past it, the rest is margin.
    private const float max_content_width = 980;
    private const float side_margin = 32;

    // Kept clear under the page for the button along the bottom.
    private const float bottom_room = 110;

    // Between the title and the page.
    private const float title_gap = 12;

    // Kept clear on the right of a line: for a stepper on what's played on now, and for an offset with its button in
    // the lists.
    private const float stepper_column = 240;
    private const float value_column = 200;

    // Where a line's text starts, past its icon.
    private const float icon_room = 40;

    // How often what Sakura measures is read again: it settles over seconds.
    private const double measurement_interval = 500;

    public IReadOnlyList<ScreenAction> ScreenActions { get; }

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Play;

    public override bool DimBackground => true;

    /// <summary>
    /// Whether leaving opens the settings again, for a screen opened from there.
    /// </summary>
    public bool ReturnsToSettings { get; init; }

    /// <summary>
    /// The lines listed, by what they're for: "audio:" or "display:" and the device's name, or "song:" and the
    /// difficulty's ID.
    /// </summary>
    public IReadOnlyDictionary<string, Drawable> Rows => rows;

    /// <summary>
    /// The sound's and the picture's delays Sakura measures (ms). Set, they're taken as given instead (tests).
    /// </summary>
    public double? SoundDelay { get; set; }

    public double? PictureDelay { get; set; }

    [Resolved(canBeNull: true)]
    private OffsetManager? offsets { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private ScoreStore? scoreStore { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? beatmaps { get; set; }

    [Resolved(canBeNull: true)]
    private SettingsOverlay? settings { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiScreenStack? screenStack { get; set; }

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    [Resolved(canBeNull: true)]
    private IAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private AppHost? host { get; set; }

    private YuukiColorPalette palette;

    private FlowContainer page = null!;
    private Container scrollArea = null!;
    private ScreenTitleTab titleTab = null!;

    private readonly Dictionary<string, Drawable> rows = new Dictionary<string, Drawable>();

    // The lines whose choices are open, and the device whose "same as" choices are, by the same keys.
    private readonly HashSet<string> open = new HashSet<string>();
    private string? choosingSameAs;

    // Every song listed since the page opened: one stepped to 0 stays, rather than going from under the finger.
    private readonly HashSet<int> listedSongs = new HashSet<int>();

    // What the page was built from. Built again when that changes; when only an offset does, what shows it is set.
    private string? builtFrom;
    private readonly List<Action> refreshers = new List<Action>();
    private bool updateScheduled;

    // What Sakura measures, said under what's played on now.
    private Words? soundNote;
    private Words? pictureNote;
    private double nextMeasurement;

    public OffsetDevicesScreen()
    {
        ScreenActions = new List<ScreenAction>
        {
            new ScreenAction
            {
                Name = "Back",
                Action = Exit,
                KeyCombination = new KeyCombination(InputKeyExtensions.FromKey(Key.Escape))
            }
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        ScreenName.Value = "Offsets by device";
        ScreenHelperText.Value = "Each pair of headphones and each screen keeps its own offset.";

        palette = Palette;

        AddRange(new Drawable[]
        {
            // Under the title, so nothing scrolls up behind it.
            scrollArea = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Child = new YuukiScrollableContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = page = new FlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 20)
                    }
                }
            },
            titleTab = new ScreenTitleTab("Offsets by device", palette),
            new LeftBottomShearButton
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = LeftBottomShearButton.DEFAULT_POSITION,
                Action = Exit,
                BackgroundColor = palette.Surface
            }
        });

        rebuild();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (offsets != null)
            offsets.Changed += scheduleUpdate;
    }

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);

        Alpha = 0;
        this.FadeIn(300, Easing.OutQuint);
        titleTab.SlideIn();
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        this.FadeOut(300, Easing.OutQuint);
        titleTab.SlideOut();

        if (ReturnsToSettings)
            settings?.Show();
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        this.FadeOut(300, Easing.OutQuint);
        titleTab.SlideOut();
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);
        this.FadeIn(300, Easing.OutQuint);
        titleTab.SlideIn();
    }

    public override void Update()
    {
        base.Update();

        // Centred, and no wider than reads well; below the title.
        float side = Math.Max(side_margin, (DrawWidth - max_content_width) / 2);
        scrollArea.Padding = new MarginPadding { Top = titleTab.Bottom + title_gap };
        page.Padding = new MarginPadding { Left = side, Right = side, Top = 4, Bottom = bottom_room };

        if (Clock.CurrentTime < nextMeasurement)
            return;

        nextMeasurement = Clock.CurrentTime + measurement_interval;

        soundNote?.Say(describeSound());
        pictureNote?.Say(describePicture());
    }

    /// <summary>
    /// Calibrates on what's played on now, coming back here after.
    /// </summary>
    public void Calibrate() => screenStack?.Push(new CalibrationScreen());

    protected override void Dispose(bool isDisposing)
    {
        if (offsets != null)
            offsets.Changed -= scheduleUpdate;

        base.Dispose(isDisposing);
    }

    // Once a frame at most, and never from inside a control's own click, which building again replaces.
    private void scheduleUpdate()
    {
        if (updateScheduled)
            return;

        updateScheduled = true;
        Scheduler.Add(() =>
        {
            updateScheduled = false;

            if (offsets != null && structure(offsets) == builtFrom)
            {
                foreach (Action refresh in refreshers)
                    refresh();
            }
            else
                rebuild();
        });
    }

    /// <summary>
    /// What the page is built from, but for the offsets themselves: the devices and songs listed, which is in use,
    /// what each goes by, and what's open.
    /// </summary>
    private string structure(OffsetManager manager)
    {
        listedSongs.UnionWith(manager.SongOffsets.Keys);

        var parts = new List<string> { manager.PerDevice.ToString(), manager.AudioDevice, manager.Display, choosingSameAs ?? string.Empty };
        parts.AddRange(open.OrderBy(k => k, StringComparer.Ordinal));
        parts.AddRange(manager.AudioDevices.OrderBy(d => d.Key, StringComparer.Ordinal)
                              .Select(d => $"audio:{d.Key}>{d.Value.SameAs}{(d.Key == manager.StartAudioDevice ? " start" : string.Empty)}"));
        parts.AddRange(manager.Displays.OrderBy(d => d.Key, StringComparer.Ordinal)
                              .Select(d => $"display:{d.Key}>{d.Value.SameAs}{(d.Key == manager.StartDisplay ? " start" : string.Empty)}"));
        parts.AddRange(listedSongs.Order().Select(id => $"song:{id}"));

        return string.Join("\n", parts);
    }

    private void rebuild()
    {
        page.Clear();
        rows.Clear();
        refreshers.Clear();
        soundNote = pictureNote = null;
        nextMeasurement = 0;

        if (offsets == null || config == null)
        {
            page.Add(card("Offsets by device", paragraph("There's nothing kept here: the game is running without its offsets.")));
            return;
        }

        builtFrom = structure(offsets);

        page.Add(createNow(offsets, config));

        if (offsets.PerDevice)
        {
            page.Add(card("Headphones and speakers", createDevices(offsets, isAudio: true)));
            page.Add(card("Screens", createDevices(offsets, isAudio: false)));
        }

        page.Add(card("Song offsets", createSongs(offsets)));
        page.Add(createEveryDevice(offsets));
    }

    #region Now

    /// <summary>
    /// The audio device and display played on now, each with its offset to step, what Sakura measures of its delay
    /// itself, and calibrating on them.
    /// </summary>
    private Drawable createNow(OffsetManager manager, YuukiConfigManager configManager)
    {
        bool perDevice = manager.PerDevice;

        Drawable audio = nowRow(IconUsage.Headphones, perDevice ? OffsetManager.AudioDeviceLabel(manager.AudioDevice) : "Every audio device",
            soundNote = note(describeSound()),
            boundStepper(configManager.Get<int>(YuukiSetting.AudioOffset), YuukiConfigManager.MIN_AUDIO_OFFSET, YuukiConfigManager.MAX_AUDIO_OFFSET));

        Drawable visual = nowRow(IconUsage.Monitor, perDevice ? OffsetManager.DisplayLabel(manager.Display) : "Every display",
            pictureNote = note(describePicture()),
            boundStepper(configManager.Get<int>(YuukiSetting.VisualOffset), YuukiConfigManager.MIN_VISUAL_OFFSET, YuukiConfigManager.MAX_VISUAL_OFFSET));

        return card(perDevice ? "Playing on now" : "Playing with", column(
            audio,
            visual,
            option(perDevice
                    ? "Not sure what they should be? Tap along for about a minute, and both are worked out for these two."
                    : "Not sure what they should be? Tap along for about a minute, and both are worked out.",
                button("Calibrate", Calibrate))));
    }

    private Drawable nowRow(IconUsage icon, string name, Drawable description, Drawable stepper)
    {
        stepper.Anchor = Anchor.TopRight;
        stepper.Origin = Anchor.TopRight;

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new[]
            {
                rowIcon(icon, true),
                new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                    Padding = new MarginPadding { Left = icon_room, Right = stepper_column },
                    Children = new[] { rowTitle(name, true), description }
                },
                stepper
            }
        };
    }

    // A setting's offset to step, following it as calibrating, the settings or a device change set it.
    private OffsetStepper boundStepper(Reactive<int> setting, int min, int max)
    {
        var stepper = new OffsetStepper(palette, string.Empty, min, max);
        stepper.Current.Value = setting.Value;
        stepper.Current.ValueChanged += e =>
        {
            if (e.NewValue != setting.Value)
                setting.Value = e.NewValue;
        };

        refreshers.Add(() =>
        {
            if (stepper.Current.Value != setting.Value)
                stepper.Current.Value = setting.Value;
        });

        return stepper;
    }

    private string describeSound()
    {
        double sound = SoundDelay is double given ? OffsetStack.Measured(given) : OffsetStack.MeasuredSound(audioManager);

        return double.IsNaN(sound)
            ? "Audio offset: when your taps count. Sakura can't measure this output's delay, so the offset covers all of it."
            : $"Audio offset: when your taps count. Sakura already takes off the {sound:0} ms of delay it measures here; the offset is for the rest.";
    }

    private string describePicture()
    {
        double picture = PictureDelay is double given ? OffsetStack.Measured(given) : OffsetStack.MeasuredPicture(host);

        return double.IsNaN(picture)
            ? "Visual offset: where the notes are drawn. Sakura can't measure this screen's delay, so the offset covers all of it."
            : $"Visual offset: where the notes are drawn. Sakura already takes off the {picture:0} ms of delay it measures here; the offset is for the rest.";
    }

    #endregion

    #region Devices

    private Drawable createDevices(OffsetManager manager, bool isAudio)
    {
        IReadOnlyDictionary<string, DeviceOffset> devices = isAudio ? manager.AudioDevices : manager.Displays;
        string inUse = isAudio ? manager.AudioDevice : manager.Display;

        var lines = new List<Drawable>
        {
            note(isAudio
                ? "Every audio device you've played on, with the audio offset it keeps. Sound plays on one, and the game switches to its offset."
                : "Every screen you've played on, with the visual offset it keeps. Move the game to one, and it switches to its offset.")
        };

        foreach ((string name, DeviceOffset entry) in devices.OrderByDescending(d => d.Key == inUse).ThenByDescending(d => d.Value.LastUsed))
            lines.Add(deviceRow(manager, isAudio, name, entry, name == inUse));

        if (devices.Count == 1)
        {
            lines.Add(note(isAudio
                ? "Play on other headphones or speakers, and they're listed here, each with an offset of its own."
                : "Play on another screen, and it's listed here, with an offset of its own."));
        }

        return column(lines.ToArray());
    }

    private Drawable deviceRow(OffsetManager manager, bool isAudio, string name, DeviceOffset entry, bool inUse)
    {
        string key = $"{(isAudio ? "audio" : "display")}:{name}";
        IReadOnlyDictionary<string, DeviceOffset> devices = isAudio ? manager.AudioDevices : manager.Displays;

        return listRow(key, isAudio ? IconUsage.Headphones : IconUsage.Monitor, label(isAudio, name), inUse,
            () => describeUse(isAudio, name, entry, inUse),
            () => OffsetManager.Signed(offsetOf(devices, name)),
            () => deviceChoices(manager, isAudio, name, entry, inUse));
    }

    /// <summary>
    /// What can be done to a device, each said in a sentence: its offset (the one in use has it above), saying it's
    /// another by a different name, starting new ones from it, and forgetting it.
    /// </summary>
    private IEnumerable<Drawable> deviceChoices(OffsetManager manager, bool isAudio, string name, DeviceOffset entry, bool inUse)
    {
        string key = $"{(isAudio ? "audio" : "display")}:{name}";
        IReadOnlyDictionary<string, DeviceOffset> devices = isAudio ? manager.AudioDevices : manager.Displays;
        string? start = isAudio ? manager.StartAudioDevice : manager.StartDisplay;

        if (entry.SameAs == null && !inUse)
        {
            var stepper = new OffsetStepper(palette, isAudio ? "Audio offset" : "Visual offset",
                isAudio ? YuukiConfigManager.MIN_AUDIO_OFFSET : YuukiConfigManager.MIN_VISUAL_OFFSET,
                isAudio ? YuukiConfigManager.MAX_AUDIO_OFFSET : YuukiConfigManager.MAX_VISUAL_OFFSET);

            stepper.Current.Value = entry.Offset;
            stepper.Current.ValueChanged += e =>
            {
                if (e.NewValue == entry.Offset)
                    return;

                if (isAudio)
                    manager.SetAudioDeviceOffset(name, e.NewValue);
                else
                    manager.SetDisplayOffset(name, e.NewValue);
            };

            refreshers.Add(() =>
            {
                if (stepper.Current.Value != entry.Offset)
                    stepper.Current.Value = entry.Offset;
            });

            yield return option(isAudio
                ? "Set by calibrating while sound plays on it, or by hand here."
                : "Set by calibrating with the game on it, or by hand here.", stepper);
        }

        List<string> others = devices.Where(d => d.Key != name && d.Value.SameAs == null).Select(d => d.Key).ToList();

        if (entry.SameAs != null)
        {
            yield return option($"It goes by {possessive(label(isAudio, entry.SameAs))} offset, as you said they're the same {(isAudio ? "device" : "screen")}.",
                button("Give it its own offset", () => sameAs(manager, isAudio, name, null)));
        }
        else if (others.Count > 0)
        {
            yield return option(isAudio
                    ? "The same headphones or speakers listed again under another name, through another port or driver? Say which, and both go by one offset."
                    : "The same screen listed again under another name? Say which, and both go by one offset.",
                button(choosingSameAs == key ? "Not the same" : "It's the same as…", () =>
                {
                    choosingSameAs = choosingSameAs == key ? null : key;
                    scheduleUpdate();
                }));

            if (choosingSameAs == key)
            {
                var choices = new FlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FlowDirection.Horizontal,
                    Spacing = new Vector2(10, 10)
                };

                foreach (string other in others)
                    choices.Add(button(label(isAudio, other), () => sameAs(manager, isAudio, name, other)));

                yield return choices;
            }
        }

        string newOnes = isAudio ? "New devices" : "New screens";

        if (name == start)
            yield return note($"{newOnes} start from this one's offset until you calibrate on them.");
        else if (entry.SameAs == null)
        {
            yield return option(
                $"{newOnes} start from {possessive(start != null ? label(isAudio, start) : "the first one")} offset until you calibrate on them. "
                + "Start them from this one instead if most of yours are like it.",
                button("Start new ones from it", () =>
                {
                    if (isAudio)
                        manager.SetStartAudioDevice(name);
                    else
                        manager.SetStartDisplay(name);
                }));
        }

        if (!inUse)
        {
            yield return option("Don't use it any more? Forget it, and if it's ever played on again, it starts afresh.",
                button("Forget", () =>
                {
                    open.Remove(key);

                    if (isAudio)
                        manager.ForgetAudioDevice(name);
                    else
                        manager.ForgetDisplay(name);
                }));
        }
    }

    private void sameAs(OffsetManager manager, bool isAudio, string name, string? other)
    {
        choosingSameAs = null;

        if (isAudio)
            manager.SetAudioDeviceSameAs(name, other);
        else
            manager.SetDisplaySameAs(name, other);
    }

    // The offset a device plays with: its own, or that of the one it's said to be the same as.
    private static int offsetOf(IReadOnlyDictionary<string, DeviceOffset> devices, string name)
    {
        DeviceOffset? entry = devices.GetValueOrDefault(name);

        // A few steps at most, as the manager takes, so names said to be each other's can't go round forever.
        for (int i = 0; i < 8 && entry?.SameAs != null && devices.TryGetValue(entry.SameAs, out DeviceOffset? next); i++)
            entry = next;

        return entry?.Offset ?? 0;
    }

    private static string label(bool isAudio, string name) => isAudio ? OffsetManager.AudioDeviceLabel(name) : OffsetManager.DisplayLabel(name);

    // "Speaker's", "Wired headphones'".
    private static string possessive(string name) => name.EndsWith('s') ? $"{name}'" : $"{name}'s";

    /// <summary>
    /// Whether it's in use or when it was last, how often it's been played on, and which way the plays on it have
    /// leaned lately.
    /// </summary>
    private string describeUse(bool isAudio, string name, DeviceOffset entry, bool inUse)
    {
        var parts = new List<string>();

        if (inUse)
            parts.Add("In use now");
        else if (entry.LastUsed != default)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Last used {entry.LastUsed.LocalDateTime:d MMM yyyy}"));

        if (entry.SameAs != null)
            parts.Add($"goes by {possessive(label(isAudio, entry.SameAs))} offset");

        if (scoreStore != null && offsets != null)
        {
            List<ScoreInfo> plays = scoreStore.Scores.Where(s => (isAudio ? s.AudioDevice : s.Display) == name).ToList();

            if (plays.Count > 0)
                parts.Add(plays.Count == 1 ? "1 play" : $"{plays.Count} plays");

            var history = new TimingHistory(offsets.AudioOffsetFor, offsets.GetSongOffset);
            List<double> leans = plays.Where(p => p.MeanError != null)
                                      .TakeLast(TimingHistory.RECENT_PLAYS)
                                      .Select(p => history.LeanNow(p)!.Value)
                                      .ToList();

            if (leans.Count > 0)
            {
                double lean = TimingHistory.Median(leans);
                parts.Add(Math.Abs(lean) < 1 ? "on time lately" : $"hitting {Math.Abs(lean):0} ms {(lean > 0 ? "late" : "early")} lately");
            }
        }

        return string.Join("  ·  ", parts);
    }

    #endregion

    #region Songs

    private Drawable createSongs(OffsetManager manager)
    {
        if (listedSongs.Count == 0)
        {
            return paragraph("No song has an offset of its own yet. They're for a song out of time with its own music: give one before playing it, "
                             + "nudge it with − and + while playing, or take it from the results when a song keeps leaning one way.");
        }

        var lines = new List<Drawable>
        {
            note("For a song out of time with its own music: each moves one difficulty's notes and their judging together, on top of your devices' offsets.")
        };

        foreach (int id in listedSongs.OrderBy(songName, StringComparer.CurrentCultureIgnoreCase))
            lines.Add(songRow(manager, id));

        return column(lines.ToArray());
    }

    private Drawable songRow(OffsetManager manager, int id)
    {
        Beatmap? beatmap = beatmaps?.GetBeatmapById(id);
        BeatmapSet? set = beatmap?.BeatmapSet;

        string name = set != null ? $"{set.Artist} - {set.Title}" : beatmap != null ? "A song" : "A difficulty no longer in your library";
        string difficulty = beatmap != null ? beatmap.DifficultyName : $"#{id}";

        return listRow($"song:{id}", IconUsage.MusicNote, name, false,
            () => difficulty,
            () => OffsetManager.Signed(manager.GetSongOffset(id)),
            () => songChoices(manager, id, set));
    }

    private IEnumerable<Drawable> songChoices(OffsetManager manager, int id, BeatmapSet? set)
    {
        var stepper = new OffsetStepper(palette, "Song offset", OffsetManager.MIN_SONG_OFFSET, OffsetManager.MAX_SONG_OFFSET);
        stepper.Current.Value = manager.GetSongOffset(id);
        stepper.Current.ValueChanged += e => manager.SetSongOffset(id, e.NewValue);

        refreshers.Add(() =>
        {
            if (stepper.Current.Value != manager.GetSongOffset(id))
                stepper.Current.Value = manager.GetSongOffset(id);
        });

        yield return option("Notes reaching the line after the music? Lower it. Before? Raise it.", stepper);

        if (set != null && beatmaps!.GetBeatmapsFromBeatmapSet(set).Length > 1)
        {
            yield return option("The whole song out of time, whichever difficulty? Give its other difficulties this offset too.",
                button("Use on every difficulty", () =>
                {
                    int offset = manager.GetSongOffset(id);

                    foreach (Beatmap difficulty in beatmaps.GetBeatmapsFromBeatmapSet(set))
                        manager.SetSongOffset(difficulty.ID, offset);
                }));
        }

        yield return option("Take it off, and the song plays with your devices' offsets alone.",
            button("Remove", () =>
            {
                listedSongs.Remove(id);
                open.Remove($"song:{id}");
                manager.SetSongOffset(id, 0);
                scheduleUpdate();
            }));
    }

    // "Artist - Title [Difficulty]", or what's left of it for a difficulty that's no longer in the library.
    private string songName(int id)
    {
        Beatmap? beatmap = beatmaps?.GetBeatmapById(id);

        if (beatmap == null)
            return $"A difficulty no longer in your library (#{id})";

        BeatmapSet? set = beatmap.BeatmapSet;
        return set != null ? $"{set.Artist} - {set.Title} [{beatmap.DifficultyName}]" : $"[{beatmap.DifficultyName}]";
    }

    #endregion

    #region Every device

    /// <summary>
    /// What goes for every device: keeping one each at all, and shifting the timing on all of them at once.
    /// </summary>
    private Drawable createEveryDevice(OffsetManager manager)
    {
        var toggle = new YuukiCheckbox
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft
        };

        toggle.Current.Value = manager.PerDevice;
        toggle.Current.ValueChanged += e => manager.PerDevice = e.NewValue;

        return card("Every device", column(
            new FlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(12, 0),
                Children = new Drawable[]
                {
                    toggle,
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "Keep an offset for each device",
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 19, FontWeight.Bold),
                        Color = palette.Text
                    }
                }
            },
            note(manager.PerDevice
                ? "On: each audio device and screen keeps its own, and the game switches between them as you change devices."
                : "Off: one audio offset and one visual offset for everything you play on. Turn it on to keep one for each again."),
            divider(),
            new SpriteText
            {
                Text = "Hitting late, or early, on every song, whatever you play on?",
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 19, FontWeight.Bold),
                Color = palette.Text
            },
            option($"That's your own timing, or your keys', not a device's. Each press moves the judging {OffsetStepper.STEP} ms on every device at once. "
                   + "The results screen offers this itself when your last few plays agree.",
                button("I hit late", () => manager.ShiftTiming(OffsetStepper.STEP)),
                button("I hit early", () => manager.ShiftTiming(-OffsetStepper.STEP)))));
    }

    #endregion

    /// <summary>
    /// One thing kept, a line of the list: its icon, its name over what's known of it, and on the right its offset and
    /// a button that opens what can be done with it underneath.
    /// </summary>
    private Drawable listRow(string key, IconUsage icon, string title, bool now, Func<string> details, Func<string> value,
                             Func<IEnumerable<Drawable>> choices)
    {
        bool isOpen = open.Contains(key);

        Words detailsText = note(details());
        var valueText = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = value(),
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold),
            Color = now ? palette.Control : palette.Text
        };

        refreshers.Add(() =>
        {
            detailsText.Say(details());
            valueText.Text = value();
        });

        var row = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 12),
            Children = new Drawable[]
            {
                divider(),
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        rowIcon(icon, now),
                        new FlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FlowDirection.Vertical,
                            Spacing = new Vector2(0, 2),
                            Padding = new MarginPadding { Left = icon_room, Right = value_column },
                            Children = new Drawable[] { rowTitle(title, now), detailsText }
                        },
                        new FlowContainer
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Horizontal,
                            Spacing = new Vector2(14, 0),
                            Children = new Drawable[]
                            {
                                valueText,
                                button(isOpen ? "Done" : "Edit", () =>
                                {
                                    if (!open.Remove(key))
                                        open.Add(key);

                                    if (choosingSameAs == key)
                                        choosingSameAs = null;

                                    scheduleUpdate();
                                })
                            }
                        }
                    }
                }
            }
        };

        if (isOpen)
        {
            row.Add(new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 14),
                Padding = new MarginPadding { Left = icon_room, Bottom = 6 },
                Children = choices().ToArray()
            });
        }

        rows[key] = row;
        return row;
    }

    /// <summary>
    /// A choice: what it does, in a sentence, over what does it.
    /// </summary>
    private Drawable option(string explanation, params Drawable[] controls) => new FlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 8),
        Children = new Drawable[]
        {
            note(explanation),
            new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Horizontal,
                Spacing = new Vector2(12, 10),
                Children = controls
            }
        }
    };

    private IconSprite rowIcon(IconUsage icon, bool now) => new IconSprite
    {
        Icon = icon,
        IconSize = 24,
        Filled = true,
        Color = now ? palette.Control : palette.Text,
        Alpha = now ? 1 : 0.8f
    };

    private TextFlowContainer rowTitle(string text, bool now) => new TextFlowContainer(t =>
    {
        t.Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 20, FontWeight.Bold);
        t.Color = now ? palette.Control : palette.Text;
    })
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Text = text
    };

    private Box divider() => new Box
    {
        RelativeSizeAxes = Axes.X,
        Height = 1,
        Color = palette.Text,
        Alpha = 0.12f
    };

    private PillButton button(string text, Action action) => new PillButton(palette.Control, palette.TextLight)
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Text = text,
        Action = action
    };

    private static FlowContainer column(params Drawable[] children) => new FlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FlowDirection.Vertical,
        Spacing = new Vector2(0, 14),
        Children = children
    };

    private TextFlowContainer paragraph(string text) => new TextFlowContainer(t =>
    {
        t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, bodySize, FontWeight.Regular);
        t.Color = palette.Text;
    })
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Text = text
    };

    private Words note(string text)
    {
        var words = new Words(t =>
        {
            t.Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, gameLayout?.IsPhone == true ? 17 : 14, FontWeight.Regular);
            t.Color = palette.Text;
            t.Alpha = 0.8f;
        })
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y
        };

        words.Say(text);
        return words;
    }

    /// <summary>
    /// A section of the page: a light panel with its title, as the offset guide's.
    /// </summary>
    private Drawable card(string title, Drawable content) => new Container
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Masking = true,
        CornerRadius = 14,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = palette.Surface,
                Alpha = 0.92f
            },
            new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 14),
                Padding = new MarginPadding { Left = 28, Right = 28, Top = 20, Bottom = 24 },
                Children = new[]
                {
                    new SpriteText
                    {
                        Text = title,
                        Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 26, FontWeight.Bold),
                        Color = palette.Text
                    },
                    content
                }
            }
        }
    };

    // Bigger on a phone, where the desktop size comes out too small to read.
    private int bodySize => gameLayout?.IsPhone == true ? 19 : 16;

    /// <summary>
    /// Words that wrap, laid out again only when they're given something else to say.
    /// </summary>
    private partial class Words : TextFlowContainer
    {
        private string? said;

        public Words(Action<SpriteText> style)
            : base(style)
        {
        }

        public void Say(string text)
        {
            if (text == said)
                return;

            said = text;
            Text = text;
        }
    }
}
