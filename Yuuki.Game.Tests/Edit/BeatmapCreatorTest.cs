// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Database;
using Yuuki.Game.Edit;
using Path = System.IO.Path;

namespace Yuuki.Game.Tests.Edit;

/// <summary>
/// New sets from a song and new difficulties in a set, as the editor makes them: written to the library
/// straight away, with IDs that can't meet an imported beatmap's.
/// </summary>
[TestFixture]
public class BeatmapCreatorTest
{
    private string tempDir = null!;
    private string songsDir = null!;
    private NativeStorage storage = null!;
    private BeatmapsCollection collection = null!;
    private BeatmapWriter writer = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-creator-test", Path.GetRandomFileName());
        songsDir = Path.Combine(tempDir, "songs");
        Directory.CreateDirectory(songsDir);
        storage = new NativeStorage(Path.Combine(tempDir, "game"));
        collection = new BeatmapsCollection();
        writer = new BeatmapWriter(storage, collection);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Test]
    public void ASongBecomesASetWithOneDifficulty()
    {
        string song = TestWav.Write(songsDir, "My Song.wav", seconds: 2);

        var beatmap = BeatmapCreator.CreateSet(song, writer, collection);
        var set = beatmap.BeatmapSet;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(set.ID, Is.LessThan(0), "Below zero: never an imported set's ID.");
            Assert.That(beatmap.ID, Is.LessThan(0));
            Assert.That(set.Title, Is.EqualTo("My Song"), "No tags: the file name is the title.");
            Assert.That(set.TotalLength, Is.EqualTo(2000).Within(50));
            Assert.That(set.TrackPath, Is.EqualTo("My Song.wav"));
            Assert.That(storage.Exists(set.TrackStoragePath), "The song was copied into the set's folder.");
            Assert.That(storage.Exists(beatmap.NotesStoragePath), "The difficulty is on disk.");
            Assert.That(collection.GetBeatmapSetById(set.ID), Is.Not.Null);
            Assert.That(collection.GetBeatmapsFromBeatmapSet(set), Has.Length.EqualTo(1));
            Assert.That(beatmap.DifficultyName, Is.EqualTo(BeatmapCreator.DEFAULT_DIFFICULTY_NAME));
        }
    }

    [Test]
    public void LocalIdsKeepGoingDown()
    {
        var first = BeatmapCreator.CreateSet(TestWav.Write(songsDir, "a.wav"), writer, collection);
        var second = BeatmapCreator.CreateSet(TestWav.Write(songsDir, "b.wav", frequency: 550), writer, collection);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.BeatmapSet.ID, Is.LessThan(first.BeatmapSet.ID));
            Assert.That(second.ID, Is.LessThan(first.ID));
        }
    }

    [Test]
    public void ABlankDifficultyKeepsTheTiming()
    {
        var first = BeatmapCreator.CreateSet(TestWav.Write(songsDir, "song.wav"), writer, collection);
        first.ControlPoints = ControlPointInfo.FromBpm(150, 100);
        first.LaneCount = 6;

        var notes = new[] { new BeatmapNote { StartTime = 500, EndTime = 500, Lane = NoteLane.Lane2 } };
        var blank = BeatmapCreator.CreateDifficulty(first, copy: false, notes, writer, collection);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(blank.ID, Is.Not.EqualTo(first.ID));
            Assert.That(blank.BeatmapSet.ID, Is.EqualTo(first.BeatmapSet.ID));
            Assert.That(blank.ControlPoints.TimingPoints, Is.EqualTo(first.ControlPoints.TimingPoints));
            Assert.That(blank.LaneCount, Is.EqualTo(6));
            Assert.That(new BeatmapNoteStore(storage).Load(blank), Is.Empty);
            Assert.That(blank.DifficultyName, Is.Not.EqualTo(first.DifficultyName), "Names stay apart.");
            Assert.That(collection.GetBeatmapsFromBeatmapSet(first.BeatmapSet), Has.Length.EqualTo(2));
        }
    }

    [Test]
    public void ACopyKeepsTheNotes()
    {
        var first = BeatmapCreator.CreateSet(TestWav.Write(songsDir, "song.wav"), writer, collection);
        var notes = new[] { new BeatmapNote { StartTime = 500, EndTime = 500, Lane = NoteLane.Lane2 } };

        var copy = BeatmapCreator.CreateDifficulty(first, copy: true, notes, writer, collection);

        // Before the load below, which fills them in.
        Assert.That(copy.Notes, Is.Empty, "The library keeps metadata only.");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(copy.DifficultyName, Is.EqualTo($"{first.DifficultyName} (copy)"));
            Assert.That(new BeatmapNoteStore(storage).Load(copy).Single().StartTime, Is.EqualTo(500));
        }
    }

    [Test]
    public void AddingAFileNeverOverwritesADifferentOne()
    {
        var beatmap = BeatmapCreator.CreateSet(TestWav.Write(songsDir, "song.wav"), writer, collection);
        var set = beatmap.BeatmapSet;

        Directory.CreateDirectory(Path.Combine(songsDir, "other"));
        string sameName = TestWav.Write(Path.Combine(songsDir, "other"), "song.wav", frequency: 660);
        string again = writer.AddFile(set, Path.Combine(songsDir, "song.wav"));
        string different = writer.AddFile(set, sameName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(again, Is.EqualTo("song.wav"), "The same file is already there.");
            Assert.That(different, Is.EqualTo("song (2).wav"));
            Assert.That(storage.Exists(BeatmapWriter.StoragePathOf(set, different)));
        }
    }

    [Test]
    public void DeletingADifficultyLeavesTheSet()
    {
        var first = BeatmapCreator.CreateSet(TestWav.Write(songsDir, "song.wav"), writer, collection);
        var second = BeatmapCreator.CreateDifficulty(first, copy: false, System.Array.Empty<BeatmapNote>(), writer, collection);

        writer.Delete(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(storage.Exists(second.NotesStoragePath), Is.False);
            Assert.That(collection.GetBeatmapById(second.ID), Is.Null);
            Assert.That(collection.GetBeatmapById(first.ID), Is.Not.Null);
            Assert.That(storage.Exists(first.BeatmapSet.TrackStoragePath));
        }
    }

    [Test]
    public void DeletingASetTakesItsFolder()
    {
        var beatmap = BeatmapCreator.CreateSet(TestWav.Write(songsDir, "song.wav"), writer, collection);
        string folder = $"beatmaps/{beatmap.BeatmapSet.StorageFolder}";

        writer.Delete(beatmap.BeatmapSet);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(storage.ExistsDirectory(folder), Is.False);
            Assert.That(collection.BeatmapSets, Is.Empty);
            Assert.That(collection.Beatmaps, Is.Empty);
        }
    }
}
