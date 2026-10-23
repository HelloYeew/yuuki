// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Yuuki.Game.Database;

/// <summary>
/// The offsets kept apart from the settings: the audio offset of each audio device and the visual offset of each
/// display the game has played on, and each difficulty's song offset. Saved to <c>offsets.json</c>;
/// <see cref="Configurations.OffsetManager"/> puts them to use.
/// </summary>
public class OffsetStore
{
    private const string file_name = "offsets.json";

    private readonly Storage? storage;

    /// <summary>
    /// What's kept. Changed in place by <see cref="Configurations.OffsetManager"/>, then <see cref="Save"/>d.
    /// </summary>
    public OffsetData Data { get; private set; } = new OffsetData();

    /// <summary>
    /// Whether <see cref="Data"/> came from a file: false the first time, before there's anything kept.
    /// </summary>
    public bool Loaded { get; private set; }

    public OffsetStore(Storage? storage)
    {
        this.storage = storage;
        load();
    }

    private void load()
    {
        if (storage == null || !storage.Exists(file_name))
            return;

        try
        {
            using Stream stream = storage.GetStream(file_name);

            if (JsonSerializer.Deserialize<OffsetData>(stream) is OffsetData data)
            {
                Data = data;
                Loaded = true;
            }
        }
        catch (Exception e)
        {
            // Starts again from the settings' offsets, as a first run does.
            Logger.Error($"Failed to read the offsets ({file_name}); starting again from the settings.", e);
        }
    }

    public void Save()
    {
        if (storage == null)
            return;

        try
        {
            // Written to a temporary file and moved into place, so a crash mid-write can't corrupt it.
            using Stream stream = storage.CreateFileSafely(file_name);
            JsonSerializer.Serialize(stream, Data);
        }
        catch (Exception e)
        {
            Logger.Error($"Failed to save the offsets ({file_name}).", e);
        }
    }
}

/// <summary>
/// Everything in <c>offsets.json</c>.
/// </summary>
public class OffsetData
{
    /// <summary>
    /// Whether each audio device and display keeps an offset of its own. Without, <see cref="SharedAudio"/> and
    /// <see cref="SharedDisplay"/> are used on all of them.
    /// </summary>
    public bool PerDevice { get; set; } = true;

    /// <summary>Audio offsets by the name of the audio device.</summary>
    public Dictionary<string, DeviceOffset> AudioDevices { get; set; } = new Dictionary<string, DeviceOffset>();

    /// <summary>Visual offsets by the name of the display.</summary>
    public Dictionary<string, DeviceOffset> Displays { get; set; } = new Dictionary<string, DeviceOffset>();

    public DeviceOffset SharedAudio { get; set; } = new DeviceOffset();

    public DeviceOffset SharedDisplay { get; set; } = new DeviceOffset();

    /// <summary>
    /// The audio device and the display a new one starts from: the first the game saw, unless the player picks another.
    /// </summary>
    public string? StartAudioDevice { get; set; }

    public string? StartDisplay { get; set; }

    /// <summary>
    /// Song offsets by difficulty (<see cref="Beatmaps.Beatmap.ID"/>), in ms: moves the difficulty's notes and its
    /// judging together, for a chart that's out of time with its music. Difficulties without one aren't listed.
    /// </summary>
    public Dictionary<int, int> Songs { get; set; } = new Dictionary<int, int>();

    /// <summary>
    /// What's been explained once already, so it isn't again: see <see cref="Configurations.OffsetManager"/>'s hints.
    /// </summary>
    public HashSet<string> Explained { get; set; } = new HashSet<string>();
}

/// <summary>
/// One audio device's or display's offset.
/// </summary>
public class DeviceOffset
{
    /// <summary>The offset in ms.</summary>
    public int Offset { get; set; }

    public DateTimeOffset LastUsed { get; set; }

    /// <summary>
    /// Another device's name this one goes by, when the player has said they're the same device (the same headphones
    /// on another port): its offset is that one's.
    /// </summary>
    public string? SameAs { get; set; }
}
