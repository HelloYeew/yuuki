// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestSongSelectionPeek : YuukiManualInputManagerTestScene
{
    private readonly BeatmapsCollection beatmapsCollection = new BeatmapsCollection();
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    private YuukiScreenStack screenStack = null!;
    private SongSelectionScreen songSelect = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(5);
        var beatmaps = new List<Beatmap>();
        foreach (BeatmapSet set in beatmapsCollection.BeatmapSets)
            beatmaps.AddRange(BeatmapTestUtility.CreateDummyBeatmaps(set, 2));
        beatmapsCollection.Beatmaps = beatmaps;

        Cache(beatmapsCollection);
        Cache(config);
        Cache(workingBeatmap);
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());

        screenStack = new YuukiScreenStack
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            DisposeOnRemoval = false
        };
        Cache(screenStack);
    }

    [Test]
    public void TestHoldHidesTheCursorToo()
    {
        AddStep("show song select", () =>
        {
            TestContent.Add(screenStack);
            screenStack.Push(songSelect = new SongSelectionScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("it's up", () => songSelect.IsLoaded);
        AddAssert("the cursor shows", () => !songSelect.CursorHidden.Value);

        // Over the background below the song panel, clear of the list, the buttons and the footer.
        AddStep("press and hold on the background", () =>
        {
            InputManager.MoveMouseTo(songSelect.ToScreenSpace(new Vector2(songSelect.DrawWidth * 0.3f, songSelect.DrawHeight * 0.7f)));
            InputManager.PressButton(MouseButton.Left);
        });
        AddUntilStep("peeking", () => songSelect.IsPeeking);
        AddAssert("the cursor is tucked away", () => songSelect.CursorHidden.Value);

        AddStep("let go", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("not peeking", () => !songSelect.IsPeeking);
        AddAssert("the cursor is back", () => !songSelect.CursorHidden.Value);

        AddStep("leave", () => songSelect.Exit());
    }
}
