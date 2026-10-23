// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Reactive;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// The offsets kept for each audio device and display, shifting them all at once, and song offsets
/// (<see cref="OffsetManager"/>), with devices that change when a test says.
/// </summary>
public partial class TestOffsetManager : YuukiTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private DevicesOffsetManager manager = null!;

    private Reactive<int> audio => config.Get<int>(YuukiSetting.AudioOffset);
    private Reactive<int> visual => config.Get<int>(YuukiSetting.VisualOffset);

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
    }

    [SetUp]
    public void SetUp()
    {
        // As a player who'd calibrated before there were offsets for each device.
        AddStep("offsets before", () =>
        {
            audio.Value = -10;
            visual.Value = 20;
        });
        AddStep("start", () => Child = manager = new DevicesOffsetManager(new OffsetStore(null)));
        AddUntilStep("loaded", () => manager.IsLoaded);
    }

    private void useAudio(string device)
    {
        AddStep($"play on {device}", () => manager.AudioNow = device);
        AddUntilStep($"on {device}", () => manager.AudioDevice == device);
    }

    private void useDisplay(string display)
    {
        AddStep($"move to {display}", () => manager.DisplayNow = display);
        AddUntilStep($"on {display}", () => manager.Display == display);
    }

    [Test]
    public void TestTheOffsetsBeforeAreTheFirstDevices()
    {
        AddAssert("kept for the speakers", () => manager.AudioDevices["Speakers"].Offset == -10);
        AddAssert("kept for the screen", () => manager.Displays["Built-in"].Offset == 20);
        AddAssert("new ones start from them", () => manager.StartAudioDevice == "Speakers" && manager.StartDisplay == "Built-in");
        AddAssert("still in use", () => audio.Value == -10 && visual.Value == 20);
    }

    [Test]
    public void TestEachAudioDeviceKeepsItsOwn()
    {
        useAudio("AirPods");
        AddAssert("starts from the speakers'", () => audio.Value == -10);

        AddStep("set it", () => audio.Value = 120);
        useAudio("Speakers");
        AddAssert("the speakers' again", () => audio.Value == -10);

        useAudio("AirPods");
        AddAssert("the AirPods' again", () => audio.Value == 120);
        AddAssert("the picture's untouched", () => visual.Value == 20);
    }

    [Test]
    public void TestEachDisplayKeepsItsOwn()
    {
        useDisplay("TV");
        AddStep("set it", () => visual.Value = -40);
        useDisplay("Built-in");
        AddAssert("the screen's again", () => visual.Value == 20);

        useDisplay("TV");
        AddAssert("the TV's again", () => visual.Value == -40);
        AddAssert("the sound's untouched", () => audio.Value == -10);
    }

    [Test]
    public void TestTimingGoesToEveryDevice()
    {
        useAudio("AirPods");
        AddStep("set it", () => audio.Value = 120);

        AddStep("shift the timing 10 ms later", () => manager.ShiftTiming(10));
        AddAssert("judged later, the notes kept", () => audio.Value == 110 && visual.Value == 30);

        useAudio("Speakers");
        AddAssert("the speakers too", () => audio.Value == -20);

        useDisplay("TV");
        AddAssert("and a new display", () => visual.Value == 30);
        AddAssert("kept in each", () => manager.AudioDevices["AirPods"].Offset == 110 && manager.AudioDevices["Speakers"].Offset == -20);
    }

    [Test]
    public void TestTheSameDeviceMovesWithTheOneItGoesBy()
    {
        useAudio("AirPods");
        AddStep("set it", () => audio.Value = 120);
        useAudio("AirPods (2)");
        AddStep("it's the AirPods", () => manager.SetAudioDeviceSameAs("AirPods (2)", "AirPods"));

        AddStep("shift the timing 10 ms later", () => manager.ShiftTiming(10));
        AddAssert("moved once", () => audio.Value == 110 && manager.AudioDevices["AirPods"].Offset == 110);
    }

    [Test]
    public void TestTimingStopsAtTheLimits()
    {
        AddStep("audio nearly at its lowest", () => audio.Value = YuukiConfigManager.MIN_AUDIO_OFFSET + 5);
        AddAssert("moves 5 of 20", () => manager.ShiftTiming(20) == 5);
        AddAssert("at the limit", () => audio.Value == YuukiConfigManager.MIN_AUDIO_OFFSET);
    }

    [Test]
    public void TestOneOffsetForEveryDevice()
    {
        AddStep("one for all", () => manager.PerDevice = false);
        AddStep("set it", () => audio.Value = 50);

        useAudio("AirPods");
        AddAssert("the same on another", () => audio.Value == 50);

        useAudio("Speakers");
        AddAssert("and back", () => audio.Value == 50);
    }

    [Test]
    public void TestTheSameDeviceByAnotherName()
    {
        useAudio("AirPods");
        AddStep("set it", () => audio.Value = 120);

        useAudio("AirPods (2)");
        AddAssert("a new one, from the speakers'", () => audio.Value == -10);

        AddStep("it's the AirPods", () => manager.SetAudioDeviceSameAs("AirPods (2)", "AirPods"));
        AddAssert("their offset", () => audio.Value == 120);

        AddStep("change it", () => audio.Value = 130);
        AddAssert("for both", () => manager.AudioDevices["AirPods"].Offset == 130);

        AddStep("its own again", () => manager.SetAudioDeviceSameAs("AirPods (2)", null));
        AddAssert("from theirs", () => audio.Value == 130 && manager.AudioDevices["AirPods (2)"].SameAs == null);
    }

    [Test]
    public void TestForgettingTheOneInUse()
    {
        useAudio("AirPods");
        AddStep("set it", () => audio.Value = 120);

        AddStep("forget it", () => manager.ForgetAudioDevice("AirPods"));
        AddAssert("back to where new ones start", () => audio.Value == -10 && manager.AudioDevices["AirPods"].Offset == -10);
    }

    [Test]
    public void TestSongOffsets()
    {
        int changed = 0;

        AddStep("watch", () => manager.SongOffsetChanged += _ => changed++);
        AddStep("set one", () => manager.SetSongOffset(5, 30));
        AddAssert("kept", () => manager.GetSongOffset(5) == 30 && changed == 1);

        AddStep("too far", () => manager.SetSongOffset(5, 500));
        AddAssert("at the limit", () => manager.GetSongOffset(5) == OffsetManager.MAX_SONG_OFFSET);

        AddStep("back to none", () => manager.SetSongOffset(5, 0));
        AddAssert("not listed", () => !manager.SongOffsets.ContainsKey(5));
        AddAssert("others have none", () => manager.GetSongOffset(6) == 0);
        AddAssert("the settings untouched", () => audio.Value == -10 && visual.Value == 20);
    }

    /// <summary>
    /// Android names a display by its number alone.
    /// </summary>
    [Test]
    public void TestADisplayNamedByNumber() => AddAssert("said as one", () =>
        OffsetManager.DisplayLabel("1") == "Display 1" && OffsetManager.DisplayLabel("LG TV 2") == "LG TV 2" && OffsetManager.DisplayLabel(string.Empty) == "Unnamed display");

    /// <summary>
    /// Plays on the devices a test says, rather than the ones the machine has.
    /// </summary>
    private partial class DevicesOffsetManager : OffsetManager
    {
        public string AudioNow = "Speakers";
        public string DisplayNow = "Built-in";

        public DevicesOffsetManager(OffsetStore store)
            : base(store)
        {
        }

        protected override string CurrentAudioDevice => AudioNow;

        protected override string CurrentDisplay => DisplayNow;
    }
}
