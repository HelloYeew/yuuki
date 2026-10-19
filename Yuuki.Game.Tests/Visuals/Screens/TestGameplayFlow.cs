// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Screens;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// Smoke coverage for the play flow screens (setup -> gameplay -> results). Verifies each loads and
/// becomes current when pushed; full completion isn't exercised headlessly (the lead-in plus the
/// slow virtual clock would exceed the step timeout).
/// </summary>
public class TestGameplayFlow : TestYuukiScreenStack
{
    [Test]
    public void TestSetupScreenLoads()
    {
        AddStep("push setup", () => screenStack.Push(new FinalSetupScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("setup is current", () => screenStack.CurrentScreen is FinalSetupScreen);
    }

    [Test]
    public void TestGameplayScreenLoads()
    {
        AddStep("push gameplay", () => screenStack.Push(new GameplayScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("gameplay is current", () => screenStack.CurrentScreen is GameplayScreen);
        AddUntilStep("clock started", () => ((GameplayScreen)screenStack.CurrentScreen!).ScreenName.Value == "Gameplay");
    }

    [Test]
    public void TestTrackChangeOnlyOutsidePlay()
    {
        AddAssert("song select allows it", () => new SongSelectionScreen().AllowTrackChange);
        AddAssert("setup locks it", () => !new FinalSetupScreen().AllowTrackChange);
        AddAssert("gameplay locks it", () => !new GameplayScreen().AllowTrackChange);
        AddAssert("results lock it", () => !new ScoreScreen(new GameplayResult()).AllowTrackChange);
    }

    [Test]
    public void TestScoreScreenLoads()
    {
        var result = new GameplayResult
        {
            Score = 123456,
            MaxCombo = 42,
            CriticalCount = 30,
            BreakCount = 8,
            HitCount = 3,
            MissCount = 1
        };

        AddStep("push results", () => screenStack.Push(new ScoreScreen(result)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("results is current", () => screenStack.CurrentScreen is ScoreScreen);
    }
}
