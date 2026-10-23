// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Database;

namespace Yuuki.Game.Tests.Visuals.Database;

/// <summary>
/// What's dropped into the top of the game's folder, as a phone's folder shown in a computer's file manager only
/// takes things there: an .osz is imported and removed, one that fails stays, a set folder in the game's own format
/// joins the library, and the game's own files are left alone.
/// </summary>
public partial class TestDroppedImports : TestScene
{
    private const string osu_file = """
                                    osu file format v14

                                    [General]
                                    AudioFilename: audio.mp3
                                    PreviewTime: -1
                                    Mode: 3

                                    [Metadata]
                                    Title:Dropped Song
                                    TitleUnicode:Dropped Song
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

                                    [HitObjects]
                                    64,192,500,1,0,0:0:0:0:
                                    192,192,1000,1,2,0:0:0:0:
                                    """;

    private string root = null!;
    private Storage storage = null!;
    private readonly BeatmapsCollection library = new BeatmapsCollection();
    private BeatmapImportManager manager = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        root = Path.Combine(Path.GetTempPath(), "yuuki-dropped-test", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "beatmaps"));
        storage = new NativeStorage(root);

        Cache(storage);
        Cache(library);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [SetUp]
    public void SetUpRoot() => AddStep("an empty folder", () =>
    {
        Clear();
        library.Clear();

        foreach (string file in Directory.GetFiles(root))
            File.Delete(file);

        foreach (string folder in Directory.GetDirectories(root))
            Directory.Delete(folder, true);

        Directory.CreateDirectory(Path.Combine(root, "beatmaps"));
    });

    [Test]
    public void TestAnOszIsImportedAndRemoved()
    {
        AddStep("drop an .osz", () => makeOsz(Path.Combine(root, "Dropped Song.osz")));
        look();
        AddAssert("in the library", () => library.BeatmapSets.Count == 1 && Directory.GetDirectories(Path.Combine(root, "beatmaps")).Length == 1);
        AddAssert("and gone from the top", () => !File.Exists(Path.Combine(root, "Dropped Song.osz")));
    }

    [Test]
    public void TestOneThatFailsStays()
    {
        AddStep("drop a broken .osz", () => File.WriteAllText(Path.Combine(root, "broken.osz"), "not a zip"));
        look();
        AddAssert("nothing imported, the file kept", () => library.BeatmapSets.Count == 0 && File.Exists(Path.Combine(root, "broken.osz")));
    }

    /// <summary>
    /// A set folder in the game's own format (one from the editor elsewhere, say) joins the library.
    /// </summary>
    [Test]
    public void TestASetFolderJoinsTheLibrary()
    {
        AddStep("drop a set folder", () =>
        {
            // Made the game's way, then dropped in.
            string scratch = Path.Combine(root, ".scratch");
            Directory.CreateDirectory(Path.Combine(scratch, "beatmaps"));
            new OszImporter(Path.Combine(scratch, "beatmaps")).Import(makeOsz(Path.Combine(scratch, "made.osz")));
            Directory.Move(Directory.GetDirectories(Path.Combine(scratch, "beatmaps")).Single(), Path.Combine(root, "My Set"));
            Directory.Delete(scratch, true);
        });
        look();
        AddAssert("in the library", () => library.BeatmapSets.Count == 1 && Directory.Exists(Path.Combine(root, "beatmaps", "My Set")));
        AddAssert("and gone from the top", () => !Directory.Exists(Path.Combine(root, "My Set")));
    }

    /// <summary>
    /// The game's own files and folders, and the system's inbox, are no beatmaps to take in.
    /// </summary>
    [Test]
    public void TestTheGamesOwnAreLeftAlone()
    {
        AddStep("the game's own files", () =>
        {
            File.WriteAllText(Path.Combine(root, "game.ini"), "x");
            File.WriteAllText(Path.Combine(root, "offsets.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            File.WriteAllText(Path.Combine(root, "logs", "runtime.log"), "x");
            Directory.CreateDirectory(Path.Combine(root, "Inbox"));
            makeOsz(Path.Combine(root, "Inbox", "shared.osz"));
        });
        look();
        AddAssert("all still there, nothing imported", () =>
            library.BeatmapSets.Count == 0 && File.Exists(Path.Combine(root, "game.ini")) && File.Exists(Path.Combine(root, "offsets.json"))
            && File.Exists(Path.Combine(root, "logs", "runtime.log")) && File.Exists(Path.Combine(root, "Inbox", "shared.osz")));
    }

    // The manager looks as it starts, as at launch, and is done when nothing dropped is in hand.
    private void look()
    {
        AddStep("start the game's importer", () => Add(manager = new BeatmapImportManager()));
        AddUntilStep("looked", () => manager.IsLoaded);
        AddWaitStep("a frame", 2);
        AddUntilStep("taken in", () => !manager.TakingInDropped, 30_000);
    }

    private static string makeOsz(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        using (var writer = new StreamWriter(zip.CreateEntry("Tester - Dropped Song (mapper) [Normal].osu").Open(), new UTF8Encoding(false)))
            writer.Write(osu_file);

        using (var audio = zip.CreateEntry("audio.mp3").Open())
            audio.Write(new byte[1024]);

        return path;
    }
}
