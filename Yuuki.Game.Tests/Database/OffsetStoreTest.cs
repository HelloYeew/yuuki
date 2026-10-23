// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Yuuki.Game.Database;

namespace Yuuki.Game.Tests.Database;

/// <summary>
/// The offsets kept apart from the settings, in offsets.json.
/// </summary>
[TestFixture]
public class OffsetStoreTest
{
    private string tempDir = null!;
    private NativeStorage storage = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-offsetstore-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Test]
    public void TestNothingKeptTheFirstTime()
    {
        var store = new OffsetStore(storage);

        Assert.That(store.Loaded, Is.False);
        Assert.That(store.Data.PerDevice, Is.True);
        Assert.That(store.Data.AudioDevices, Is.Empty);
    }

    [Test]
    public void TestKeptAcrossRuns()
    {
        var store = new OffsetStore(storage);
        store.Data.Explained.Add("song offset");
        store.Data.AudioDevices["AirPods Pro"] = new DeviceOffset { Offset = 150, LastUsed = new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.FromHours(7)) };
        store.Data.AudioDevices["AirPods Pro (2)"] = new DeviceOffset { SameAs = "AirPods Pro" };
        store.Data.Displays["LG HDR 4K"] = new DeviceOffset { Offset = -20 };
        store.Data.StartAudioDevice = "MacBook Pro Speakers";
        store.Data.Songs[4610626] = -12;
        store.Save();

        var again = new OffsetStore(storage);

        Assert.That(again.Loaded, Is.True);
        Assert.That(again.Data.Explained, Does.Contain("song offset"));
        Assert.That(again.Data.AudioDevices["AirPods Pro"].Offset, Is.EqualTo(150));
        Assert.That(again.Data.AudioDevices["AirPods Pro"].LastUsed, Is.EqualTo(store.Data.AudioDevices["AirPods Pro"].LastUsed));
        Assert.That(again.Data.AudioDevices["AirPods Pro (2)"].SameAs, Is.EqualTo("AirPods Pro"));
        Assert.That(again.Data.Displays["LG HDR 4K"].Offset, Is.EqualTo(-20));
        Assert.That(again.Data.StartAudioDevice, Is.EqualTo("MacBook Pro Speakers"));
        Assert.That(again.Data.Songs[4610626], Is.EqualTo(-12));
    }

    [Test]
    public void TestADamagedFileStartsAgain()
    {
        File.WriteAllText(Path.Combine(tempDir, "offsets.json"), "{ not json");

        var store = new OffsetStore(storage);

        Assert.That(store.Loaded, Is.False);
        Assert.That(store.Data.Songs, Is.Empty);
    }
}
