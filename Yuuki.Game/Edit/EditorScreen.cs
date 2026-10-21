// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Screens;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Edit.Components;
using Yuuki.Game.Edit.Compose;
using Yuuki.Game.Edit.Setup;
using Yuuki.Game.Edit.Timeline;
using Yuuki.Game.Edit.Timing;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.Text;
using Yuuki.Game.Graphics.UserInterface.Notifications;
using Yuuki.Game.Graphics.UserInterface.Overlay;
using Yuuki.Game.Utilities;
using Waveform = Sakura.Framework.Audio.Waveform;

namespace Yuuki.Game.Edit;

/// <summary>
/// The beatmap editor
/// </summary>
public partial class EditorScreen : YuukiScreen
{
    private const float margin = 10;
    private const float gap = 8;
    private const float zoom_width = 36;

    public override YuukiScreenPart ScreenPart => YuukiScreenPart.Edit;

    public override bool HideFooter => true;

    public override bool DimBackground => true;

    public override bool AllowTrackChange => false;

    [Resolved]
    private YuukiScreenStack screenStack { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private WorkingBeatmap? workingBeatmap { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiAudioManager? audioManager { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapNoteStore? noteStore { get; set; }

    [Resolved(canBeNull: true)]
    private BeatmapWriter? writer { get; set; }

    [Resolved(canBeNull: true)]
    private DialogOverlay? dialogOverlay { get; set; }

    [Resolved(canBeNull: true)]
    private NotificationOverlay? notifications { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiConfigManager? config { get; set; }

    [Resolved(canBeNull: true)]
    private EditorClipboard? clipboard { get; set; }

    [Resolved(canBeNull: true)]
    private Storage? storage { get; set; }

    [Resolved(canBeNull: true)]
    private IWindow? window { get; set; }

    /// <summary>
    /// What's being edited, with its history, clock and the mapper's choices.
    /// </summary>
    public EditorSession Session { get; private set; } = null!;

    /// <summary>
    /// Which tab is showing.
    /// </summary>
    public Reactive<EditorScreenMode> Mode { get; } = new Reactive<EditorScreenMode>(EditorScreenMode.Compose);

    /// <summary>
    /// The tab to open on: Setup for a new set; the same tab when moving to another
    /// difficulty.
    /// </summary>
    public EditorScreenMode InitialMode { get; init; } = EditorScreenMode.Compose;

    /// <summary>
    /// Where in the song to open, in ms, or null for just before the first note.
    /// </summary>
    public double? InitialTime { get; init; }

    /// <summary>
    /// A set just made for this session (<see cref="BeatmapCreator.CreateSet"/>) and never saved since:
    /// leaving always asks, and leaving without saving deletes it again.
    /// </summary>
    public bool IsNewBeatmap { get; init; }

    /// <summary>
    /// What the working beatmap goes back to when a new set is thrown away (what song select had picked).
    /// </summary>
    public (BeatmapSet? Set, Beatmap? Beatmap) RestoreOnDiscard { get; init; }

    /// <summary>
    /// Whether this editor is leaving for another difficulty of its set, rather than for song select.
    /// </summary>
    public bool SwitchingDifficulty { get; private set; }

    /// <summary>
    /// The song's waveform, owned by the timeline; Setup shows it too.
    /// </summary>
    public Waveform? CurrentWaveform { get; private set; }

    [Resolved(canBeNull: true)]
    private BeatmapsCollection? collection { get; set; }

    [Resolved(canBeNull: true)]
    private YuukiBackground? background { get; set; }

    private Beatmap sourceBeatmap = null!;
    private IAudioChannel? channel;
    private bool notesLoaded;
    private double audioOffset;

    // The song file the clock and waveform are on, and the background on show, to notice a change
    // (picked in Setup, or undone).
    private string? loadedTrack;
    private string? shownBackground;
    private bool backgroundPreviewed;

    // Whether anything was saved, so song select gets the library's new copy on the way out.
    private bool savedAny;

    private bool isNewBeatmap;
    private bool discardNewSet;
    private bool exitConfirmed;

    // Where a test play started, to come back to.
    private double testStartTime;

    private EditorTopBar topBar = null!;
    private EditorTimeline timeline = null!;
    private BeatDivisorControl divisorControl = null!;
    private Container zoomButtons = null!;
    private Container content = null!;
    private EditorBottomBar bottomBar = null!;
    private SavedToast savedToast = null!;

    public ComposeScreen Compose { get; private set; } = null!;

    /// <summary>
    /// The menus and tabs along the top.
    /// </summary>
    public EditorTopBar TopBar => topBar;

    /// <summary>
    /// Where right-click menus open: over the whole editor, so a sub-menu can hang anywhere on
    /// screen and still take the mouse.
    /// </summary>
    public EditorContextMenuContainer ContextMenus { get; private set; } = null!;

    private TimingScreen timingScreen = null!;

    /// <summary>
    /// The Timing tab.
    /// </summary>
    public TimingScreen TimingTab => timingScreen;
    private SetupScreen setupScreen = null!;

    // Menu items that follow the editor's state.
    private MenuItem undoItem = null!;
    private MenuItem redoItem = null!;
    private MenuItem cutItem = null!;
    private MenuItem copyItem = null!;
    private MenuItem pasteItem = null!;
    private MenuItem cloneItem = null!;
    private MenuItem deleteItem = null!;
    private MenuItem openFolderItem = null!;
    private MenuItem openFileItem = null!;
    private MenuItem deleteDifficultyItem = null!;
    private MenuItem? currentDifficultyItem;

    [Resolved(canBeNull: true)]
    private GameLayout? gameLayout { get; set; }

    [BackgroundDependencyLoader]
    private void load()
    {
        ScreenName.Value = "Edit";
        ScreenHelperText.Value = "Make it yours!";

        sourceBeatmap = workingBeatmap?.Beatmap ?? throw new InvalidOperationException("The editor needs a working beatmap.");
        isNewBeatmap = IsNewBeatmap;
        Mode.Value = InitialMode;

        // The library keeps metadata only; the chart is read for this session and released on the way out.
        var notes = noteStore?.Load(sourceBeatmap) ?? sourceBeatmap.Notes;
        notesLoaded = noteStore != null && !string.IsNullOrEmpty(sourceBeatmap.NotesStoragePath);

        var beatmap = new EditorBeatmap(sourceBeatmap, notes);

        // A chart from before timing points existed plays on the set's BPM with a beat on its first note
        // (see PlayableBeatmap.FromBeatmap); the editor makes that explicit, so it saves as what it plays as.
        if (!beatmap.ControlPoints.HasTiming && beatmap.BeatmapSet.BPM > 0)
        {
            var legacy = ControlPointInfo.FromLegacyBpm(beatmap.BeatmapSet.BPM, beatmap.Notes.Count > 0 ? beatmap.Notes[0].StartTime : 0);

            foreach (var point in legacy.TimingPoints)
                beatmap.ControlPoints.Add(point.Clone());
        }

        // The editor's own copy of the set: the song it plays can change before the library's does.
        channel = audioManager?.BeginGameplay(beatmap.BeatmapSet);
        loadedTrack = beatmap.BeatmapSet.TrackStoragePath;
        shownBackground = beatmap.BeatmapSet.BackgroundStoragePath;
        audioOffset = config?.Get(YuukiSetting.AudioOffset, 0).Value ?? 0;

        Session = new EditorSession(beatmap, createClock);
        Cache(Session);

        if (gameLayout?.IsPhone == true)
            Session.TimelineSpan.Value = EditorSession.PHONE_TIMELINE_SPAN;

        Waveform? waveform = CurrentWaveform = createWaveform(beatmap.BeatmapSet);

        Add(ContextMenus = new EditorContextMenuContainer
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One
        });

        ContextMenus.AddRange(new Drawable[]
        {
            content = new Container
            {
                Children = new Drawable[]
                {
                    // Each fills the area between the timeline and the bottom bar (sized in Update).
                    Compose = new ComposeScreen(),
                    timingScreen = new TimingScreen { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Alpha = 0 },
                    setupScreen = new SetupScreen(this) { RelativeSizeAxes = Axes.Both, Size = Vector2.One, Alpha = 0 }
                }
            },
            zoomButtons = new Container
            {
                Size = new Vector2(zoom_width, EditorTimeline.HEIGHT),
                Children = new Drawable[]
                {
                    new EditorButton(IconUsage.ZoomIn)
                    {
                        Size = new Vector2(zoom_width, (EditorTimeline.HEIGHT - 6) / 2),
                        Action = () => timeline.Zoom(1)
                    },
                    new EditorButton(IconUsage.ZoomOut)
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Size = new Vector2(zoom_width, (EditorTimeline.HEIGHT - 6) / 2),
                        Action = () => timeline.Zoom(-1)
                    }
                }
            },
            timeline = new EditorTimeline(waveform) { Height = EditorTimeline.HEIGHT },
            divisorControl = new BeatDivisorControl(),
            bottomBar = new EditorBottomBar(TestPlay),
            savedToast = new SavedToast
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre
            },
            // Last, so the menus it opens hang over everything else.
            topBar = new EditorTopBar(createMenu(), Mode)
        });

        Mode.BindValueChanged(e => showMode(e.NewValue), true);
        Session.Beatmap.MetadataChanged += onMetadataChanged;

        Session.Changes.CanUndo.BindValueChanged(e => undoItem.Enabled.Value = e.NewValue, true);
        Session.Changes.CanRedo.BindValueChanged(e => redoItem.Enabled.Value = e.NewValue, true);
        Session.Beatmap.SelectionChanged += updateSelectionItems;
        updateSelectionItems();
        updateFileItems();
    }

    private EditorClock createClock(EditorSession session) =>
        new EditorClock(channel, trackLengthFor(session.Beatmap), () => session.Beatmap.ControlPoints, session.Divisor, audioOffset);

    private Waveform? createWaveform(BeatmapSet set)
    {
        try
        {
            string? track = set.TrackStoragePath;

            if (storage != null && !set.UseLocalSource && !string.IsNullOrEmpty(track) && storage.Exists(track))
                return new Waveform(storage.GetStream(track));
        }
        catch (Exception e)
        {
            Logger.Error($"Couldn't read the song for the editor's waveform: {e.Message}");
        }

        return null;
    }

    /// <summary>
    /// How long the song is: the track's length, or with no track, a minute past the last note.
    /// </summary>
    private double trackLengthFor(EditorBeatmap beatmap)
    {
        if (channel != null && channel.Length > 0)
            return channel.Length;

        double lastNote = beatmap.Notes.Count > 0 ? beatmap.Notes.Max(n => n.EndTime) : 0;
        return Math.Max(beatmap.BeatmapSet.TotalLength, lastNote + 60000);
    }

    #region Menu

    private IReadOnlyList<MenuItem> createMenu()
    {
        undoItem = new MenuItem("Undo", Undo) { ShortcutText = "Ctrl+Z" };
        redoItem = new MenuItem("Redo", Redo) { ShortcutText = "Ctrl+Y" };
        cutItem = new MenuItem("Cut", Cut) { ShortcutText = "Ctrl+X" };
        copyItem = new MenuItem("Copy", Copy) { ShortcutText = "Ctrl+C" };
        pasteItem = new MenuItem("Paste", Paste) { ShortcutText = "Ctrl+V" };
        cloneItem = new MenuItem("Clone", Clone) { ShortcutText = "Ctrl+D" };
        deleteItem = new MenuItem("Delete", DeleteSelection) { ShortcutText = "Del" };
        openFolderItem = new MenuItem("Open beatmap folder", OpenBeatmapFolder);
        openFileItem = new MenuItem("Open beatmap file", OpenBeatmapFile);

        var waveformItems = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }.Select(opacity =>
        {
            var item = new ToggleMenuItem($"{opacity * 100:0}%");
            item.Action = () => Session.WaveformOpacity.Value = opacity;
            Session.WaveformOpacity.BindValueChanged(e => item.State.Value = Math.Abs(e.NewValue - opacity) < 0.01f, true);
            return (MenuItem)item;
        }).ToArray();

        var seekAfterPlacing = new ToggleMenuItem("Seek after placing", Session.SeekAfterPlacing.Value);
        seekAfterPlacing.State.BindValueChanged(e => Session.SeekAfterPlacing.Value = e.NewValue);

        deleteDifficultyItem = new MenuItem("Delete difficulty...", DeleteDifficulty);

        return new[]
        {
            new MenuItem("File", new MenuItem[]
            {
                new MenuItem("Create new difficulty", CreateNewDifficulty),
                new MenuItem("Change difficulty", createDifficultyItems()),
                new MenuSeparator(),
                deleteDifficultyItem,
                new MenuSeparator(),
                new MenuItem("Save", () => Save()) { ShortcutText = "Ctrl+S" },
                new MenuItem("Test play", TestPlay) { ShortcutText = "F5" },
                new MenuSeparator(),
                openFolderItem,
                openFileItem,
                new MenuSeparator(),
                new MenuItem("Exit", AttemptExit) { ShortcutText = "Esc" }
            }),
            new MenuItem("Edit", new MenuItem[]
            {
                undoItem,
                redoItem,
                new MenuSeparator(),
                cutItem,
                copyItem,
                pasteItem,
                cloneItem,
                new MenuSeparator(),
                new MenuItem("Select all", SelectAll) { ShortcutText = "Ctrl+A" },
                deleteItem
            }),
            new MenuItem("View", new MenuItem[]
            {
                new MenuItem("Waveform opacity", waveformItems),
                seekAfterPlacing
            }),
            new MenuItem("Timing", new MenuItem[]
            {
                new MenuItem("Set preview point here", SetPreviewPointToCurrentTime),
                new MenuItem("Add timing point here", () =>
                {
                    Mode.Value = EditorScreenMode.Timing;
                    timingScreen.AddAtCurrentTime();
                })
            })
        };
    }

    // Only for what's in the library: a beatmap that was never saved has no folder or file yet.
    private void updateFileItems()
    {
        openFolderItem.Enabled.Value = beatmapFolder != null;
        openFileItem.Enabled.Value = beatmapFile != null;

        // a set keeps at least one difficulty.
        deleteDifficultyItem.Enabled.Value = writer != null && siblings().Length > 1;
    }

    /// <summary>
    /// The set's difficulties in the library, easiest first.
    /// </summary>
    private Beatmap[] siblings() => collection?.GetBeatmapsFromBeatmapSet(sourceBeatmap.BeatmapSet) ?? Array.Empty<Beatmap>();

    /// <summary>
    /// "Change difficulty": every difficulty of the set, this one ticked.
    /// </summary>
    private MenuItem[] createDifficultyItems()
    {
        var items = new List<MenuItem>();

        foreach (var beatmap in siblings())
        {
            bool current = beatmap.ID == sourceBeatmap.ID;
            var item = new ToggleMenuItem(difficultyLabel(beatmap.DifficultyName, beatmap.DifficultyRating), current);

            // Picking the one being edited does nothing; any other goes there.
            item.Action = current ? () => { } : () => leaveDifficultyThen(() => openDifficulty(beatmap));
            items.Add(item);

            if (current)
                currentDifficultyItem = item;
        }

        // A set with nothing in the library yet (one being made) still shows itself.
        if (items.Count == 0)
            items.Add(currentDifficultyItem = new ToggleMenuItem(difficultyLabel(sourceBeatmap.DifficultyName, sourceBeatmap.DifficultyRating), true) { Action = () => { } });

        return items.ToArray();
    }

    private static string difficultyLabel(string name, double rating) =>
        $"{(string.IsNullOrEmpty(name) ? "(unnamed)" : name)}  {rating:0.00}★";

    private void updateSelectionItems()
    {
        bool any = Session.Beatmap.SelectedNotes.Count > 0;

        cutItem.Enabled.Value = copyItem.Enabled.Value = cloneItem.Enabled.Value = deleteItem.Enabled.Value = any;
        pasteItem.Enabled.Value = clipboard?.HasContent == true;
    }

    #endregion

    #region Layout

    private void showMode(EditorScreenMode mode)
    {
        show(Compose, mode == EditorScreenMode.Compose);
        show(timingScreen, mode == EditorScreenMode.Timing);
        show(setupScreen, mode == EditorScreenMode.Setup);

        // Setup is a form; the song's timeline has nothing to do with it.
        bool timelineShown = mode != EditorScreenMode.Setup;

        foreach (var drawable in new Drawable[] { timeline, divisorControl, zoomButtons })
            drawable.FadeTo(timelineShown ? 1 : 0, 200, Easing.OutQuint);
    }

    private static void show(Drawable screen, bool shown)
    {
        screen.ClearTransforms();
        screen.FadeTo(shown ? 1 : 0, 200, Easing.OutQuint);
    }

    public override void UpdateSubTree()
    {
        // Before anything reads the time this frame.
        if (IsLoaded)
            Session.Clock.ProcessFrame();

        base.UpdateSubTree();
    }

    public override void Update()
    {
        base.Update();

        // A streamed track can learn its length after it has opened.
        if (channel != null && channel.Length > 0 && Math.Abs(Session.Clock.TrackLength - channel.Length) > 1)
            Session.Clock.TrackLength = channel.Length;

        float width = DrawWidth - margin * 2;

        topBar.Position = new Vector2(margin, margin);
        topBar.Width = width;

        float timelineY = margin + EditorTopBar.HEIGHT + gap;

        zoomButtons.Position = new Vector2(margin, timelineY);
        timeline.Position = new Vector2(margin + zoom_width + gap, timelineY);
        timeline.Width = Math.Max(0, width - zoom_width - gap * 2 - BeatDivisorControl.WIDTH);
        divisorControl.Position = new Vector2(DrawWidth - margin - BeatDivisorControl.WIDTH, timelineY);

        bool timelineShown = Mode.Value != EditorScreenMode.Setup;
        float contentY = timelineShown ? timelineY + EditorTimeline.HEIGHT + gap : timelineY;
        float bottomY = DrawHeight - margin - EditorBottomBar.HEIGHT;

        content.Position = new Vector2(0, contentY);
        content.Size = new Vector2(DrawWidth, Math.Max(0, bottomY - gap - contentY));

        bottomBar.Position = new Vector2(margin, bottomY);
        bottomBar.Width = width;

        savedToast.Y = timelineY + 6;
    }

    #endregion

    #region Lifecycle

    public override void OnEntering(Screen? last)
    {
        base.OnEntering(last);
        this.FadeInFromZero(300, Easing.OutQuint);

        // Where the last difficulty was, when coming from one.
        if (InitialTime is double time)
        {
            Session.Clock.Seek(time);
            return;
        }

        // Just before the first note (a beat of run-up), or the start for an empty chart.
        var notes = Session.Beatmap.Notes;

        if (notes.Count > 0)
        {
            var timing = Session.Beatmap.ControlPoints;
            double beat = timing.HasTiming ? timing.TimingPointAt(notes[0].StartTime).BeatLength : 500;
            Session.Clock.Seek(Math.Max(0, notes[0].StartTime - beat));
        }
    }

    public override void OnSuspending(Screen next)
    {
        base.OnSuspending(next);
        Session.Clock.Stop();
        this.FadeOut(200, Easing.OutQuint);
    }

    public override void OnResuming(Screen? last)
    {
        base.OnResuming(last);
        this.FadeIn(200, Easing.OutQuint);

        if (last is EditorTestPlayScreen)
        {
            // Test play set its own speed on the shared channel; put the editor's back, and its place.
            Session.Clock.Rate = Session.Clock.Rate;
            Session.Clock.Seek(testStartTime);
        }
    }

    public override void OnExiting(Screen? next)
    {
        base.OnExiting(next);

        Session.Clock.Stop();
        Session.Beatmap.SelectionChanged -= updateSelectionItems;
        Session.Beatmap.MetadataChanged -= onMetadataChanged;

        if (notesLoaded)
            noteStore?.Unload(sourceBeatmap);

        this.FadeOut(300, Easing.OutQuint);

        // Another difficulty's editor takes over the song straight away; song select gets it back when
        // the last one leaves.
        if (SwitchingDifficulty)
            return;

        audioManager?.EndGameplay();

        if (discardNewSet)
        {
            // Thrown away: gone from disk and the library, and song select back on what it had.
            writer?.Delete(Session.Beatmap.BeatmapSet);

            if (workingBeatmap != null && RestoreOnDiscard.Set != null)
            {
                workingBeatmap.BeatmapSet = RestoreOnDiscard.Set;
                workingBeatmap.Beatmap = RestoreOnDiscard.Beatmap!;
            }
        }
        else if (savedAny && workingBeatmap != null && collection?.GetBeatmapById(sourceBeatmap.ID) is Beatmap saved)
        {
            // Song select picks up what was saved (the library holds new objects for it now).
            workingBeatmap.BeatmapSet = saved.BeatmapSet;
            workingBeatmap.Beatmap = saved;
        }

        // A background picked but not kept was only ever on show.
        if (backgroundPreviewed && workingBeatmap?.BeatmapSet != null)
            background?.ShowSet(workingBeatmap.BeatmapSet);
    }

    // Leaving would lose something: unsaved changes, or a new set that has never been saved at all.
    private bool hasSomethingToLose => Session.Changes.HasUnsavedChanges.Value || isNewBeatmap;

    /// <summary>
    /// Leaves, asking first when there are unsaved changes (or the set is new).
    /// </summary>
    public void AttemptExit()
    {
        if (exitConfirmed || !hasSomethingToLose || dialogOverlay == null)
        {
            exitConfirmed = true;
            Exit();
            return;
        }

        promptForSave(() => Exit());
    }

    public override bool InterceptGameExit(Action resume)
    {
        if (!IsLoaded || exitConfirmed || !hasSomethingToLose || dialogOverlay == null)
            return false;

        // Asked already: this is the second close in a row, so the question stands.
        if (dialogOverlay.CurrentDialog is PromptForSaveDialog)
            return true;

        // Back from test play first, so the question is asked over the editor.
        while (screenStack.CurrentScreen is not null && screenStack.CurrentScreen != this)
            screenStack.CurrentScreen.Exit();

        promptForSave(resume);
        return true;
    }

    private void promptForSave(Action leave)
    {
        dialogOverlay!.Push(new PromptForSaveDialog(
            exit: () =>
            {
                exitConfirmed = true;

                discardNewSet = isNewBeatmap;
                leave();
            },
            saveAndExit: () =>
            {
                if (!Save())
                    return;

                exitConfirmed = true;
                leave();
            }));
    }

    #endregion

    #region Saving

    /// <summary>
    /// Writes the difficulty and its set to the library. Returns false (after saying why) if it couldn't.
    /// </summary>
    public bool Save()
    {
        if (writer == null || Session.Beatmap.BeatmapSet.UseLocalSource)
        {
            notifications?.Post(new SimpleErrorNotification { Text = "This beatmap can't be saved: it isn't in your library." });
            return false;
        }

        try
        {
            var beatmap = Session.Beatmap.CreateBeatmapForSave();
            var set = beatmap.BeatmapSet;

            // The tempo song select shows for the set: the difficulty's main one.
            if (beatmap.ControlPoints.HasTiming)
                set.BPM = (float)beatmap.ControlPoints.GetMostCommonBpm(beatmap.Notes.Length > 0 ? beatmap.Notes.Max(n => n.EndTime) : 0);

            writer.Save(set);
            writer.Save(beatmap);

            // The working copy follows where the writer put things (a renamed difficulty is a new file).
            Session.Beatmap.Beatmap.NotesStoragePath = beatmap.NotesStoragePath;
            Session.Beatmap.BeatmapSet.StorageFolder = set.StorageFolder;

            // The library keeps metadata only; the chart is read again from the file when it's played.
            beatmap.Notes = Array.Empty<BeatmapNote>();
            savedAny = true;
            isNewBeatmap = false;

            Session.Changes.MarkSaved();
            updateFileItems();
            savedToast.Flash();
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't save the beatmap: {e.Message}" });
            return false;
        }
    }

    #endregion

    #region Difficulties

    /// <summary>
    /// "Create new difficulty": blank (keeping the timing) or a copy of this one, saved into the
    /// set, then opened.
    /// </summary>
    public void CreateNewDifficulty()
    {
        if (writer == null || collection == null)
            return;

        if (dialogOverlay == null)
        {
            createDifficulty(false);
            return;
        }

        dialogOverlay.Push(new CreateNewDifficultyDialog(createDifficulty));
    }

    private void createDifficulty(bool copy)
    {
        // As it is now, unsaved edits and all; then this one is settled.
        var current = Session.Beatmap.CreateBeatmapForSave();

        leaveDifficultyThen(() =>
        {
            current.BeatmapSet = collection!.GetBeatmapSetById(current.BeatmapSet.ID) ?? current.BeatmapSet;
            var created = BeatmapCreator.CreateDifficulty(current, copy, current.Notes, writer!, collection);
            openDifficulty(created);
        });
    }

    /// <summary>
    /// "Delete difficulty": this one, from disk and the library, after a held confirmation; the
    /// editor moves to the next easier one (or the next harder, if this was the easiest).
    /// </summary>
    public void DeleteDifficulty()
    {
        var all = siblings();

        if (writer == null || all.Length < 2)
            return;

        void delete()
        {
            int index = Array.FindIndex(all, b => b.ID == sourceBeatmap.ID);
            var next = all[index <= 0 ? 1 : index - 1];

            writer.Delete(all[Math.Max(0, index)]);

            // Nothing left here to save: the difficulty is gone.
            exitConfirmed = true;
            openDifficulty(next);
        }

        if (dialogOverlay == null)
            delete();
        else
            dialogOverlay.Push(new DeleteDifficultyDialog(Session.Beatmap.Beatmap.DifficultyName, Session.Beatmap.Notes.Count, delete));
    }

    /// <summary>
    /// Settles this difficulty before going to another: a new set is saved first (another difficulty
    /// needs the set on disk), and unsaved changes are asked about.
    /// </summary>
    private void leaveDifficultyThen(Action next)
    {
        if (dialogOverlay == null)
        {
            next();
            return;
        }

        if (isNewBeatmap)
        {
            dialogOverlay.Push(new SaveRequiredDialog(() =>
            {
                if (Save())
                    next();
            }));
            return;
        }

        if (Session.Changes.HasUnsavedChanges.Value)
        {
            promptForSave(next);
            return;
        }

        next();
    }

    /// <summary>
    /// Hands over to an editor for <paramref name="beatmap"/> (another difficulty of this set), on the
    /// same tab and at the same time.
    /// </summary>
    private void openDifficulty(Beatmap beatmap)
    {
        if (!IsCurrentScreen)
            return;

        SwitchingDifficulty = true;
        exitConfirmed = true;

        // Only the difficulty: changing the working set would start song select's preview of it.
        if (workingBeatmap != null)
            workingBeatmap.Beatmap = beatmap;

        var next = new EditorScreen
        {
            RelativeSizeAxes = Axes.Both,
            InitialMode = Mode.Value,
            InitialTime = Session.Clock.CurrentTime
        };

        Exit();
        screenStack.Push(next);
    }

    #endregion

    #region Song and background

    /// <summary>
    /// Makes the song at <paramref name="path"/> the set's: copied into its folder and played from now
    /// on. Its tags fill in the artist and title (here only where they're still empty).
    /// Undoable; the old song stays in the folder until a save.
    /// </summary>
    public bool ChangeTrack(string path)
    {
        if (!canChangeFiles())
            return false;

        try
        {
            var tags = SongTags.Read(path);
            string name = writer!.AddFile(Session.Beatmap.BeatmapSet, path);

            Session.Beatmap.UpdateMetadata(b =>
            {
                var set = b.BeatmapSet;
                set.TrackPath = name;
                set.TrackStoragePath = BeatmapWriter.StoragePathOf(set, name);

                if (tags.Length > 0)
                    set.TotalLength = (int)tags.Length;

                if (string.IsNullOrWhiteSpace(set.ArtistUnicode) && !string.IsNullOrEmpty(tags.Artist))
                    set.ArtistUnicode = set.Artist = tags.Artist;

                if (string.IsNullOrWhiteSpace(set.TitleUnicode) && !string.IsNullOrEmpty(tags.Title))
                    set.TitleUnicode = set.Title = tags.Title;
            });

            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't use that song: {e.Message}" });
            return false;
        }
    }

    /// <summary>
    /// Makes the image at <paramref name="path"/> the set's background, copied into its folder and shown
    /// behind the editor straight away. The album cover is its own image and stays. Undoable.
    /// </summary>
    public bool ChangeBackground(string path) => changeFile(path, "image", (set, name) =>
    {
        set.BackgroundPath = name;
        set.BackgroundStoragePath = BeatmapWriter.StoragePathOf(set, name);
        set.Background = null!;
    });

    /// <summary>
    /// Makes the image at <paramref name="path"/> the set's album cover: the square art on song select's
    /// cards and the now-playing panel. Undoable.
    /// </summary>
    public bool ChangeCover(string path) => changeFile(path, "image", (set, name) =>
    {
        set.CoverPath = name;
        set.CoverStoragePath = BeatmapWriter.StoragePathOf(set, name);
        set.Cover = null!;
    });

    /// <summary>
    /// Makes the video at <paramref name="path"/> the set's, played behind the menus and gameplay in
    /// time with the song. Undoable.
    /// </summary>
    public bool ChangeVideo(string path) => changeFile(path, "video", (set, name) =>
    {
        set.VideoPath = name;
        set.VideoStoragePath = BeatmapWriter.StoragePathOf(set, name);
        set.HasVideo = true;
    });

    /// <summary>
    /// Takes the set's video away (the file stays in the folder). Undoable.
    /// </summary>
    public void RemoveVideo()
    {
        if (!Session.Beatmap.BeatmapSet.HasVideo && string.IsNullOrEmpty(Session.Beatmap.BeatmapSet.VideoPath))
            return;

        Session.Beatmap.UpdateMetadata(b =>
        {
            b.BeatmapSet.VideoPath = "";
            b.BeatmapSet.VideoStoragePath = null!;
            b.BeatmapSet.HasVideo = false;
        });
    }

    // Copies a picked file into the set's folder and points the set at it, as one undoable edit.
    private bool changeFile(string path, string kind, Action<BeatmapSet, string> use)
    {
        if (!canChangeFiles())
            return false;

        try
        {
            string name = writer!.AddFile(Session.Beatmap.BeatmapSet, path);
            Session.Beatmap.UpdateMetadata(b => use(b.BeatmapSet, name));
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't use that {kind}: {e.Message}" });
            return false;
        }
    }

    private bool canChangeFiles()
    {
        if (writer != null && !Session.Beatmap.BeatmapSet.UseLocalSource)
            return true;

        notifications?.Post(new SimpleErrorNotification { Text = "This beatmap's files can't be changed: it isn't in your library." });
        return false;
    }

    // After any metadata change (a pick in Setup, an undo or redo): play the song and show the
    // background the set now names, if they're not the ones already up.
    private void onMetadataChanged()
    {
        var set = Session.Beatmap.BeatmapSet;

        if (set.TrackStoragePath != loadedTrack)
            reloadTrack();

        if (set.BackgroundStoragePath != shownBackground)
        {
            shownBackground = set.BackgroundStoragePath;
            backgroundPreviewed = true;
            background?.ShowSet(set);
        }

        if (currentDifficultyItem != null)
            currentDifficultyItem.Text.Value = difficultyLabel(Session.Beatmap.Beatmap.DifficultyName, Session.Beatmap.Beatmap.DifficultyRating);
    }

    private void reloadTrack()
    {
        var set = Session.Beatmap.BeatmapSet;

        // The old clock first: the new channel replaces (and disposes) the one it's on.
        Session.Clock.Stop();

        channel = audioManager?.BeginGameplay(set);
        loadedTrack = set.TrackStoragePath;

        Session.ReplaceClock(createClock(Session));

        // Setup's copy first: the timeline disposes the old one as it takes the new.
        CurrentWaveform = createWaveform(set);
        setupScreen.ShowWaveform(CurrentWaveform);
        timeline.SetWaveform(CurrentWaveform);
    }

    #endregion

    #region Commands

    public void Undo()
    {
        if (!Session.Beatmap.InTransaction)
            Session.Changes.Undo();
    }

    public void Redo()
    {
        if (!Session.Beatmap.InTransaction)
            Session.Changes.Redo();
    }

    public void Copy()
    {
        var selection = Session.Beatmap.SelectedNotes;

        // Ctrl+C always puts the timestamp on the system clipboard.
        window?.SetClipboardText(EditorClipboard.Timestamp(Session.Clock.CurrentTime, selection));

        if (selection.Count > 0)
            clipboard?.Copy(selection);

        updateSelectionItems();
    }

    public void Cut()
    {
        if (Session.Beatmap.SelectedNotes.Count == 0)
            return;

        Copy();
        DeleteSelection();
    }

    /// <summary>
    /// Pastes at the playhead (snapped), selecting what was pasted.
    /// </summary>
    public void Paste()
    {
        if (clipboard == null)
            return;

        var notes = clipboard.Paste(Session.Snap(Session.Clock.CurrentTime), Session.Beatmap.Beatmap.LaneCount);

        if (notes.Length == 0)
            return;

        Session.Beatmap.BeginChange();
        Session.Beatmap.AddRange(notes);
        Session.Beatmap.Select(notes);
        Session.Beatmap.EndChange();
    }

    /// <summary>
    /// Clone: copy, then paste at the playhead.
    /// </summary>
    public void Clone()
    {
        if (Session.Beatmap.SelectedNotes.Count == 0)
            return;

        Copy();
        Paste();
    }

    public void SelectAll() => Session.Beatmap.Select(Session.Beatmap.Notes);

    public void DeleteSelection()
    {
        var selection = Session.Beatmap.SelectedNotes.ToArray();

        if (selection.Length > 0)
            Session.Beatmap.RemoveRange(selection);
    }

    /// <summary>
    /// The set's folder in the library, from the storage root, or null when it isn't there.
    /// </summary>
    private string? beatmapFolder
    {
        get
        {
            var set = Session.Beatmap.BeatmapSet;

            if (storage == null || set.UseLocalSource || string.IsNullOrEmpty(set.StorageFolder))
                return null;

            string path = $"{BeatmapSetUtility.BEATMAP_FOLDER_NAME}/{set.StorageFolder}";
            return storage.ExistsDirectory(path) ? path : null;
        }
    }

    /// <summary>
    /// The difficulty's file in the library, or null when it hasn't been saved there.
    /// </summary>
    private string? beatmapFile
    {
        get
        {
            string? path = Session.Beatmap.Beatmap.NotesStoragePath;
            return storage != null && !string.IsNullOrEmpty(path) && storage.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Shows the set's folder in the system's file browser ("Open Song Folder").
    /// </summary>
    public void OpenBeatmapFolder()
    {
        if (beatmapFolder is string folder)
            storage!.OpenFileExternally(folder);
    }

    /// <summary>
    /// Opens the difficulty's file (as last saved) in whatever the system opens .ykb files with.
    /// The first time, the system asks which app to use.
    /// </summary>
    public void OpenBeatmapFile()
    {
        if (beatmapFile is string file)
            storage!.OpenFileExternally(file);
    }

    public void SetPreviewPointToCurrentTime() =>
        Session.Beatmap.UpdateMetadata(b => b.BeatmapSet.PreviewTime = (int)Session.Clock.CurrentTime);

    /// <summary>
    /// Plays the chart from the playhead on the 3D highway (F5).
    /// </summary>
    public void TestPlay()
    {
        if (!IsCurrentScreen)
            return;

        testStartTime = Session.Clock.CurrentTime;
        Session.Clock.Stop();

        screenStack.Push(new EditorTestPlayScreen(Session.Beatmap.CreatePlayableBeatmap(), testStartTime, channel)
        {
            RelativeSizeAxes = Axes.Both
        });
    }

    private void seekToNote(int direction)
    {
        double now = Session.Clock.CurrentTimeAccurate;
        var notes = Session.Beatmap.Notes;

        var target = direction > 0
            ? notes.FirstOrDefault(n => n.StartTime > now + 1)
            : notes.LastOrDefault(n => n.StartTime < now - 1);

        if (target != null)
            Session.Clock.SeekSmoothlyTo(target.StartTime);
    }

    private void seekToTimingPoint(int direction)
    {
        double now = Session.Clock.CurrentTimeAccurate;
        var points = Session.Beatmap.ControlPoints.TimingPoints;

        var target = direction > 0
            ? points.FirstOrDefault(p => p.Time > now + 1)
            : points.LastOrDefault(p => p.Time < now - 1);

        if (target != null)
            Session.Clock.SeekSmoothlyTo(target.Time);
    }

    #endregion

    #region Input

    // Ctrl, or Cmd on macOS (which arrives as the Windows key).
    private bool commandHeld(KeyEvent e)
    {
        if ((e.Modifiers & KeyModifiers.Control) != 0)
            return true;

        var state = GetContainingInputManager()?.CurrentState;
        return state?.IsPressed(Key.WinLeft) == true || state?.IsPressed(Key.WinRight) == true;
    }

    // Typing in a field (Setup, Timing) is typing, not shortcuts.
    private bool typing => GetContainingFocusManager()?.FocusedDrawable is Sakura.Framework.Graphics.UserInterface.TextBox;

    public override bool OnKeyDown(KeyEvent e)
    {
        if (typing)
            return base.OnKeyDown(e);

        bool command = commandHeld(e);
        bool shift = (e.Modifiers & KeyModifiers.Shift) != 0;

        if (command)
        {
            switch (e.Key)
            {
                case Key.S when !e.IsRepeat:
                    Save();
                    return true;

                case Key.Z when shift:
                case Key.Y:
                    Redo();
                    return true;

                case Key.Z:
                    Undo();
                    return true;

                case Key.C when !e.IsRepeat:
                    Copy();
                    return true;

                case Key.X when !e.IsRepeat:
                    Cut();
                    return true;

                case Key.V when !e.IsRepeat:
                    Paste();
                    return true;

                case Key.D when !e.IsRepeat:
                    Clone();
                    return true;

                case Key.A when !e.IsRepeat:
                    SelectAll();
                    return true;

                case Key.Left:
                    seekToNote(-1);
                    return true;

                case Key.Right:
                    seekToNote(1);
                    return true;
            }

            return base.OnKeyDown(e);
        }

        switch (e.Key)
        {
            case Key.Escape when !e.IsRepeat:
                // A selection goes first; then the editor.
                if (Session.Beatmap.SelectedNotes.Count > 0)
                    Session.Beatmap.ClearSelection();
                else
                    AttemptExit();
                return true;

            case Key.Space when !e.IsRepeat:
            case Key.C when !e.IsRepeat:
                Session.Clock.Toggle();
                return true;

            case Key.Left:
                Session.Clock.SeekBackward(!Session.Clock.IsRunning);
                return true;

            case Key.Right:
                Session.Clock.SeekForward(!Session.Clock.IsRunning);
                return true;

            case Key.Up:
                seekToTimingPoint(-1);
                return true;

            case Key.Down:
                seekToTimingPoint(1);
                return true;

            case Key.Z when !e.IsRepeat:
                Session.Clock.SeekSmoothlyTo(0);
                return true;

            case Key.X when !e.IsRepeat:
                Session.Clock.Seek(0);
                Session.Clock.Start();
                return true;

            case Key.V when !e.IsRepeat:
                Session.Clock.SeekSmoothlyTo(Session.Clock.TrackLength);
                return true;

            case Key.F1:
                Mode.Value = EditorScreenMode.Compose;
                return true;

            case Key.F3:
                Mode.Value = EditorScreenMode.Timing;
                return true;

            case Key.F4:
                Mode.Value = EditorScreenMode.Setup;
                return true;

            case Key.F5 when !e.IsRepeat:
                TestPlay();
                return true;

            case Key.Delete:
            case Key.BackSpace:
                DeleteSelection();
                return true;
        }

        if (Mode.Value == EditorScreenMode.Compose && !e.IsRepeat && handleComposeKey(e.Key))
            return true;

        return base.OnKeyDown(e);
    }

    // The tool keys (1–7) and hitsound toggles (W/E/R), compose keys.
    private bool handleComposeKey(Key key)
    {
        switch (key)
        {
            case >= Key.Number1 and <= Key.Number7:
                Session.Tool.Value = (ComposeTool)(key - Key.Number1);
                return true;

            case Key.W:
                Compose.ToggleAddition(HitSampleAddition.Whistle);
                return true;

            case Key.E:
                Compose.ToggleAddition(HitSampleAddition.Finish);
                return true;

            case Key.R:
                Compose.ToggleAddition(HitSampleAddition.Clap);
                return true;
        }

        return false;
    }

    #endregion

    /// <summary>
    /// A pill that flashes up under the top bar on save.
    /// </summary>
    private partial class SavedToast : Container
    {
        public SavedToast()
        {
            Masking = true;
            CornerRadius = 15;
            Height = 30;
            AutoSizeAxes = Axes.X;
            Alpha = 0;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                    Color = EditorColors.PREVIEW_POINT.Darken(0.3f)
                },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "Saved",
                    Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, 15, FontWeight.Bold),
                    Color = Color.White,
                    Margin = new MarginPadding { Left = 18, Right = 18 }
                }
            };
        }

        public void Flash()
        {
            ClearTransforms();
            Alpha = 1;
            this.Wait(1200).FadeOut(400, Easing.OutQuint);
        }
    }
}
