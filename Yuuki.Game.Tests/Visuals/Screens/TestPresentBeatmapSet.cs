// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestPresentBeatmapSet : TestYuukiScreenStack
{
    [Cached]
    private BeatmapsCollection beatmapsCollection { get; set; } = new BeatmapsCollection();

    [Cached]
    private readonly YuukiConfigManager configManager = new YuukiInMemoryConfigManager();

    [Cached]
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    private BeatmapSet imported = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("a library, the last set just imported", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(20);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(set => BeatmapTestUtility.CreateDummyBeatmaps(set, 2)).ToList();
            imported = beatmapsCollection.BeatmapSets[^1];
            workingBeatmap.BeatmapSet = beatmapsCollection.BeatmapSets[0];
        });
    }

    private static SongSelectionScreen songSelect(bool editMode = false) => new SongSelectionScreen
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        RelativeSizeAxes = Axes.Both,
        EditMode = editMode
    };

    [Test]
    public void TestPicksItOnSoloSongSelect()
    {
        SongSelectionScreen screen = null!;

        AddStep("on Solo Play's song select", () => screenStack.Push(screen = songSelect()));
        AddUntilStep("up", () => screen.IsLoaded && screenStack.CurrentScreen == screen);

        AddAssert("presenting goes", () => screenStack.PresentBeatmapSet(imported));
        AddAssert("same screen, the set picked", () => screenStack.CurrentScreen == screen && workingBeatmap.BeatmapSet == imported);

        AddStep("leave", () => screen.Exit());
    }

    [Test]
    public void TestOpensSoloFromEditSongSelect()
    {
        SongSelectionScreen edit = null!;

        AddStep("on the editor's song select", () => screenStack.Push(edit = songSelect(editMode: true)));
        AddUntilStep("up", () => edit.IsLoaded && screenStack.CurrentScreen == edit);

        AddAssert("presenting goes", () => screenStack.PresentBeatmapSet(imported));
        AddUntilStep("Solo Play's song select opened over it", () => screenStack.CurrentScreen is SongSelectionScreen { EditMode: false } solo && solo.IsLoaded);
        AddAssert("on the set", () => workingBeatmap.BeatmapSet == imported);

        AddStep("leave both", () =>
        {
            ((SongSelectionScreen)screenStack.CurrentScreen!).Exit();
        });
        AddUntilStep("back on the editor's", () => screenStack.CurrentScreen == edit);
        AddStep("leave", () => edit.Exit());
    }

    [Test]
    public void TestStaysPutMidSomething()
    {
        BusyScreen busy = null!;

        AddStep("somewhere a song is going on", () => screenStack.Push(busy = new BusyScreen { RelativeSizeAxes = Axes.Both }));
        AddUntilStep("up", () => busy.IsLoaded && screenStack.CurrentScreen == busy);

        AddAssert("presenting doesn't go", () => !screenStack.PresentBeatmapSet(imported));
        AddAssert("nothing changed", () => screenStack.CurrentScreen == busy && workingBeatmap.BeatmapSet != imported);

        AddStep("leave", () => busy.Exit());
    }

    /// <summary>
    /// Stands in for setup, gameplay, results or the editor.
    /// </summary>
    private partial class BusyScreen : YuukiScreen
    {
        public override bool AllowTrackChange => false;
    }
}
