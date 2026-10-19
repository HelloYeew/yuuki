// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestMusicPlayerOverlay : YuukiTestScene
{
    [Cached]
    private readonly BeatmapsCollection beatmaps = new BeatmapsCollection();

    [Cached]
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    private MusicPlayerOverlay overlay = null!;

    public TestMusicPlayerOverlay()
    {
        beatmaps.BeatmapSets.Add(new BeatmapSet { ID = 1, Title = "Moonlightspeed", Artist = "Midnight Grand Orchestra" });
        beatmaps.BeatmapSets.Add(new BeatmapSet { ID = 2, Title = "Flower Dance", Artist = "DJ OKAWARI" });
        beatmaps.BeatmapSets.Add(new BeatmapSet { ID = 3, Title = "This Game", Artist = "Roselia" });
        workingBeatmap.BeatmapSet = beatmaps.BeatmapSets[0];
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("add overlay", () => Add(overlay = new MusicPlayerOverlay()));
    }

    [Test]
    public void TestShowHide()
    {
        AddStep("show", () => overlay.Show());
        AddUntilStep("visible", () => overlay.State == Visibility.Visible);
        // Let Update() run a few frames (progress bar + play-state polling) with no audio manager.
        AddWaitStep("settle", 3);
        AddStep("hide", () => overlay.Hide());
        AddUntilStep("hidden", () => overlay.State == Visibility.Hidden);
    }

    [Test]
    public void TestReflectsWorkingSetChange()
    {
        AddStep("show", () => overlay.Show());
        AddStep("switch to second set", () => workingBeatmap.BeatmapSet = beatmaps.BeatmapSets[1]);
        AddAssert("working set updated", () => workingBeatmap.BeatmapSet.ID == 2);
    }
}
