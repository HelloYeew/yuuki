// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestSongSelectionScreen : TestYuukiScreenStack
{
    private SongSelectionScreen songSelectionScreen = null!;

    [Cached]
    private BeatmapsCollection beatmapsCollection { get; set; } = new BeatmapsCollection();

    [Cached]
    private readonly YuukiConfigManager configManager = new YuukiInMemoryConfigManager();

    [Cached]
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    [Test]
    public void TestAddScreen()
    {
        AddStep("add dummy beatmap", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(1000);
            var beatmapList = new List<Beatmap>();
            foreach (var set in beatmapsCollection.BeatmapSets)
            {
                beatmapList.AddRange(BeatmapTestUtility.CreateDummyBeatmaps(set, 3));
            }
            beatmapsCollection.Beatmaps = beatmapList;
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));

        // Flipping the setting must reshape the on-screen carousels live, without tearing anything down.
        AddStep("switch to vertical", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical);
        AddStep("switch to horizontal", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Horizontal);

        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    [Test]
    public void TestFiltersSitAboveTheVerticalList()
    {
        AddStep("add songs", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(50);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(set => BeatmapTestUtility.CreateDummyBeatmaps(set, 3)).ToList();
            configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical;
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));

        // Once everything has slid in: the list's column starts below the panel instead of running under it.
        AddWaitStep("settle", 1500);
        AddAssert("list starts below the filters", () =>
        {
            var filters = songSelectionScreen.Selector.FilterArea.DrawRectangle;
            var list = songSelectionScreen.Selector.ListArea.DrawRectangle;
            return filters.Height > 100 && list.Height > 100 && list.Y >= filters.Y + filters.Height - 1;
        });
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    [Test]
    public void TestFiltersHideWhilePickingADifficulty()
    {
        AddStep("add songs", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(20);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(set => BeatmapTestUtility.CreateDummyBeatmaps(set, 3)).ToList();

            // Not a song left over from another test's library: song select picks one of these.
            workingBeatmap.BeatmapSet = null!;
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("filters showing", () => songSelectionScreen.IsLoaded && songSelectionScreen.Selector.FilterArea.Alpha > 0.99f);

        AddStep("pick the song", () => songSelectionScreen.Selector.EnterDifficultySelection());
        AddUntilStep("filters stepped aside", () => songSelectionScreen.Selector.FilterArea.Alpha < 0.01f);

        AddStep("back to songs", () => songSelectionScreen.Selector.ExitToSets());
        AddUntilStep("filters back", () => songSelectionScreen.Selector.FilterArea.Alpha > 0.99f);
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    /// <summary>
    /// Enter held down: the press opens the song's difficulties, and its repeats don't go on to the
    /// next screen.
    /// </summary>
    [Test]
    public void TestHeldEnterStepsOnOnce()
    {
        AddStep("add songs", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(20);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(set => BeatmapTestUtility.CreateDummyBeatmaps(set, 3)).ToList();
            workingBeatmap.BeatmapSet = null!;
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("loaded", () => songSelectionScreen.IsLoaded);

        AddStep("press enter", () => songSelectionScreen.OnKeyDown(new KeyEvent(Key.Enter, KeyModifiers.None, false)));
        AddAssert("picking a difficulty", () => songSelectionScreen.Selector.State == BeatmapSelector.SelectionState.SelectingBeatmap);

        AddStep("keep holding it", () =>
        {
            for (int i = 0; i < 5; i++)
                songSelectionScreen.OnKeyDown(new KeyEvent(Key.Enter, KeyModifiers.None, true));
        });
        AddAssert("still picking a difficulty", () =>
            songSelectionScreen.Selector.State == BeatmapSelector.SelectionState.SelectingBeatmap && screenStack.CurrentScreen == songSelectionScreen);
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    [Test]
    public void TestDifficultiesFollowASongChangedFromOutside()
    {
        BeatmapSet other = null!;

        AddStep("add songs", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(20);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(set => BeatmapTestUtility.CreateDummyBeatmaps(set, 3)).ToList();
            workingBeatmap.BeatmapSet = null!;
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("loaded", () => songSelectionScreen.IsLoaded);
        AddStep("pick the song", () => songSelectionScreen.Selector.EnterDifficultySelection());

        // What the music player's next / previous / shuffle do.
        AddStep("the music player switches song", () => workingBeatmap.BeatmapSet = other =
            beatmapsCollection.BeatmapSets.First(s => s != workingBeatmap.BeatmapSet));

        AddAssert("still picking a difficulty", () => songSelectionScreen.Selector.State == BeatmapSelector.SelectionState.SelectingBeatmap);
        AddAssert("the working difficulty is the new song's", () => workingBeatmap.Beatmap?.BeatmapSet == other);
        AddStep("next difficulty", () => songSelectionScreen.Selector.NavigateNext());
        AddAssert("still the new song's", () => songSelectionScreen.Selector.CurrentBeatmap?.BeatmapSet == other);
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    [Test]
    public void TestSearchNarrowsTheList()
    {
        AddStep("add 1000 songs", () =>
        {
            beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(1000);
            beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(set => BeatmapTestUtility.CreateDummyBeatmaps(set, 3)).ToList();
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("every song listed", () => songSelectionScreen.IsLoaded && songSelectionScreen.Selector.MatchCount == 1000);

        AddStep("search for one", () => songSelectionScreen.Selector.Search("song 1000"));
        AddUntilStep("only it", () => songSelectionScreen.Selector.MatchCount == 1);

        AddStep("search for nothing", () => songSelectionScreen.Selector.Search("no such song"));
        AddUntilStep("none", () => songSelectionScreen.Selector.MatchCount == 0);

        AddStep("clear the search", () => songSelectionScreen.Selector.Search(""));
        AddUntilStep("every song back", () => songSelectionScreen.Selector.MatchCount == 1000);

        AddStep("switch to vertical", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical);
        AddStep("switch to horizontal", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Horizontal);
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    [Test]
    public void TestNothingMatchingShowsPlaceholderAndCantBePicked()
    {
        addSongs(20);
        pushScreen();

        AddStep("search for nothing", () => songSelectionScreen.Selector.Search("no such song"));
        AddUntilStep("the placeholder is up", () => songSelectionScreen.Selector.ShowingNoResults);
        AddAssert("nothing listed", () => !songSelectionScreen.Selector.HasSongsListed);

        AddStep("try to pick", () => songSelectionScreen.Selector.EnterDifficultySelection());
        AddAssert("still picking a song", () => songSelectionScreen.Selector.State == BeatmapSelector.SelectionState.SelectingSet);

        AddStep("switch to vertical", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical);
        AddStep("switch to horizontal", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Horizontal);

        AddStep("clear the search", () => songSelectionScreen.Selector.Search(""));
        AddUntilStep("the placeholder goes", () => !songSelectionScreen.Selector.ShowingNoResults);
        AddAssert("songs listed again", () => songSelectionScreen.Selector.HasSongsListed);
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    [Test]
    public void TestASongPickedFromOutsideIsListedDespiteTheSearch()
    {
        BeatmapSet hidden = null!;

        addSongs(20);
        pushScreen();

        AddStep("search for one song", () => songSelectionScreen.Selector.Search("Song 20"));
        AddUntilStep("only it", () => songSelectionScreen.Selector.MatchCount == 1);

        // What the music player's next / previous / shuffle do.
        AddStep("the music player picks a song the search hides", () => workingBeatmap.BeatmapSet = hidden =
            beatmapsCollection.BeatmapSets.First(s => s.Title == "Test Song 7"));
        AddUntilStep("it's listed and selected", () => songSelectionScreen.Selector.ListArea is BeatmapSetCarousel carousel
                                                        && carousel.Contains(hidden) && carousel.CurrentItem.Value == hidden);
        AddAssert("still one match", () => songSelectionScreen.Selector.MatchCount == 1);

        AddStep("pick it", () => songSelectionScreen.Selector.EnterDifficultySelection());
        AddAssert("its difficulties", () => workingBeatmap.Beatmap?.BeatmapSet == hidden);

        AddStep("back to songs", () => songSelectionScreen.Selector.ExitToSets());
        AddStep("search for nothing", () => songSelectionScreen.Selector.Search("no such song"));
        AddUntilStep("the forced song goes with the new search", () => !songSelectionScreen.Selector.HasSongsListed);
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    private void addSongs(int count) => AddStep($"add {count} songs", () =>
    {
        beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(count);
        beatmapsCollection.Beatmaps = beatmapsCollection.BeatmapSets.SelectMany(set => BeatmapTestUtility.CreateDummyBeatmaps(set, 3)).ToList();
        workingBeatmap.BeatmapSet = null!;
    });

    private void pushScreen()
    {
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("loaded", () => songSelectionScreen.IsLoaded);
    }
}
