// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// What the game plays with, layer on layer (<see cref="OffsetStack"/>): the devices' offsets by name, a song's own,
/// and where they leave the judging and the notes.
/// </summary>
public partial class TestOffsetStack : YuukiTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    // One for the fixture, cached for the stacks to resolve, and added again after each test clears the scene.
    private readonly NamedDevicesOffsetManager offsets = new NamedDevicesOffsetManager(new OffsetStore(null)) { DisposeOnRemoval = false };

    private OffsetStack stack = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
        Cache<OffsetManager>(offsets);
    }

    private void show(int? beatmapId) => AddStep(beatmapId == null ? "the stack, no song" : "the stack, for Hard", () =>
    {
        if (offsets.Parent == null)
            Add(offsets);

        if (offsets.IsLoaded)
        {
            offsets.PerDevice = true;
            offsets.SetSongOffset(5, 0);
        }

        config.Get<int>(YuukiSetting.AudioOffset).Value = -10;
        config.Get<int>(YuukiSetting.VisualOffset).Value = 20;

        Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding(40),
            Child = stack = new OffsetStack(DefaultYuukiColorProvider.GetDefaultPalette())
            {
                RelativeSizeAxes = Axes.X,
                BeatmapId = beatmapId,
                DifficultyName = "Hard",
                SoundDelay = 20,
                PictureDelay = 10
            }
        });
    });

    [Test]
    public void TestEveryLayerByWhatItsFor()
    {
        show(null);
        AddUntilStep("the audio device's", () => stack.Summary.Contains("+ Speakers | audio offset -10 ms | this audio device"));
        AddAssert("the display's", () => stack.Summary.Contains("+ Built-in | visual offset +20 ms | this display"));
        AddAssert("what a song's would add", () => stack.Summary.Contains("+ The song | its song offset, if it has one | one difficulty"));
        AddAssert("measured", () => stack.Summary.Contains("Measured by Sakura | sound 20 ms · picture 10 ms | automatic"));
        AddAssert("in the end", () => stack.Summary.Contains("= You play with | taps judged 30 ms later · notes drawn 30 ms ahead"));
    }

    [Test]
    public void TestASongsOwnOffset()
    {
        show(5);
        AddStep("Hard 30 ms later", () => offsets.SetSongOffset(5, 30));
        AddUntilStep("this difficulty's", () => stack.Summary.Contains("+ Hard | song offset +30 ms | this difficulty only"));
        AddAssert("judged with it", () => stack.Summary.Contains("taps judged 0 ms later"));

        AddStep("nudged", () => offsets.SetSongOffset(5, 20));
        AddUntilStep("follows it", () => stack.Summary.Contains("song offset +20 ms") && stack.Summary.Contains("taps judged 10 ms later"));
    }

    [Test]
    public void TestOneOffsetForEveryDevice()
    {
        show(null);
        AddStep("one for all", () => offsets.PerDevice = false);
        AddUntilStep("says so", () => stack.Summary.Contains("| every audio device") && stack.Summary.Contains("| every display"));
        AddStep("one each again", () => offsets.PerDevice = true);
    }

    private partial class NamedDevicesOffsetManager : OffsetManager
    {
        public NamedDevicesOffsetManager(OffsetStore store)
            : base(store)
        {
        }

        protected override string CurrentAudioDevice => "Speakers";

        protected override string CurrentDisplay => "Built-in";
    }
}
