// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Beatmaps;

/// <summary>
/// Covers the split between metadata and chart data: a library scan must not bring every
/// difficulty's notes into memory, and <see cref="BeatmapNoteStore"/> must be able to fetch and
/// release them per play.
/// </summary>
[TestFixture]
public class BeatmapNoteStoreTest
{
    private const int note_count = 500;

    private string tempDir = null!;
    private NativeStorage storage = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-notestore-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        storage = new NativeStorage(tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [Test]
    public void DeserializeMetadata_DropsNotesKeepsMetadata()
    {
        using var stream = new MemoryStream(serialize(makeBeatmap()));

        var beatmap = BeatmapUtility.DeserializeMetadata(stream);

        Assert.That(beatmap, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(beatmap.Notes, Is.Empty);
            Assert.That(beatmap.DifficultyName, Is.EqualTo("Basic"));
            Assert.That(beatmap.Creator, Is.EqualTo("someone"));
            Assert.That(beatmap.DifficultyRating, Is.EqualTo(3.5));
            Assert.That(beatmap.BeatmapSet, Is.Not.Null);
        }
        Assert.That(beatmap.BeatmapSet.ID, Is.EqualTo(1));
    }

    [Test]
    public void Deserialize_KeepsNotes()
    {
        using var stream = new MemoryStream(serialize(makeBeatmap()));

        var beatmap = BeatmapUtility.Deserialize(stream);

        Assert.That(beatmap.Notes, Has.Length.EqualTo(note_count));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(beatmap.Notes[0].Lane, Is.EqualTo(NoteLane.Lane1));
            Assert.That(beatmap.Notes[1].Type, Is.EqualTo(NoteType.Hold));
            Assert.That(beatmap.Notes[1].EndTime, Is.GreaterThan(beatmap.Notes[1].StartTime));
        }
    }

    [Test]
    public void Load_ReadsNotesFromStorage()
    {
        write("chart.ykb", makeBeatmap());

        var beatmap = new Beatmap { NotesStoragePath = "chart.ykb" };
        var store = new BeatmapNoteStore(storage);

        var notes = store.Load(beatmap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notes, Has.Length.EqualTo(note_count));
            Assert.That(beatmap.Notes, Is.SameAs(notes), "the loaded chart should be cached on the beatmap for the run");
        }
    }

    [Test]
    public void Load_AlreadyLoaded_DoesNotReadAgain()
    {
        write("chart.ykb", makeBeatmap());

        var beatmap = new Beatmap { NotesStoragePath = "chart.ykb" };
        var store = new BeatmapNoteStore(storage);

        var first = store.Load(beatmap);
        var second = store.Load(beatmap);

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void Unload_ReleasesNotes()
    {
        write("chart.ykb", makeBeatmap());

        var beatmap = new Beatmap { NotesStoragePath = "chart.ykb" };
        var store = new BeatmapNoteStore(storage);

        store.Load(beatmap);
        store.Unload(beatmap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(beatmap.Notes, Is.Empty);

            // And is reloadable afterward, since the file is the source of truth.
            Assert.That(store.Load(beatmap), Has.Length.EqualTo(note_count));
        }
    }

    /// <summary>
    /// In-memory beatmaps (generated charts, the bundled theme) have no file to reload from, so the
    /// store must leave their notes alone in both directions.
    /// </summary>
    [Test]
    public void InMemoryBeatmap_NotesAreNeitherReloadedNorDropped()
    {
        var beatmap = makeBeatmap();
        var original = beatmap.Notes;
        var store = new BeatmapNoteStore(storage);

        Assert.That(store.Load(beatmap), Is.SameAs(original));

        store.Unload(beatmap);

        Assert.That(beatmap.Notes, Is.SameAs(original));
    }

    [Test]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var store = new BeatmapNoteStore(storage);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.Load(new Beatmap { NotesStoragePath = "nope.ykb" }), Is.Empty);
            Assert.That(store.Load(new Beatmap()), Is.Empty);
            Assert.That(store.Load(null!), Is.Empty);
        }
    }

    [Test]
    public void Load_MalformedFile_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(tempDir, "broken.ykb"), "{ not json");

        Assert.That(new BeatmapNoteStore(storage).Load(new Beatmap { NotesStoragePath = "broken.ykb" }), Is.Empty);
    }

    /// <summary>
    /// The end-to-end shape this exists for: scanning the library leaves no charts resident, but every
    /// beatmap it found can still produce its chart on demand.
    /// </summary>
    [Test]
    public void CollectionReader_LeavesNotesUnloadedButLoadable()
    {
        var set = new BeatmapSet { ID = 1, Title = "Title", Artist = "Artist" };
        string folder = Path.Combine(tempDir, BeatmapSetUtility.BEATMAP_FOLDER_NAME, "1 Artist - Title");
        Directory.CreateDirectory(folder);

        File.WriteAllBytes(Path.Combine(folder, "set.yks"), JsonSerializer.SerializeToUtf8Bytes(set));
        File.WriteAllBytes(Path.Combine(folder, "chart.ykb"), serialize(makeBeatmap()));

        var collection = new BeatmapsCollection();
        new BeatmapCollectionReader(storage, collection).Read();

        Assert.That(collection.Beatmaps, Has.Count.EqualTo(1));

        var beatmap = collection.Beatmaps[0];

        Assert.That(beatmap.Notes, Is.Empty, "a library scan must not bring charts into memory");
        Assert.That(beatmap.NotesStoragePath, Is.Not.Null);
        Assert.That(new BeatmapNoteStore(storage).Load(beatmap), Has.Length.EqualTo(note_count));
    }

    /// <summary>
    /// The summary has to come from the file without the chart becoming resident: it's read for display
    /// on every difficulty browsed, and may run off the update thread beside a play loading the same one.
    /// </summary>
    [Test]
    public void GetSummary_ReadsFileWithoutLoadingNotes()
    {
        write("chart.ykb", makeBeatmap());

        var beatmap = new Beatmap { NotesStoragePath = "chart.ykb" };
        var summary = new BeatmapNoteStore(storage).GetSummary(beatmap);

        Assert.That(summary.TotalNotes, Is.EqualTo(note_count));
        Assert.That(summary.HoldCount, Is.EqualTo(note_count / 2));
        Assert.That(beatmap.Notes, Is.Empty, "summarising must not leave the chart loaded");
        Assert.That(beatmap.NoteSummary, Is.SameAs(summary), "the summary should be cached on the beatmap");
    }

    [Test]
    public void Summary_CountsKindsComboAndPeak()
    {
        var summary = BeatmapNoteSummary.From(new[]
        {
            new BeatmapNote { Type = NoteType.Tap, StartTime = 0, EndTime = 0 },
            new BeatmapNote { Type = NoteType.Tap, StartTime = 100, EndTime = 300 }, // has a duration: plays as a hold
            new BeatmapNote { Type = NoteType.Hold, StartTime = 200, EndTime = 600 },
            new BeatmapNote { Type = NoteType.Swipe, StartTime = 300, EndTime = 300 },
            new BeatmapNote { Type = NoteType.Bullet, StartTime = 5000, EndTime = 5000 }
        });

        Assert.That(summary.TapCount, Is.EqualTo(1));
        Assert.That(summary.HoldCount, Is.EqualTo(2));
        Assert.That(summary.SwipeCount, Is.EqualTo(1));
        Assert.That(summary.BulletCount, Is.EqualTo(1));
        Assert.That(summary.NormalCount, Is.EqualTo(4));
        Assert.That(summary.MaxCombo, Is.EqualTo(7), "each hold adds a release judgement");
        Assert.That(summary.PeakNotesPerSecond, Is.EqualTo(4));
        Assert.That(summary.Density, Has.Length.EqualTo(BeatmapNoteSummary.DENSITY_COLUMNS));
        Assert.That(summary.Density, Has.Some.EqualTo(1f), "density is normalised to the busiest column");
    }

    [Test]
    public void Summary_EmptyChart()
    {
        Assert.That(BeatmapNoteSummary.EMPTY.IsEmpty, Is.True);
        Assert.That(BeatmapNoteSummary.EMPTY.Density, Is.Empty);
        Assert.That(BeatmapNoteSummary.EMPTY.PeakNotesPerSecond, Is.Zero);
    }

    private static Beatmap makeBeatmap()
    {
        var notes = new BeatmapNote[note_count];

        for (int i = 0; i < notes.Length; i++)
        {
            bool hold = i % 2 == 1;

            notes[i] = new BeatmapNote
            {
                Lane = (NoteLane)(i % 4),
                Type = hold ? NoteType.Hold : NoteType.Tap,
                StartTime = i * 250,
                EndTime = i * 250 + (hold ? 500 : 0)
            };
        }

        return new Beatmap
        {
            ID = 10,
            Creator = "someone",
            DifficultyName = "Basic",
            DifficultyRating = 3.5,
            BeatmapSet = new BeatmapSet { ID = 1, Title = "Title", Artist = "Artist" },
            Notes = notes
        };
    }

    private static byte[] serialize(Beatmap beatmap) => JsonSerializer.SerializeToUtf8Bytes(beatmap);

    private void write(string name, Beatmap beatmap) => File.WriteAllBytes(Path.Combine(tempDir, name), serialize(beatmap));
}
