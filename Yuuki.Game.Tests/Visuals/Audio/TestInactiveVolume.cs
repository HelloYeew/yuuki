// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Configurations;
using Sakura.Framework.Testing;
using Yuuki.Game.Audio;
using Yuuki.Game.Configurations;

namespace Yuuki.Game.Tests.Visuals.Audio;

public partial class TestInactiveVolume : TestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    [Resolved]
    private IAudioManager audio { get; set; } = null!;

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    private InactiveVolumeFader fader = null!;
    private bool windowActive;
    private double savedMaster;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("a fader, the window active", () =>
        {
            windowActive = true;
            config.Get<double>(YuukiSetting.InactiveVolume).Value = 0.25;
            frameworkConfig.Get<double>(FrameworkSetting.MasterVolume).Value = 0.8;
            savedMaster = 0.8;

            Add(fader = new InactiveVolumeFader { IsWindowActive = () => windowActive });
        });
        AddUntilStep("loaded", () => fader.IsLoaded);
    }

    [Test]
    public void TestFullVolumeWhileActive()
    {
        AddAssert("no fade", () => fader.Fade == 1);
        AddAssert("master as set", () => Math.Abs(audio.MasterVolume.Value - 0.8) < 1e-3);
    }

    [Test]
    public void TestFadesDownWhenInactiveAndBackUp()
    {
        AddStep("another window takes focus", () => windowActive = false);
        AddUntilStep("on its way down", () => fader.Fade < 0.95);
        AddAssert("gradually, not cut", () => fader.Fade > 0.25 + 0.05);
        AddAssert("the effective master follows", () => Math.Abs(audio.MasterVolume.Value - 0.8 * fader.Fade) < 1e-3);
        AddUntilStep("settles at a quarter of master", () => Math.Abs(fader.Fade - 0.25) < 0.01);
        AddAssert("the saved master volume is untouched", () => frameworkConfig.Get<double>(FrameworkSetting.MasterVolume).Value == savedMaster);
        AddStep("back to the game", () => windowActive = true);
        AddUntilStep("back up, master as set again", () => fader.Fade == 1 && Math.Abs(audio.MasterVolume.Value - 0.8) < 1e-3);
    }

    [Test]
    public void TestFollowsTheSettingWhileAway()
    {
        AddStep("another window takes focus", () => windowActive = false);
        AddStep("set inactive volume to a half", () => config.Get<double>(YuukiSetting.InactiveVolume).Value = 0.5);
        AddUntilStep("settles at a half", () => Math.Abs(fader.Fade - 0.5) < 0.01);
    }

    [Test]
    public void TestMovingMasterWhileAwayKeepsTheShare()
    {
        AddStep("another window takes focus", () => windowActive = false);
        AddUntilStep("settled", () => Math.Abs(fader.Fade - 0.25) < 0.01);
        AddStep("master to a half", () => frameworkConfig.Get<double>(FrameworkSetting.MasterVolume).Value = 0.5);
        AddUntilStep("a quarter of the new master", () => Math.Abs(audio.MasterVolume.Value - 0.5 * 0.25) < 0.005);
    }

    [Test]
    public void TestRemovingItLeavesNothingTurnedDown()
    {
        AddStep("another window takes focus", () => windowActive = false);
        AddUntilStep("turned down", () => fader.Fade < 0.9);
        AddStep("remove it", () => Remove(fader));
        AddUntilStep("master as set", () => Math.Abs(audio.MasterVolume.Value - 0.8) < 1e-3);
    }
}
