// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestMainMenuScreen : TestYuukiScreenStack
{
    private MainMenuScreen mainMenuScreen = null!;

    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(workingBeatmap);
        Cache(config);
    }

    [Test]
    public void TestAddScreen()
    {
        AddStep("Add screen", () => screenStack.Push(mainMenuScreen = new MainMenuScreen()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddStep("Remove screen", () => mainMenuScreen.Exit());
    }

    /// <summary>The song playing is announced in the corner on arrival, and again whenever it changes.</summary>
    [Test]
    public void TestNowPlayingAnnouncesSongs()
    {
        BeatmapSet first = BeatmapTestUtility.MakeBeatmapSet(1);
        BeatmapSet second = BeatmapTestUtility.MakeBeatmapSet(2);

        AddStep("a song playing", () => workingBeatmap.BeatmapSet = first);
        AddStep("Add screen", () => screenStack.Push(mainMenuScreen = new MainMenuScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("it's announced", () => mainMenuScreen.IsLoaded && mainMenuScreen.NowPlaying.IsShowing && mainMenuScreen.NowPlaying.ShownTitle == first.Title);

        AddStep("the song changes", () => workingBeatmap.BeatmapSet = second);
        AddAssert("the new one is announced", () => mainMenuScreen.NowPlaying.ShownTitle == second.Title);
        AddStep("Remove screen", () => mainMenuScreen.Exit());
    }

    /// <summary>The announcement and the idle card switch script as soon as the setting changes.</summary>
    [Test]
    public void TestNowPlayingFollowsUnicodeSetting()
    {
        BeatmapSet song = BeatmapTestUtility.MakeBeatmapSet(1);
        song.TitleUnicode = "テストソング";

        AddStep("romanized", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = false);
        AddStep("a song playing", () => workingBeatmap.BeatmapSet = song);
        AddStep("Add screen", () => screenStack.Push(mainMenuScreen = new MainMenuScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("romanized title", () => mainMenuScreen.IsLoaded && mainMenuScreen.NowPlaying.ShownTitle == song.Title);

        AddStep("switch to unicode", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = true);
        AddAssert("toast in unicode", () => mainMenuScreen.NowPlaying.ShownTitle == song.TitleUnicode);
        AddAssert("idle card in unicode", () => mainMenuScreen.IdlePanel.ShownTitle == song.TitleUnicode);

        AddStep("switch back", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = false);
        AddAssert("toast romanized again", () => mainMenuScreen.NowPlaying.ShownTitle == song.Title);
        AddStep("Remove screen", () => mainMenuScreen.Exit());
    }
}
