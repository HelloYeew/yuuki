// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// The text field's caret glides out and back once a beat of the song playing, and typing relights it.
/// </summary>
public partial class TestYuukiTextBox : YuukiManualInputManagerTestScene
{
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(workingBeatmap);
    }

    [Test]
    public void TestCaretBlinksToTheBeat()
    {
        YuukiTextBox box = null!;
        double litAt = 0;
        double period = 0;
        double flash = 0;

        AddStep("a 120 BPM song", () =>
        {
            BeatmapSet set = BeatmapTestUtility.MakeBeatmapSet(1);
            set.BPM = 120;
            workingBeatmap.BeatmapSet = set;
        });
        AddStep("add a text box", () => TestContent.Add(box = new YuukiTextBox
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        }));
        AddStep("focus it", () =>
        {
            InputManager.MoveMouseTo(box);
            InputManager.PressButton(MouseButton.Left);
            InputManager.ReleaseButton(MouseButton.Left);
        });

        AddUntilStep("the caret goes out between beats", () => !box.CaretLit);
        AddUntilStep("and comes back on the beat", () => box.CaretLit);
        // Focusing relit it off the beat, so the first return can be the beat snapping it back on;
        // measure between two later ones, which are a whole blink apart.
        AddUntilStep("settle: out", () => !box.CaretLit);
        AddUntilStep("settle: back", () => box.CaretLit);
        AddStep("note when", () => litAt = box.Clock.CurrentTime);
        AddUntilStep("out again", () => !box.CaretLit);
        AddStep("measure the flash", () => flash = box.Clock.CurrentTime - litAt);
        // A flash that dies away after the beat lands, not a glide lit for half a beat around it.
        AddAssert("lit for the start of the beat only", () => flash > 120 && flash < 230);
        AddUntilStep("back again", () => box.CaretLit);
        AddStep("measure the blink", () => period = box.Clock.CurrentTime - litAt);
        AddAssert("one blink a beat (500 ms at 120 BPM)", () => period > 400 && period < 650);

        AddUntilStep("out once more", () => !box.CaretLit);
        AddStep("type", () => InputManager.TypeText("a"));
        AddAssert("typing relights it", () => box.CaretLit);
    }
}
