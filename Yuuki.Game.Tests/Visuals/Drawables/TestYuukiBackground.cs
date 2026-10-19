// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Drawables;

/// <summary>
/// Which screens the beatmap video shows on: wherever the screen wants it, unless it's turned off
/// everywhere, or the screen is a play screen and it's turned off during play.
/// </summary>
public partial class TestYuukiBackground : YuukiTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    private YuukiBackground background = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
        workingBeatmap.BeatmapSet = new BeatmapTestUtility().ThemeSet;
        Cache(workingBeatmap);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("settings to defaults", () =>
        {
            config.Get<bool>(YuukiSetting.DisableVideo).Value = false;
            config.Get<bool>(YuukiSetting.DisableGameplayVideo).Value = false;
        });
        AddStep("add background", () => Child = background = new YuukiBackground
        {
            RelativeSizeAxes = Axes.Both
        });
        AddUntilStep("loaded", () => background.IsLoaded);
    }

    [Test]
    public void TestVideoFollowsScreen()
    {
        AddAssert("hidden to begin with", () => !background.VideoShown);
        AddStep("screen shows video", () => background.ShowVideo());
        AddAssert("shown", () => background.VideoShown);
        AddStep("play screen", () => background.SetGameplayDim(true));
        AddAssert("still shown in play", () => background.VideoShown);
        AddStep("screen hides video", () => background.HideVideo());
        AddAssert("hidden", () => !background.VideoShown);
    }

    [Test]
    public void TestGameplayVideoOffHidesItOnlyInPlay()
    {
        AddStep("turn off video during play", () => config.Get<bool>(YuukiSetting.DisableGameplayVideo).Value = true);
        AddStep("song select", () =>
        {
            background.SetGameplayDim(false);
            background.ShowVideo();
        });
        AddAssert("shown in song select", () => background.VideoShown);

        AddStep("gameplay", () => background.SetGameplayDim(true));
        AddAssert("hidden in play", () => !background.VideoShown);

        AddStep("turn it back on mid-play", () => config.Get<bool>(YuukiSetting.DisableGameplayVideo).Value = false);
        AddAssert("shown again", () => background.VideoShown);

        AddStep("turn it off again", () => config.Get<bool>(YuukiSetting.DisableGameplayVideo).Value = true);
        AddStep("back to song select", () => background.SetGameplayDim(false));
        AddAssert("shown after play", () => background.VideoShown);
    }

    [Test]
    public void TestVideoOffEverywhereWins()
    {
        AddStep("song select", () => background.ShowVideo());
        AddStep("turn off video", () => config.Get<bool>(YuukiSetting.DisableVideo).Value = true);
        AddAssert("hidden in song select", () => !background.VideoShown);
        AddStep("gameplay", () => background.SetGameplayDim(true));
        AddAssert("hidden in play", () => !background.VideoShown);
    }
}
