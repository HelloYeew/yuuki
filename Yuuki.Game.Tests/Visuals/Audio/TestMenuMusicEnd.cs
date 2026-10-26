// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;
using Yuuki.Game.Audio;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Tests.Visuals.Audio;

/// <summary>
/// What the menu music does when its song ends: never stay silent. One case a scene, as only the first test of a
/// scene hears its headless songs end.
/// </summary>
public abstract partial class TestMenuMusicEnd : TestScene
{
    private string tempDir = null!;
    private readonly BeatmapsCollection collection = new BeatmapsCollection();
    private readonly WorkingBeatmap working = new WorkingBeatmap();

    protected YuukiAudioManager Audio = null!;
    private bool ended;

    [BackgroundDependencyLoader]
    private void load()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "yuuki-menu-music-test", Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(tempDir, "songs"));
        TestWav.Write(Path.Combine(tempDir, "songs"), "first.wav");
        TestWav.Write(Path.Combine(tempDir, "songs"), "second.wav");

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
        AddStep("the audio manager", () =>
        {
            Clear();
            Add(Audio = new YuukiAudioManager());
        });
        AddUntilStep("loaded", () => Audio.IsLoaded);
    }

    protected BeatmapSet AddSong(int id, string file)
    {
        var set = new BeatmapSet { ID = id, Title = $"Song {id}", TrackStoragePath = $"songs/{file}" };
        collection.BeatmapSets.Add(set);
        return set;
    }

    protected void Play(BeatmapSet set)
    {
        ended = false;
        working.BeatmapSet = set;
        Audio.CurrentChannel.OnEnd += () => ended = true;
    }

    protected int? PlayingSetId => working.BeatmapSet?.ID;

    // Headless audio plays in real time against the test's fast virtual clock, so the song is put at its end, still
    // playing, and ends at the next update as one that played out does.
    protected void EndSong()
    {
        AddUntilStep("playing", () => Audio.IsPlaying);
        AddStep("the song reaches its end", () => Audio.CurrentChannel.CurrentTime = Audio.CurrentChannel.Length);
        AddUntilStep("it ends", () => ended);
    }
}

public partial class TestMenuMusicEndOnlySong : TestMenuMusicEnd
{
    [Test]
    public void TestTheOnlySongPlaysAgainOnceItEnds()
    {
        AddStep("one song in the library, playing", () => Play(AddSong(1, "first.wav")));

        // Picked at random again, the only song would be the same one, which changes nothing: the music used to stop for good.
        EndSong();
        AddUntilStep("and plays again", () => Audio.IsPlaying);
    }
}

public partial class TestMenuMusicEndNextSong : TestMenuMusicEnd
{
    [Test]
    public void TestAnotherSongFollowsWhereThereIsOne()
    {
        AddStep("two songs, the first playing", () =>
        {
            var first = AddSong(1, "first.wav");
            AddSong(2, "second.wav");
            Play(first);
        });

        EndSong();
        AddUntilStep("the other one plays", () => PlayingSetId == 2 && Audio.IsPlaying);
    }
}

public partial class TestMenuMusicEndPreview : TestMenuMusicEnd
{
    [Test]
    public void TestAPreviewThatEndedAsItWasToLoopStartsOver()
    {
        AddStep("song select's looping preview, playing", () =>
        {
            Play(AddSong(1, "first.wav"));
            Audio.LoopPreview.Value = true;

            // As when the song runs out just before the loop reaches the channel: it ends rather than wrapping.
            Audio.CurrentChannel.Looping = false;
        });

        EndSong();
        AddUntilStep("and the preview plays again", () => Audio.IsPlaying);
    }
}
