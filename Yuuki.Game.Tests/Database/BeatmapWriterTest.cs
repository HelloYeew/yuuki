// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;
using Yuuki.Game.Database;

namespace Yuuki.Game.Tests.Database;

/// <summary>
/// The editor's save path: what <see cref="BeatmapWriter"/> writes has to come back the same through
/// the library scan and the per-play note load.
/// </summary>
[TestFixture]
public class BeatmapWriterTest
{
    private string tempDir = null!;
    private NativeStorage storage = null!;
    private BeatmapsCollection collection = null!;
    private BeatmapWriter writer = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-writer-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);
        collection = new BeatmapsCollection();
        writer = new BeatmapWriter(storage, collection);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    private static BeatmapSet makeSet() => new BeatmapSet
    {
        ID = 42,
        Title = "Song",
        TitleUnicode = "ソング",
        Artist = "Artist",
        ArtistUnicode = "Artist",
        BPM = 150
    };

    private static Beatmap makeBeatmap(BeatmapSet set)
    {
        var timing = new ControlPointInfo();
        timing.Add(new TimingPoint(120, 400));
        timing.Add(new TimingPoint(8120, 300, TimeSignature.SimpleTriple));

        return new Beatmap
        {
            ID = 4201,
            BeatmapSet = set,
            DifficultyName = "Hard",
            Creator = "mapper",
            LaneCount = 4,
            ControlPoints = timing,
            // Out of order on purpose: a save sorts them.
            Notes = Enumerable.Range(0, 200)
                              .Select(i => new BeatmapNote { Lane = (NoteLane)(i % 4), StartTime = 120 + (199 - i) * 100, EndTime = 120 + (199 - i) * 100 })
                              .ToArray()
        };
    }

    private BeatmapsCollection rescan()
    {
        var scanned = new BeatmapsCollection();
        new BeatmapCollectionReader(storage, scanned).Read();
        return scanned;
    }

    [Test]
    public void ASavedChartComesBackThroughTheLibraryScan()
    {
        var set = makeSet();
        var beatmap = makeBeatmap(set);

        writer.Save(set);
        writer.Save(beatmap);

        var scanned = rescan();
        var readSet = scanned.GetBeatmapSetById(42)!;
        var read = scanned.GetBeatmapById(4201)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readSet.TitleUnicode, Is.EqualTo("ソング"));
            Assert.That(readSet.StorageFolder, Is.EqualTo(set.StorageFolder));
            Assert.That(read.BeatmapSet, Is.SameAs(readSet));
            Assert.That(read.Version, Is.EqualTo(Beatmap.CURRENT_VERSION));
            Assert.That(read.ControlPoints.TimingPoints, Is.EqualTo(beatmap.ControlPoints.TimingPoints));
            Assert.That(read.Notes, Is.Empty, "The scan still reads metadata only.");
            Assert.That(read.NotesStoragePath, Is.EqualTo(beatmap.NotesStoragePath));
        }

        var notes = new BeatmapNoteStore(storage).Load(read);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes, Has.Length.EqualTo(200));
            Assert.That(notes.Select(n => n.StartTime), Is.Ordered, "Saved in time order.");
        }
    }

    [Test]
    public void SavingRatesTheChart()
    {
        var set = makeSet();
        var beatmap = makeBeatmap(set);
        beatmap.DifficultyRating = 0;

        writer.Save(beatmap);

        Assert.That(beatmap.DifficultyRating, Is.GreaterThan(0));
    }

    [Test]
    public void RenamingADifficultyLeavesNoOldFileBehind()
    {
        var set = makeSet();
        var beatmap = makeBeatmap(set);

        writer.Save(set);
        writer.Save(beatmap);

        string oldPath = beatmap.NotesStoragePath;

        beatmap.DifficultyName = "Insane";
        writer.Save(beatmap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(storage.Exists(oldPath), Is.False);
            Assert.That(storage.Exists(beatmap.NotesStoragePath), Is.True);
            Assert.That(rescan().Beatmaps.Single().DifficultyName, Is.EqualTo("Insane"));
        }
    }

    [Test]
    public void RetitlingASetKeepsOneSetFileInTheSameFolder()
    {
        var set = makeSet();
        writer.Save(set);

        string folder = set.StorageFolder!;

        set.Title = "Renamed";
        writer.Save(set);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(set.StorageFolder, Is.EqualTo(folder));
            Assert.That(storage.GetFiles($"beatmaps/{folder}", "*.yks").Count(), Is.EqualTo(1));
            Assert.That(rescan().BeatmapSets.Single().Title, Is.EqualTo("Renamed"));
        }
    }

    [Test]
    public void SavingReplacesTheLibraryEntryWithTheSameId()
    {
        var set = makeSet();
        var original = makeBeatmap(set);
        collection.BeatmapSets.Add(set);
        collection.Beatmaps.Add(original);

        var edited = makeBeatmap(set);
        edited.DifficultyName = "Edited";
        writer.Save(edited);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collection.Beatmaps, Has.Count.EqualTo(1));
            Assert.That(collection.Beatmaps[0], Is.SameAs(edited));
        }
    }
}
