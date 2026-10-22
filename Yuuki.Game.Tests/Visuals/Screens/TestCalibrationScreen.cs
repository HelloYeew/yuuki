// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The calibration screen from tapping to saving: by ear, by eye, then the check with both offsets on.
/// </summary>
public partial class TestCalibrationScreen : TestYuukiScreenStack
{
    private const double beat = 600;

    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private CalibrationScreen screen = null!;
    private MockPlayfield? lastField;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(config);
    }

    /// <summary>
    /// Taps landing 30 ms late on the clicks and 50 ms late on the notes: the music is judged 30 ms earlier, and the
    /// notes are drawn 50 ms along to be seen with it.
    /// </summary>
    [Test]
    public void TestWorksOutAndSavesBothOffsets()
    {
        AddStep("Clear the offsets", () =>
        {
            config.Get<int>(YuukiSetting.AudioOffset).Value = 0;
            config.Get<int>(YuukiSetting.VisualOffset).Value = 0;
        });
        AddStep("Open calibration", () => screenStack.Push(screen = new CalibrationScreen()));
        AddUntilStep("Listening", () => screen.IsLoaded && screen.Field?.IsLoaded == true);

        tapAlong(late: 30);
        AddUntilStep("On to watching", () => screen.Field != lastField && screen.Field?.IsLoaded == true);
        AddAssert("Notes this time, still unjudged", () => screen.Field!.AllowJudgement == false);

        tapAlong(late: 50);
        AddUntilStep("On to the check", () => screen.Field?.AllowJudgement == true);
        AddAssert("Both worked out", () => screen.Offsets == (-30, 50));
        AddAssert("The check runs with them", () => screen.Field!.AudioOffset == -30 && screen.Field!.VisualOffset == 50);

        AddStep("Save", () => screen.Save());
        AddAssert("Kept", () => config.Get<int>(YuukiSetting.AudioOffset).Value == -30 && config.Get<int>(YuukiSetting.VisualOffset).Value == 50);
        AddUntilStep("Gone", () => !screen.IsCurrentScreen);
    }

    [Test]
    public void TestLeavingKeepsTheOldOffsets()
    {
        AddStep("Set offsets", () =>
        {
            config.Get<int>(YuukiSetting.AudioOffset).Value = -12;
            config.Get<int>(YuukiSetting.VisualOffset).Value = 7;
        });
        AddStep("Open calibration", () => screenStack.Push(screen = new CalibrationScreen()));
        AddUntilStep("Listening", () => screen.Field?.IsLoaded == true);

        tapAlong(late: 80);
        AddStep("Leave", () => screen.Exit());
        AddAssert("Untouched", () => config.Get<int>(YuukiSetting.AudioOffset).Value == -12 && config.Get<int>(YuukiSetting.VisualOffset).Value == 7);
    }

    // The taps a step needs, and the warm-up before them, each `late` after its beat.
    private void tapAlong(double late)
    {
        AddStep($"Tap {late} ms late on twenty beats", () =>
        {
            lastField = screen.Field;

            for (int i = 1; i <= 20; i++)
            {
                screen.Field!.Playfield.PressLane(i % 4, i * beat + late);
                screen.Field!.Playfield.ReleaseLane(i % 4, i * beat + late + 80);
            }
        });
    }
}
