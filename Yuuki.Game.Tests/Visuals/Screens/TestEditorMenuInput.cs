// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Edit;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.ScreenStacks;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The editor's menus with the mouse: a menu dropped from the bar hangs below it, over the timeline,
/// and every row of it has to take the mouse, not whatever is underneath.
/// </summary>
public partial class TestEditorMenuInput : YuukiManualInputManagerTestScene
{
    private readonly WorkingBeatmap working = new WorkingBeatmap();

    private YuukiScreenStack screenStack = null!;
    private EditorScreen editor = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(working);
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(new EditorClipboard());

        screenStack = new YuukiScreenStack
        {
            RelativeSizeAxes = Axes.Both,
            DisposeOnRemoval = false
        };
        Cache(screenStack);
    }

    [SetUp]
    public void SetUpEditor()
    {
        AddStep("open the editor", () =>
        {
            var set = new BeatmapSet { ID = 7, Title = "Song", Artist = "Artist", BPM = 120 };

            working.BeatmapSet = set;
            working.Beatmap = new Beatmap
            {
                ID = 70,
                BeatmapSet = set,
                DifficultyName = "Normal",
                LaneCount = 4,
                Notes = new[] { new BeatmapNote { StartTime = 1000, EndTime = 1000, Lane = NoteLane.Lane1 } }
            };

            TestContent.Add(screenStack);
            screenStack.Push(editor = new EditorScreen { RelativeSizeAxes = Axes.Both });
        });
        AddUntilStep("the editor is up", () => editor.IsLoaded && screenStack.CurrentScreen == editor);
    }

    [Test]
    public void TestEveryRowOfADroppedMenuTakesTheMouse()
    {
        Menu menu = null!;
        double timeBefore = 0;

        AddStep("open View", () =>
        {
            menu = editor.TopBar.Menu;
            InputManager.MoveMouseTo(menu.DrawableItems[2]);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("it drops down", () => menu.Submenu?.State == MenuState.Open && menu.Submenu.DrawableItems.Count == 2);

        AddStep("click its second row", () =>
        {
            timeBefore = editor.Session.Clock.CurrentTime;
            InputManager.MoveMouseTo(menu.Submenu!.DrawableItems[1]);
            InputManager.Click(MouseButton.Left);
        });

        AddAssert("the row took it", () => !editor.Session.SeekAfterPlacing.Value);
        AddAssert("the timeline under it didn't", () => editor.Session.Clock.CurrentTime == timeBefore);
    }

    /// <summary>
    /// A note on the judgement line sits low in the compose area, so its right-click menu is pushed up
    /// against the bottom and the Bank sub-menu hangs past it, over the bottom bar.
    /// </summary>
    [Test]
    public void TestRightClickSubMenusTakeTheMouseBelowTheComposeArea()
    {
        BeatmapNote note = null!;
        Menu menu = null!;

        AddStep("go to the note", () =>
        {
            note = editor.Session.Beatmap.Notes[0];
            editor.Session.Clock.Seek(note.StartTime);
        });
        AddStep("right-click it", () =>
        {
            InputManager.MoveMouseTo(editor.Compose.Playfield.ScreenSpacePositionOf(note));
            InputManager.Click(MouseButton.Right);
        });
        AddUntilStep("its menu opens", () =>
        {
            menu = editor.ContextMenus.Menu;
            return menu.State == MenuState.Open;
        });
        AddStep("open Bank", () =>
        {
            InputManager.MoveMouseTo(menu.DrawableItems[2]);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("Bank drops out", () => menu.Submenu?.State == MenuState.Open && menu.Submenu.DrawableItems.Count == 3);
        AddAssert("and hangs below the compose area", () =>
        {
            var drum = menu.Submenu!.DrawableItems[2];
            return drum.ToScreenSpace(drum.DrawSize / 2).Y > editor.Compose.ToScreenSpace(new Vector2(0, editor.Compose.DrawHeight)).Y;
        });
        AddStep("pick Drum", () =>
        {
            InputManager.MoveMouseTo(menu.Submenu!.DrawableItems[2]);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("the note is on drum", () => note.Bank == SampleBank.Drum);
    }
}
