// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Edit;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The editor end to end, headless and silent (no track): it opens on the working beatmap, every tab
/// shows, edits undo and redo, notes copy and paste, saving writes the library, test play comes back,
/// and leaving with unsaved changes asks first.
/// </summary>
public partial class TestEditorScreen : TestYuukiScreenStack
{
    private string tempDir = null!;
    private NativeStorage storage = null!;
    private BeatmapsCollection collection = null!;
    private WorkingBeatmap working = null!;
    private DialogOverlay dialogOverlay = null!;

    private EditorScreen editor = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-editor-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);
        collection = new BeatmapsCollection();
        working = new WorkingBeatmap();

        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(collection);
        Cache(working);
        // The editor finds the library through the game's storage, as the writer does.
        Cache<Storage>(storage);
        Cache(new BeatmapWriter(storage, collection));
        Cache(new EditorClipboard());
        Cache(dialogOverlay = new DialogOverlay { DisposeOnRemoval = false });
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [SetUp]
    public void SetUpEditor()
    {
        AddStep("add dialogs", () =>
        {
            if (dialogOverlay.Parent == null)
                Add(dialogOverlay);
        });

        // The library and the stack are the fixture's; each test starts from an empty one of each.
        AddStep("a fresh start", () =>
        {
            screenStack.Size = Vector2.One;

            while (screenStack.CurrentScreen != null)
                screenStack.CurrentScreen.Exit();

            collection.BeatmapSets.Clear();
            collection.Beatmaps.Clear();

            foreach (string directory in Directory.EnumerateDirectories(tempDir))
                Directory.Delete(directory, true);
        });

        AddStep("pick a beatmap", () =>
        {
            // A chart from before timing points: the set's 120 BPM, a beat on the first note.
            var set = new BeatmapSet { ID = 7, Title = "Song", Artist = "Artist", BPM = 120, Creator = "Mapper" };

            working.BeatmapSet = set;
            working.Beatmap = new Beatmap
            {
                ID = 70,
                BeatmapSet = set,
                DifficultyName = "Normal",
                Creator = "Mapper",
                LaneCount = 4,
                Notes = new[]
                {
                    new BeatmapNote { StartTime = 1000, EndTime = 1000, Lane = NoteLane.Lane1 },
                    new BeatmapNote { StartTime = 1500, EndTime = 1500, Lane = NoteLane.Lane2 },
                    new BeatmapNote { StartTime = 2000, EndTime = 2500, Lane = NoteLane.Lane3, Type = NoteType.Hold }
                }
            };
        });

        AddStep("open the editor", () => screenStack.Push(editor = new EditorScreen { RelativeSizeAxes = Axes.Both }));
        AddUntilStep("the editor is up", () => editor.IsLoaded && screenStack.CurrentScreen == editor);
    }

    [Test]
    public void TestOpensOnTheWorkingBeatmap()
    {
        AddAssert("its notes", () => editor.Session.Beatmap.Notes.Count == 3);
        // The legacy grid: 120 BPM with a beat on the first note (1000 ms), so a beat at 0 too.
        AddAssert("the legacy tempo, made explicit", () =>
        {
            var points = editor.Session.Beatmap.ControlPoints.TimingPoints;
            return points.Count == 1 && points[0].Time == 0 && System.Math.Abs(points[0].Bpm - 120) < 0.001;
        });
        AddAssert("nothing to save yet", () => !editor.Session.Changes.HasUnsavedChanges.Value);
    }

    /// <summary>
    /// Every tab fills the space between the timeline and the bottom bar: no more, which once put the
    /// playfield and the selection panel off screen.
    /// </summary>
    [Test]
    public void TestTabsFitTheEditor()
    {
        AddUntilStep("laid out", () => editor.Compose.DrawHeight > 0);
        AddAssert("compose fits", () => editor.Compose.DrawWidth <= editor.DrawWidth && editor.Compose.DrawHeight < editor.DrawHeight);
        AddAssert("the playfield fits", () => editor.Compose.Playfield.DrawWidth <= editor.DrawWidth && editor.Compose.Playfield.DrawHeight < editor.DrawHeight);
        AddAssert("the playfield is on screen", () =>
        {
            var centre = editor.Compose.Playfield.ToScreenSpace(editor.Compose.Playfield.DrawSize / 2);
            var editorCentre = editor.ToScreenSpace(editor.DrawSize / 2);
            return System.Math.Abs(centre.X - editorCentre.X) < 1;
        });
    }

    /// <summary>
    /// A sprite text shows a newline as a missing glyph, so text over several lines has to be a text flow.
    /// </summary>
    [Test]
    public void TestNoTextHasRawNewlines()
    {
        AddStep("select the hold", () => editor.Session.Beatmap.Select(new[] { editor.Session.Beatmap.Notes[2] }));
        AddUntilStep("the selection is described", () => texts().Any(t => t.Text.Trim() == "Hold"));
        AddAssert("no line breaks inside a sprite text", () => !texts().Any(t => t.Text.Contains('\n')));
    }

    private IEnumerable<SpriteText> texts() => descendants(editor).OfType<SpriteText>();

    private static IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is not Container container)
            yield break;

        foreach (var child in container.Children)
        {
            foreach (var descendant in descendants(child))
                yield return descendant;
        }
    }

    /// <summary>
    /// A big UI scale leaves the tabs short: the side panels scroll rather than run off the bottom.
    /// </summary>
    [Test]
    public void TestSidePanelsScrollWhenTheTabIsShort()
    {
        float shortHeight = 0;

        AddStep("a short window", () => screenStack.Size = new Vector2(1, 0.45f));
        AddUntilStep("the tools fit the tab", () => editor.Compose.ToolColumn.DrawHeight > 0 && editor.Compose.ToolColumn.DrawHeight <= editor.Compose.DrawHeight + 0.5f);
        AddStep("note how tall", () => shortHeight = editor.Compose.ToolColumn.DrawHeight);
        AddAssert("and scroll", () => editor.Compose.ToolColumn.ScrollableExtent.Y > 0);
        AddStep("timing", () => editor.Mode.Value = EditorScreenMode.Timing);
        AddUntilStep("the timing panels fit", () =>
            editor.TimingTab.SettingsColumn.DrawHeight > 0 && editor.TimingTab.SettingsColumn.DrawHeight <= editor.TimingTab.DrawHeight + EditorPanelColumn.SHADOW_ROOM * 2 + 0.5f);
        AddAssert("and scroll", () => editor.TimingTab.SettingsColumn.ScrollableExtent.Y > 0);
        AddStep("compose, in a tall window again", () =>
        {
            editor.Mode.Value = EditorScreenMode.Compose;
            screenStack.Size = Vector2.One;
        });
        AddUntilStep("the tools take the room back", () => editor.Compose.ToolColumn.DrawHeight > shortHeight + 1);
    }

    [Test]
    public void TestEveryTabShows()
    {
        AddStep("timing", () => editor.Mode.Value = EditorScreenMode.Timing);
        AddUntilStep("a few frames", () => editor.Mode.Value == EditorScreenMode.Timing);
        AddStep("setup", () => editor.Mode.Value = EditorScreenMode.Setup);
        AddUntilStep("a few frames", () => editor.Mode.Value == EditorScreenMode.Setup);
        AddStep("compose", () => editor.Mode.Value = EditorScreenMode.Compose);
        AddAssert("still editing", () => screenStack.CurrentScreen == editor);
    }

    [Test]
    public void TestUndoAndRedo()
    {
        AddStep("add a note", () => editor.Session.Beatmap.Add(new BeatmapNote { StartTime = 3000, EndTime = 3000, Lane = NoteLane.Lane4 }));
        AddAssert("unsaved", () => editor.Session.Changes.HasUnsavedChanges.Value);
        AddStep("undo", () => editor.Undo());
        AddAssert("gone", () => editor.Session.Beatmap.Notes.Count == 3 && !editor.Session.Changes.HasUnsavedChanges.Value);
        AddStep("redo", () => editor.Redo());
        AddAssert("back", () => editor.Session.Beatmap.Notes.Count == 4);
    }

    [Test]
    public void TestCopyAndPaste()
    {
        AddStep("select the first note", () => editor.Session.Beatmap.Select(new[] { editor.Session.Beatmap.Notes[0] }));
        AddStep("copy", () => editor.Copy());
        AddStep("go later", () => editor.Session.Clock.Seek(4000));
        AddStep("paste", () => editor.Paste());
        AddAssert("pasted at the playhead, selected", () =>
        {
            var selection = editor.Session.Beatmap.SelectedNotes;
            return editor.Session.Beatmap.Notes.Count == 4 && selection.Count == 1 && selection[0].StartTime == 4000 && selection[0].Lane == NoteLane.Lane1;
        });
    }

    [Test]
    public void TestSaveWritesTheLibrary()
    {
        AddStep("add a note", () => editor.Session.Beatmap.Add(new BeatmapNote { StartTime = 3000, EndTime = 3000, Lane = NoteLane.Lane4 }));
        AddAssert("saves", () => editor.Save());
        AddAssert("nothing left to save", () => !editor.Session.Changes.HasUnsavedChanges.Value);
        AddAssert("the chart is on disk", () => Directory.GetFiles(tempDir, "*.ykb", SearchOption.AllDirectories).Length == 1);
        AddAssert("and in the library", () => collection.GetBeatmapById(70) is { } saved && saved.NotesStoragePath != null && saved.ControlPoints.HasTiming);
    }

    [Test]
    public void TestOpenFolderAndFileOnceSaved()
    {
        MenuItem item(string text) => editor.TopBar.Menu.Items[0].Items.First(i => i.Text.Value == text);

        AddAssert("not before it's in the library", () => !item("Open beatmap folder").Enabled.Value && !item("Open beatmap file").Enabled.Value);
        AddStep("save", () => editor.Save());
        AddAssert("then both", () => item("Open beatmap folder").Enabled.Value && item("Open beatmap file").Enabled.Value);
    }

    [Test]
    public void TestTestPlayComesBack()
    {
        AddStep("go to a note", () => editor.Session.Clock.Seek(1500));
        AddStep("test", () => editor.TestPlay());
        AddUntilStep("playing", () => screenStack.CurrentScreen is EditorTestPlayScreen { IsLoaded: true });
        AddStep("leave", () => screenStack.CurrentScreen!.Exit());
        AddUntilStep("back in the editor", () => screenStack.CurrentScreen == editor);
        AddAssert("where the test started", () => System.Math.Abs(editor.Session.Clock.CurrentTime - 1500) < 1);
    }

    [Test]
    public void TestLeavingWithChangesAsks()
    {
        AddStep("add a note", () => editor.Session.Beatmap.Add(new BeatmapNote { StartTime = 3000, EndTime = 3000, Lane = NoteLane.Lane4 }));
        AddStep("leave", () => editor.AttemptExit());
        AddAssert("asked", () => dialogOverlay.CurrentDialog is PromptForSaveDialog);
        AddStep("keep editing", () => dialogOverlay.CurrentDialog!.PerformCancelAction());
        AddAssert("still editing", () => screenStack.CurrentScreen == editor);

        AddStep("leave again", () => editor.AttemptExit());
        AddStep("save my masterpiece", () => dialogOverlay.CurrentDialog!.PerformOkAction());
        AddUntilStep("left", () => screenStack.CurrentScreen != editor);
        AddAssert("saved", () => collection.GetBeatmapById(70) != null);
        AddAssert("song select gets the saved one", () => working.Beatmap.NotesStoragePath != null && working.Beatmap.Notes.Length == 0);
    }

    [Test]
    public void TestLeavingWithoutChangesDoesNotAsk()
    {
        AddStep("leave", () => editor.AttemptExit());
        AddUntilStep("left", () => screenStack.CurrentScreen != editor);
        AddAssert("not asked", () => dialogOverlay.CurrentDialog == null);
    }

    private EditorScreen currentEditor => (EditorScreen)screenStack.CurrentScreen!;

    private System.Collections.Generic.List<Yuuki.Game.Graphics.UserInterface.DialogButton> dialogButtons() =>
        descendants(dialogOverlay).OfType<Yuuki.Game.Graphics.UserInterface.DialogButton>().Where(b => b.IsLoaded).ToList();

    [Test]
    public void TestCreatingADifficultyOpensIt()
    {
        AddStep("save, so the set is in the library", () => editor.Save());
        AddStep("on the timing tab", () => editor.Mode.Value = EditorScreenMode.Timing);
        AddStep("create a difficulty", () => editor.CreateNewDifficulty());
        AddAssert("asked blank or copy", () => dialogOverlay.CurrentDialog is CreateNewDifficultyDialog);
        AddStep("from scratch", () => dialogOverlay.CurrentDialog!.PerformOkAction());
        AddUntilStep("its editor is up", () => screenStack.CurrentScreen is EditorScreen { IsLoaded: true } next && next != editor);
        AddAssert("on the new difficulty", () => currentEditor.Session.Beatmap.Beatmap.ID != 70 && currentEditor.Session.Beatmap.Notes.Count == 0);
        AddAssert("with the timing", () => currentEditor.Session.Beatmap.ControlPoints.HasTiming);
        AddAssert("same tab", () => currentEditor.Mode.Value == EditorScreenMode.Timing);
        AddAssert("two difficulties now", () => collection.GetBeatmapsFromBeatmapSet(collection.GetBeatmapSetById(7)!).Length == 2);
    }

    [Test]
    public void TestDeletingADifficultyGoesToAnother()
    {
        AddStep("save", () => editor.Save());
        AddStep("create a difficulty", () => editor.CreateNewDifficulty());
        AddStep("from scratch", () => dialogOverlay.CurrentDialog!.PerformOkAction());
        AddUntilStep("on it", () => screenStack.CurrentScreen is EditorScreen { IsLoaded: true } next && next != editor);
        AddStep("delete it", () => currentEditor.DeleteDifficulty());
        AddAssert("asked", () => dialogOverlay.CurrentDialog is DeleteDifficultyDialog);
        AddUntilStep("its buttons are up", () => dialogButtons().Count == 2);
        AddStep("yes (by key, which takes it at once)", () => dialogButtons()[0].Activate());
        AddUntilStep("back on the first", () => screenStack.CurrentScreen is EditorScreen { IsLoaded: true } next && next.Session.Beatmap.Beatmap.ID == 70);
        AddAssert("one difficulty left", () => collection.GetBeatmapsFromBeatmapSet(collection.GetBeatmapSetById(7)!).Length == 1);
    }

    [Test]
    public void TestSwitchingWithChangesAsks()
    {
        AddStep("save", () => editor.Save());
        AddStep("create a difficulty", () => editor.CreateNewDifficulty());
        AddStep("from scratch", () => dialogOverlay.CurrentDialog!.PerformOkAction());
        AddUntilStep("on it", () => screenStack.CurrentScreen is EditorScreen { IsLoaded: true } next && next != editor);
        AddStep("edit it", () => currentEditor.Session.Beatmap.Add(new BeatmapNote { StartTime = 1000, EndTime = 1000, Lane = NoteLane.Lane1 }));
        AddStep("pick the other difficulty", () =>
        {
            var change = currentEditor.TopBar.Menu.Items[0].Items.First(i => i.Text.Value == "Change difficulty");
            change.Items.First(i => i is ToggleMenuItem { State.Value: false }).Action!();
        });
        AddAssert("asked about the changes", () => dialogOverlay.CurrentDialog is PromptForSaveDialog);
    }

    private BeatmapSet previousSet = null!;
    private Beatmap previousBeatmap = null!;

    private void openNewSet()
    {
        AddStep("leave the default editor", () => editor.AttemptExit());
        AddUntilStep("left", () => screenStack.CurrentScreen != editor);
        AddStep("make a set from a song", () =>
        {
            previousSet = working.BeatmapSet;
            previousBeatmap = working.Beatmap;

            string songs = Path.Combine(tempDir, "songs");
            Directory.CreateDirectory(songs);

            working.Beatmap = BeatmapCreator.CreateSet(TestWav.Write(songs, "Fresh Song.wav", seconds: 2), new BeatmapWriter(storage, collection), collection);
            screenStack.Push(editor = new EditorScreen
            {
                RelativeSizeAxes = Axes.Both,
                InitialMode = EditorScreenMode.Setup,
                IsNewBeatmap = true,
                RestoreOnDiscard = (previousSet, previousBeatmap)
            });
        });
        AddUntilStep("the editor is up", () => editor.IsLoaded && screenStack.CurrentScreen == editor);
    }

    [Test]
    public void TestANewSetOpensOnSetup()
    {
        openNewSet();
        AddAssert("on setup", () => editor.Mode.Value == EditorScreenMode.Setup);
        AddAssert("named after the song", () => editor.Session.Beatmap.BeatmapSet.Title == "Fresh Song");
    }

    [Test]
    public void TestANewSetLeftUnsavedIsThrownAway()
    {
        openNewSet();
        AddStep("leave without touching it", () => editor.AttemptExit());
        AddAssert("asked anyway", () => dialogOverlay.CurrentDialog is PromptForSaveDialog);
        AddUntilStep("its buttons are up", () => dialogButtons().Count == 3);
        AddStep("forget it", () => dialogButtons()[1].Activate());
        AddUntilStep("left", () => screenStack.CurrentScreen != editor);
        AddAssert("gone from the library", () => collection.BeatmapSets.All(set => set.Title != "Fresh Song"));
        AddAssert("and from disk", () => !Directory.EnumerateDirectories(Path.Combine(tempDir, "beatmaps")).Any(d => d.Contains("Fresh Song")));
        AddAssert("song select back on what it had", () => working.BeatmapSet == previousSet && working.Beatmap == previousBeatmap);
    }

    [Test]
    public void TestANewSetSavedIsKept()
    {
        openNewSet();
        AddStep("save", () => editor.Save());
        AddStep("leave", () => editor.AttemptExit());
        AddUntilStep("left without asking", () => screenStack.CurrentScreen != editor && dialogOverlay.CurrentDialog == null);
        AddAssert("still in the library", () => collection.BeatmapSets.Any(set => set.Title == "Fresh Song"));
        AddAssert("song select on it", () => working.BeatmapSet.Title == "Fresh Song");
    }

    [Test]
    public void TestSongSelectMakesANewSet()
    {
        SongSelectionScreen songSelect = null!;

        AddStep("leave the default editor", () => editor.AttemptExit());
        AddUntilStep("left", () => screenStack.CurrentScreen != editor);
        AddStep("song select, to edit", () => screenStack.Push(songSelect = new SongSelectionScreen { RelativeSizeAxes = Axes.Both, EditMode = true }));
        AddUntilStep("it's up", () => songSelect.IsLoaded && screenStack.CurrentScreen == songSelect);
        AddStep("a new beatmap from a song", () =>
        {
            string songs = Path.Combine(tempDir, "songs");
            Directory.CreateDirectory(songs);
            songSelect.CreateBeatmapFrom(TestWav.Write(songs, "Brand New.wav"));
        });
        AddUntilStep("the editor is up on it", () => screenStack.CurrentScreen is EditorScreen { IsLoaded: true } next && next.Session.Beatmap.BeatmapSet.Title == "Brand New");
        AddAssert("on setup, as new", () => currentEditor.Mode.Value == EditorScreenMode.Setup && currentEditor.IsNewBeatmap);
        AddStep("save and leave", () =>
        {
            currentEditor.Save();
            currentEditor.AttemptExit();
        });
        AddUntilStep("back in song select", () => screenStack.CurrentScreen == songSelect);
        AddAssert("on the new set", () => working.BeatmapSet.Title == "Brand New");
    }

    // Stand-ins for picked files: their bytes are only copied, never decoded, on the Compose tab.
    private string fakeFile(string name, byte fill)
    {
        string folder = Path.Combine(tempDir, "picked");
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, 64).ToArray());
        return path;
    }

    [Test]
    public void TestTheCoverIsItsOwnImage()
    {
        AddStep("a background", () => editor.ChangeBackground(fakeFile("bg.png", 1)));
        AddAssert("the background, and only it", () =>
            editor.Session.Beatmap.BeatmapSet.BackgroundPath == "bg.png" && string.IsNullOrEmpty(editor.Session.Beatmap.BeatmapSet.CoverPath));
        AddStep("an album cover", () => editor.ChangeCover(fakeFile("cover.png", 2)));
        AddAssert("the cover", () => editor.Session.Beatmap.BeatmapSet.CoverPath == "cover.png" && storage.Exists(editor.Session.Beatmap.BeatmapSet.CoverStoragePath));
        AddAssert("the background stays", () => editor.Session.Beatmap.BeatmapSet.BackgroundPath == "bg.png");
        AddStep("undo", () => editor.Undo());
        AddAssert("no cover again", () => string.IsNullOrEmpty(editor.Session.Beatmap.BeatmapSet.CoverPath) && editor.Session.Beatmap.BeatmapSet.BackgroundPath == "bg.png");
        AddStep("redo, then save", () =>
        {
            editor.Redo();
            editor.Save();
        });
        AddAssert("the library has both", () =>
            collection.GetBeatmapSetById(7) is { CoverPath: "cover.png", BackgroundPath: "bg.png" });
    }

    [Test]
    public void TestAVideoCanBeAddedAndTakenAway()
    {
        AddStep("a video", () => editor.ChangeVideo(fakeFile("clip.mp4", 3)));
        AddAssert("the set has it", () =>
        {
            var set = editor.Session.Beatmap.BeatmapSet;
            return set.HasVideo && set.VideoPath == "clip.mp4" && storage.Exists(set.VideoStoragePath);
        });
        AddStep("take it away", () => editor.RemoveVideo());
        AddAssert("gone", () => !editor.Session.Beatmap.BeatmapSet.HasVideo && editor.Session.Beatmap.BeatmapSet.VideoPath == "");
        AddStep("undo", () => editor.Undo());
        AddAssert("back, where it was", () =>
        {
            var set = editor.Session.Beatmap.BeatmapSet;
            return set.HasVideo && set.VideoPath == "clip.mp4" && storage.Exists(set.VideoStoragePath);
        });
    }

    [Test]
    public void TestChangingTheSongCanBeUndone()
    {
        string firstTrack = null!;

        openNewSet();
        AddStep("note the song", () => firstTrack = editor.Session.Beatmap.BeatmapSet.TrackStoragePath);
        AddStep("pick another", () =>
        {
            string other = Path.Combine(tempDir, "other");
            Directory.CreateDirectory(other);
            editor.ChangeTrack(TestWav.Write(other, "Other Song.wav", seconds: 3, frequency: 330));
        });
        AddAssert("it's the set's now", () => editor.Session.Beatmap.BeatmapSet.TrackPath == "Other Song.wav");
        AddAssert("copied in", () => storage.Exists(editor.Session.Beatmap.BeatmapSet.TrackStoragePath));
        AddAssert("its length", () => System.Math.Abs(editor.Session.Beatmap.BeatmapSet.TotalLength - 3000) < 50);
        AddStep("undo", () => editor.Undo());
        AddAssert("the first song again", () => editor.Session.Beatmap.BeatmapSet.TrackStoragePath == firstTrack && editor.Session.Beatmap.BeatmapSet.TrackPath == "Fresh Song.wav");
    }

    // The fixture's chart is 120 BPM with a beat on 0: beats every 500 ms.
    [Test]
    public void TestTheMetronomeTicksOnTheBeat()
    {
        int before = 0;

        AddStep("on the timing tab", () => editor.Mode.Value = EditorScreenMode.Timing);
        AddUntilStep("showing", () => editor.TimingTab.Alpha > 0.5f);
        AddStep("play from just before a beat", () =>
        {
            before = editor.TimingTab.Metronome.TicksPlayed;
            editor.Session.Clock.Seek(980);
            editor.Session.Clock.Start();
        });
        AddUntilStep("it ticks as the beat goes by", () => editor.TimingTab.Metronome.TicksPlayed > before);
        AddAssert("and swings", () => editor.TimingTab.Metronome.IsSwinging);
        AddStep("stop", () => editor.Session.Clock.Stop());
        AddUntilStep("it settles", () => !editor.TimingTab.Metronome.IsSwinging);
    }

    [Test]
    public void TestTheMetronomeIsQuietOnOtherTabs()
    {
        int before = 0;

        AddStep("play through a beat on compose", () =>
        {
            before = editor.TimingTab.Metronome.TicksPlayed;
            editor.Session.Clock.Seek(980);
            editor.Session.Clock.Start();
        });
        AddUntilStep("past the beat", () => editor.Session.Clock.CurrentTime > 1050);
        AddAssert("no tick", () => editor.TimingTab.Metronome.TicksPlayed == before);
        AddStep("stop", () => editor.Session.Clock.Stop());
    }

    [Test]
    public void TestASeekIsNotABeat()
    {
        int before = 0;

        AddStep("on the timing tab", () => editor.Mode.Value = EditorScreenMode.Timing);
        AddUntilStep("showing", () => editor.TimingTab.Alpha > 0.5f);
        AddStep("play mid-beat", () =>
        {
            editor.Session.Clock.Seek(1100);
            editor.Session.Clock.Start();
        });
        AddUntilStep("running", () => editor.Session.Clock.CurrentTime > 1110);
        AddStep("jump ahead several beats", () =>
        {
            before = editor.TimingTab.Metronome.TicksPlayed;
            editor.Session.Clock.Seek(3120);
        });
        AddUntilStep("landed", () => editor.Session.Clock.CurrentTime >= 3120);
        AddAssert("no tick for the beats jumped over", () => editor.TimingTab.Metronome.TicksPlayed == before);
        AddStep("stop", () => editor.Session.Clock.Stop());
    }

    [Test]
    public void TestUndoKeepsWhereTheSetLives()
    {
        AddStep("save once", () => editor.Save());
        AddStep("retitle", () => editor.Session.Beatmap.UpdateMetadata(b => b.BeatmapSet.Title = "Retitled"));
        AddStep("undo", () => editor.Undo());
        AddAssert("title back", () => editor.Session.Beatmap.BeatmapSet.Title == "Song");
        AddAssert("folder kept", () => !string.IsNullOrEmpty(editor.Session.Beatmap.BeatmapSet.StorageFolder));
        AddStep("save again", () => editor.Save());
        AddAssert("still one set folder", () => Directory.GetFiles(tempDir, "*.yks", SearchOption.AllDirectories).Length == 1);
    }
}
