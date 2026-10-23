// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;

namespace Yuuki.Game.Tests.Database;

/// <summary>
/// The .osz import built into the game (the separate converter's logic), on a small hand-made archive.
/// </summary>
[TestFixture]
public class OszImporterTest
{
    private const string osu_file = """
                                    osu file format v14

                                    [General]
                                    AudioFilename: audio.mp3
                                    PreviewTime: -1
                                    Mode: 3

                                    [Metadata]
                                    Title:Test Song
                                    TitleUnicode:テストソング
                                    Artist:Tester
                                    ArtistUnicode:Tester
                                    Creator:mapper
                                    Version:Normal
                                    Source:
                                    Tags:
                                    BeatmapID:0
                                    BeatmapSetID:-1

                                    [Difficulty]
                                    HPDrainRate:5
                                    CircleSize:4
                                    OverallDifficulty:5
                                    ApproachRate:5
                                    SliderMultiplier:1.4
                                    SliderTickRate:1

                                    [Events]

                                    [TimingPoints]
                                    500,500,4,1,0,100,1,0
                                    4500,375,3,1,0,100,1,0
                                    4500,-100,4,1,0,100,0,0

                                    [HitObjects]
                                    64,192,500,1,0,0:0:0:0:
                                    192,192,1000,1,2,0:0:0:0:
                                    320,192,1500,128,0,2500:0:0:0:0:
                                    448,192,2000,1,0,0:0:0:0:
                                    """;

    private string tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-osz-test", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(tempDir, "beatmaps"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    private string makeOsz(string name, string osu)
    {
        string path = Path.Combine(tempDir, name + ".osz");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        using (var writer = new StreamWriter(zip.CreateEntry("Tester - Test Song (mapper) [Normal].osu").Open(), new UTF8Encoding(false)))
            writer.Write(osu);

        using (var audio = zip.CreateEntry("audio.mp3").Open())
            audio.Write(new byte[1024]);

        return path;
    }

    [Test]
    public void ImportsAPlayableSetWithItsTiming()
    {
        var result = new OszImporter(Path.Combine(tempDir, "beatmaps")).Import(makeOsz("999 Tester - Test Song", osu_file));

        Assert.That(result.Success, Is.True, result.Error);

        // Read back the way the game reads its library.
        var library = new BeatmapsCollection();
        new BeatmapCollectionReader(new NativeStorage(tempDir), library).Read();

        var set = library.BeatmapSets.Single();
        var beatmap = library.Beatmaps.Single();
        var notes = new BeatmapNoteStore(new NativeStorage(tempDir)).Load(beatmap);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(set.ID, Is.EqualTo(999), "Unsubmitted, so the ID comes from the osu!-style file name.");
            Assert.That(set.TitleUnicode, Is.EqualTo("テストソング"));
            Assert.That(set.BPM, Is.EqualTo(120).Within(0.01), "The tempo that lasts longest up to the last note.");

            Assert.That(beatmap.LaneCount, Is.EqualTo(4));
            Assert.That(beatmap.DifficultyRating, Is.GreaterThan(0));

            Assert.That(notes.Select(n => (int)n.Lane), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(notes[2].Type, Is.EqualTo(NoteType.Hold));
            Assert.That(notes[2].EndTime, Is.EqualTo(2500));
            Assert.That(notes[2].Bank, Is.EqualTo(SampleBank.Drum));
            Assert.That(notes[1].Additions, Is.EqualTo(HitSampleAddition.Whistle));

            // Red lines only; the green line at 4500 isn't timing.
            Assert.That(beatmap.ControlPoints.TimingPoints, Has.Count.EqualTo(2));
            Assert.That(beatmap.ControlPoints.TimingPoints[0].Time, Is.EqualTo(500));
            Assert.That(beatmap.ControlPoints.TimingPoints[1].Bpm, Is.EqualTo(160).Within(0.01));
            Assert.That(beatmap.ControlPoints.TimingPoints[1].TimeSignature, Is.EqualTo(TimeSignature.SimpleTriple));
        }
    }

    /// <summary>
    /// A file a phone hands over as a link has no path, only a stream and the name the app it came from
    /// gives it; the set's ID still comes from that name.
    /// </summary>
    [Test]
    public void ImportsFromAStreamByItsName()
    {
        string osz = makeOsz("archive", osu_file);
        OszImportResult result;

        using (var stream = File.OpenRead(osz))
            result = new OszImporter(Path.Combine(tempDir, "beatmaps")).Import(stream, "999 Tester - Test Song.osz");

        var library = new BeatmapsCollection();
        new BeatmapCollectionReader(new NativeStorage(tempDir), library).Read();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(library.BeatmapSets.Single().ID, Is.EqualTo(999));
        }
    }

    [Test]
    public void ImportingAgainReplacesTheSet()
    {
        var importer = new OszImporter(Path.Combine(tempDir, "beatmaps"));
        string osz = makeOsz("999 Tester - Test Song", osu_file);

        importer.Import(osz);
        var again = importer.Import(osz);

        Assert.That(again.Success, Is.True, again.Error);
        Assert.That(Directory.GetDirectories(Path.Combine(tempDir, "beatmaps")), Has.Length.EqualTo(1));
    }

    [Test]
    public void ANonManiaMapIsRefusedWithAReason()
    {
        var result = new OszImporter(Path.Combine(tempDir, "beatmaps")).Import(makeOsz("1 Standard", osu_file.Replace("Mode: 3", "Mode: 0")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("osu!mania"));
            Assert.That(Directory.GetDirectories(Path.Combine(tempDir, "beatmaps")), Is.Empty);
        }
    }

    [Test]
    public void ABrokenFileIsAFailureNotACrash()
    {
        string path = Path.Combine(tempDir, "broken.osz");
        File.WriteAllBytes(path, new byte[64]);

        Assert.That(new OszImporter(Path.Combine(tempDir, "beatmaps")).Import(path).Success, Is.False);
    }
}
