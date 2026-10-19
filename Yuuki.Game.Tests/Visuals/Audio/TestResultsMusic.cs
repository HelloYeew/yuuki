// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Tests.Visuals.Audio;

public partial class TestResultsMusic : TestScene
{
    private string tempDir = null!;
    private readonly BeatmapsCollection collection = new BeatmapsCollection();
    private readonly WorkingBeatmap working = new WorkingBeatmap();

    private YuukiAudioManager audio = null!;
    private IAudioChannel? song;

    [BackgroundDependencyLoader]
    private void load()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-results-music-test", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(tempDir, "songs"));
        TestWav.Write(Path.Combine(tempDir, "songs"), "song.wav");

        Cache<Storage>(new NativeStorage(tempDir));
        Cache(collection);
        Cache(working);
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("the audio manager, a played song", () =>
        {
            Clear();
            Add(audio = new YuukiAudioManager());
        });
        AddUntilStep("loaded", () => audio.IsLoaded);
        AddStep("the play's song, playing", () =>
        {
            working.BeatmapSet = new BeatmapSet { ID = 1, Title = "Song", TrackStoragePath = "songs/song.wav" };
            song = audio.BeginGameplay();
            song!.Play();
        });
        AddAssert("playing", () => song!.IsRunning.Value);
    }

    [TearDown]
    public void TearDown()
    {
        audio?.EndResultsMusic();
        audio?.EndGameplay();
    }

    [Test]
    public void TestWaitsForTheSongToEnd()
    {
        AddStep("the results ask for their music", () => audio.BeginResultsMusic());
        AddAssert("it waits: the song is still going", () => audio.ResultsMusicWaiting && song!.IsRunning.Value);
        AddWaitStep("a while", 5);
        AddAssert("still waiting", () => audio.ResultsMusicWaiting);

        endSong();
        AddUntilStep("then the music comes in", () => !audio.ResultsMusicWaiting);
    }

    [Test]
    public void TestNoWaitOnceTheSongIsOver()
    {
        AddStep("the song stopped (a replay leaves it paused)", () => song!.Pause());
        AddStep("the results ask for their music", () => audio.BeginResultsMusic());
        AddAssert("straight away", () => !audio.ResultsMusicWaiting);
    }

    [Test]
    public void TestLeavingWhileWaitingKeepsItFromStarting()
    {
        AddStep("the results ask for their music", () => audio.BeginResultsMusic());
        AddStep("leave the results (back, retry, watch the replay)", () => audio.EndResultsMusic());
        AddAssert("no longer waiting", () => !audio.ResultsMusicWaiting);

        endSong();
        AddWaitStep("a while", 5);
        AddAssert("and nothing starts", () => !audio.ResultsMusicWaiting);
    }

    // Where a song that plays out ends up: at its end, no longer running. (Headless audio runs in real
    // time against the test's fast virtual clock, so waiting for it to get there would be flaky.)
    private void endSong() => AddStep("the song ends", () =>
    {
        song!.CurrentTime = song.Length;
        song.Pause();
    });
}
