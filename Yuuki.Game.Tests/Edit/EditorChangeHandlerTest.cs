// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Edit;

namespace Yuuki.Game.Tests.Edit;

[TestFixture]
public class EditorChangeHandlerTest
{
    private Beatmap source = null!;
    private EditorBeatmap beatmap = null!;
    private EditorChangeHandler changes = null!;

    [SetUp]
    public void SetUp()
    {
        source = new Beatmap
        {
            ID = 1,
            BeatmapSet = new BeatmapSet { ID = 1, Title = "Song", Artist = "Artist" },
            DifficultyName = "Normal",
            LaneCount = 4,
            ControlPoints = ControlPointInfo.FromBpm(120)
        };

        beatmap = new EditorBeatmap(source, new[]
        {
            note(1000, NoteLane.Lane1),
            note(500, NoteLane.Lane2)
        });

        changes = new EditorChangeHandler(beatmap);
    }

    private static BeatmapNote note(double time, NoteLane lane) => new BeatmapNote { StartTime = time, EndTime = time, Lane = lane };

    [Test]
    public void TheWorkingCopyLeavesTheLibraryAlone()
    {
        beatmap.Add(note(1500, NoteLane.Lane3));
        beatmap.UpdateTiming(t => t.Add(new TimingPoint(2000, 400)));
        beatmap.UpdateMetadata(b => b.BeatmapSet.Title = "Edited");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Notes, Is.Empty);
            Assert.That(source.ControlPoints.TimingPoints, Has.Count.EqualTo(1));
            Assert.That(source.BeatmapSet.Title, Is.EqualTo("Song"));
        }
    }

    [Test]
    public void NotesStayInTimeOrder()
    {
        beatmap.Add(note(750, NoteLane.Lane3));
        beatmap.Update(new[] { beatmap.Notes[0] }, n => n.StartTime = n.EndTime = 2000);

        Assert.That(beatmap.Notes.Select(n => n.StartTime), Is.EqualTo(new[] { 750.0, 1000, 2000 }));
    }

    [Test]
    public void UndoAndRedoStepThroughEdits()
    {
        beatmap.Add(note(1500, NoteLane.Lane3));
        beatmap.Remove(beatmap.Notes[0]);

        Assert.That(beatmap.Notes, Has.Count.EqualTo(2));

        changes.Undo();
        Assert.That(beatmap.Notes, Has.Count.EqualTo(3));

        changes.Undo();
        Assert.That(beatmap.Notes, Has.Count.EqualTo(2));
        Assert.That(beatmap.Notes.Select(n => n.StartTime), Is.EqualTo(new[] { 500.0, 1000 }));
        Assert.That(changes.CanUndo.Value, Is.False);

        changes.Redo();
        changes.Redo();

        Assert.That(beatmap.Notes.Select(n => n.StartTime), Is.EqualTo(new[] { 1000.0, 1500 }));
        Assert.That(changes.CanRedo.Value, Is.False);
    }

    [Test]
    public void ATransactionUndoesAsOne()
    {
        beatmap.BeginChange();
        beatmap.Add(note(1500, NoteLane.Lane3));
        beatmap.Add(note(1750, NoteLane.Lane4));
        beatmap.UpdateTiming(t => t.Add(new TimingPoint(1500, 400)));
        beatmap.EndChange();

        changes.Undo();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(beatmap.Notes, Has.Count.EqualTo(2));
            Assert.That(beatmap.ControlPoints.TimingPoints, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void ANewEditDropsWhatCouldBeRedone()
    {
        beatmap.Add(note(1500, NoteLane.Lane3));
        changes.Undo();
        beatmap.Add(note(1750, NoteLane.Lane4));

        Assert.That(changes.CanRedo.Value, Is.False);
    }

    [Test]
    public void UnsavedChangesFollowTheSavedState()
    {
        Assert.That(changes.HasUnsavedChanges.Value, Is.False);

        beatmap.Add(note(1500, NoteLane.Lane3));
        Assert.That(changes.HasUnsavedChanges.Value, Is.True);

        changes.Undo();
        Assert.That(changes.HasUnsavedChanges.Value, Is.False, "Undoing back to what's on disk is nothing to save.");

        changes.Redo();
        changes.MarkSaved();
        Assert.That(changes.HasUnsavedChanges.Value, Is.False);
    }

    [Test]
    public void UndoDropsTheSelection()
    {
        beatmap.Add(note(1500, NoteLane.Lane3));
        beatmap.Select(beatmap.Notes);

        changes.Undo();

        Assert.That(beatmap.SelectedNotes, Is.Empty);
    }

    [Test]
    public void RemovingSelectedNotesDeselectsThem()
    {
        beatmap.Select(new[] { beatmap.Notes[0] });
        beatmap.Remove(beatmap.Notes[0]);

        Assert.That(beatmap.SelectedNotes, Is.Empty);
    }

    [Test]
    public void TheSaveCopyCarriesTheNotes()
    {
        var saved = beatmap.CreateBeatmapForSave();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Notes, Has.Length.EqualTo(2));
            Assert.That(saved.Notes[0], Is.Not.SameAs(beatmap.Notes[0]), "A save is a copy, not the editor's own notes.");
            Assert.That(saved.ControlPoints.TimingPoints, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void UndoKeepsWhatSnapshotsDoNotHold()
    {
        // Where the set lives and its song are never serialized, so a snapshot doesn't carry them.
        beatmap.BeatmapSet.StorageFolder = "7 Artist - Song";
        beatmap.BeatmapSet.TrackStoragePath = "beatmaps/7 Artist - Song/audio.mp3";
        beatmap.Beatmap.NotesStoragePath = "beatmaps/7 Artist - Song/Normal.ykb";

        beatmap.UpdateMetadata(b => b.BeatmapSet.Title = "Retitled");
        changes.Undo();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(beatmap.BeatmapSet.Title, Is.EqualTo("Song"));
            Assert.That(beatmap.BeatmapSet.StorageFolder, Is.EqualTo("7 Artist - Song"));
            Assert.That(beatmap.BeatmapSet.TrackStoragePath, Is.EqualTo("beatmaps/7 Artist - Song/audio.mp3"));
            Assert.That(beatmap.Beatmap.NotesStoragePath, Is.EqualTo("beatmaps/7 Artist - Song/Normal.ykb"));
            Assert.That(beatmap.Beatmap.Notes, Is.Empty, "The working copy's notes live in the editor, not on the beatmap.");
        }
    }
}
