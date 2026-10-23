// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics;
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

    [Cached]
    private readonly GameLayout gameLayout = new GameLayout();

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
    /// Escape with a search applied clears it first; the next Escape leaves. The back button leaves at
    /// once. The jump keys show in the footer but are the screen's own (so they repeat while held).
    /// </summary>
    [Test]
    public void TestEscapeClearsTheSearchFirst()
    {
        addSongs(20);
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("loaded", () => songSelectionScreen.IsLoaded);
        AddAssert("the jumps are hints only", () => songSelectionScreen.ScreenActions.Where(a => a.Name.StartsWith("Jump")).All(a => a.HintOnly)
                                                    && songSelectionScreen.ScreenActions.Count(a => a.Name.StartsWith("Jump")) == 2);

        AddStep("search", () => songSelectionScreen.Selector.Search("song"));
        AddStep("escape", () => back());
        AddAssert("the search is cleared, still here", () => songSelectionScreen.Selector.SearchText.Length == 0 && screenStack.CurrentScreen == songSelectionScreen);
        AddStep("escape again", () => back());
        AddUntilStep("gone", () => screenStack.CurrentScreen != songSelectionScreen);
    }

    private void back() => songSelectionScreen.ScreenActions.Single(a => a.Name == "Back").Action();

    /// <summary>
    /// Random from the difficulty list goes back to the songs, the window included, as Back does.
    /// </summary>
    [Test]
    public void TestRandomFromTheDifficultiesBringsTheSongsBack()
    {
        addSongs(20);
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("the song window shows", () => songSelectionScreen.IsLoaded && songList.FocusWindow?.Alpha > 0.99f);

        AddStep("pick the song", () => songSelectionScreen.Selector.EnterDifficultySelection());
        AddUntilStep("the song window's gone", () => songList.FocusWindow!.Alpha < 0.01f);

        AddStep("random", () => songSelectionScreen.Selector.SelectRandom());
        AddAssert("back on the songs", () => songSelectionScreen.Selector.State == BeatmapSelector.SelectionState.SelectingSet);
        AddUntilStep("the song window's back", () => songList.FocusWindow!.Alpha > 0.99f);
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
        // Not a random first song: one in twenty times that was the song picked below, already playing,
        // so picking it changed nothing and it was never listed.
        AddStep("start on a song the test doesn't use", () => workingBeatmap.BeatmapSet = beatmapsCollection.BeatmapSets.First(s => s.Title == "Test Song 1"));
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

    private BeatmapSetCarousel songList => (BeatmapSetCarousel)songSelectionScreen.Selector.ListArea;

    /// <summary>
    /// PgUp / PgDn move a group at a time in the current sort: here, the dummy songs' BPMs (121 to 150) in
    /// bands of ten. Home / End go to the ends.
    /// </summary>
    [Test]
    public void TestGroupJump()
    {
        addSongs(30);
        sortBy(BeatmapSort.Bpm);
        pushScreen();

        AddStep("to the first", () => songSelectionScreen.Selector.JumpToEnd(last: false));
        AddUntilStep("the 120s", () => songList.CurrentIndex == 0);
        AddStep("next group", () => songSelectionScreen.Selector.JumpGroup(1));
        AddUntilStep("the 130s", () => songList.CurrentIndex == 9);
        AddStep("next group", () => songSelectionScreen.Selector.JumpGroup(1));
        AddUntilStep("the 140s", () => songList.CurrentIndex == 19);
        AddStep("previous group", () => songSelectionScreen.Selector.JumpGroup(-1));
        AddUntilStep("the 130s again, from their start", () => songList.CurrentIndex == 9);
        AddStep("to the end", () => songSelectionScreen.Selector.JumpToEnd(last: true));
        AddUntilStep("the last", () => songList.CurrentIndex == 29);

        AddStep("Remove screen", () => songSelectionScreen.Exit());
        sortBy(BeatmapSort.Added);
    }

    [Test]
    public void TestGroupJumpWithoutGroupsGoesTenAtATime()
    {
        addSongs(30);
        sortBy(BeatmapSort.Added);
        pushScreen();

        AddStep("to the first", () => songSelectionScreen.Selector.JumpToEnd(last: false));
        AddUntilStep("there", () => songList.CurrentIndex == 0);
        AddStep("next \"group\"", () => songSelectionScreen.Selector.JumpGroup(1));
        AddUntilStep("ten on", () => songList.CurrentIndex == 10);

        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    /// <summary>
    /// The scrubber (the song list's scrollbar) is labelled where each group starts, in coarser bands than
    /// the jump: BPM in twenties. An order without groups has no labels.
    /// </summary>
    [Test]
    public void TestScrubberLabels()
    {
        addSongs(30);
        sortBy(BeatmapSort.Bpm);
        pushScreen();

        AddAssert("labelled by BPM band", () => songList.ScrollbarLabels.SequenceEqual(new[] { (0, "120"), (19, "140") }));
        AddStep("Remove screen", () => songSelectionScreen.Exit());

        sortBy(BeatmapSort.Added);
        pushScreen();
        AddAssert("no labels as added", () => songList.ScrollbarLabels.Count == 0);
        AddStep("Remove screen again", () => songSelectionScreen.Exit());
    }

    /// <summary>
    /// A long jump skims, and while it does the group it's passing through shows large.
    /// </summary>
    [Test]
    public void TestSkimLabelShowsTheGroup()
    {
        addSongs(30);
        sortBy(BeatmapSort.Bpm);
        pushScreen();

        AddStep("to the first", () => songSelectionScreen.Selector.JumpToEnd(last: false));
        AddUntilStep("at rest there", () => songList.CurrentIndex == 0 && songList.Motion.Skim == 0);
        AddStep("all the way to the end", () => songSelectionScreen.Selector.JumpToEnd(last: true));
        AddUntilStep("the group shows", () => songSelectionScreen.Selector.SkimLabelText?.EndsWith(" BPM") == true);
        AddUntilStep("and goes once it stops", () => songList.CurrentIndex == 29 && songSelectionScreen.Selector.SkimLabelText == null);

        AddStep("Remove screen", () => songSelectionScreen.Exit());
        sortBy(BeatmapSort.Added);
    }

    /// <summary>
    /// Picking a difficulty over the strip, the info panel keeps clear of it and of the hero tile rising out
    /// of it. On a desktop canvas its tabs fit under it; on a phone's they move to the top right. Picked on
    /// the rail first, so the tabs start out laid out under the panel.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TestInfoPanelClearsTheStrip(bool phone)
    {
        addSongs(10);
        AddStep(phone ? "a phone" : "a desktop", () =>
        {
            gameLayout.Current.Value = phone ? DisplayLayout.Phone : DisplayLayout.Desktop;
            configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical;
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            // Less the footer's 100. A screen is sized relatively by default.
            RelativeSizeAxes = Axes.None,
            Size = phone ? new Vector2(1280, 620) : new Vector2(1920, 980)
        }));
        AddUntilStep("loaded", () => songSelectionScreen.IsLoaded);
        AddStep("pick the song", () => songSelectionScreen.Selector.EnterDifficultySelection());
        AddUntilStep("the details are in", () => !songSelectionScreen.InfoPanel.IsLoading);
        // The rows slide in one after another; until they have, the panel isn't its full size.
        AddWaitStep("let the rows settle", 1500);
        AddAssert("the tabs under the panel, by the rail", () => songSelectionScreen.InfoPanel.DetailsHost == null);

        AddStep("over to the strip", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Horizontal);
        AddWaitStep("let it settle", 1500);

        AddAssert(phone ? "the tabs at the top right" : "the tabs under the panel",
            () => (songSelectionScreen.InfoPanel.DetailsHost != null) == phone);
        AddAssert("nothing over the strip or the hero", () =>
        {
            RectangleF hero = songSelectionScreen.Selector.HeroRect!.Value;
            float bandTop = songSelectionScreen.Selector.BandTop;

            bool clear(RectangleF r) => r.Y + r.Height <= bandTop + 1
                                        && (r.Y + r.Height <= hero.Y + 1 || r.X + r.Width <= hero.X + 1 || r.X >= hero.X + hero.Width - 1);

            return clear(songSelectionScreen.InfoPanel.DrawRectangle)
                   && (songSelectionScreen.InfoPanel.DetailsHost is not Drawable details || clear(details.DrawRectangle));
        });

        AddStep("back to the rail", () =>
        {
            configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical;
            gameLayout.Current.Value = DisplayLayout.Desktop;
        });
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    /// <summary>
    /// On a phone, the info panel beside the rail is as big as the room left of it allows. Back on the rail
    /// with a song picked over the strip, it's that size again: the song list, which steps aside for the
    /// difficulties, was last laid out as the strip, and the room isn't measured from that.
    /// </summary>
    [Test]
    public void TestInfoPanelFitsTheRailBackFromTheStrip()
    {
        float byTheRail = 0;

        addSongs(10);
        AddStep("a phone, vertical", () =>
        {
            gameLayout.Current.Value = DisplayLayout.Phone;
            configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical;
        });
        AddStep("Add screen", () => screenStack.Push(songSelectionScreen = new SongSelectionScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(1280, 735)
        }));
        AddUntilStep("loaded", () => songSelectionScreen.IsLoaded);
        AddWaitStep("let it settle", 1500);
        AddStep("note the panel's size by the rail", () => byTheRail = songSelectionScreen.InfoPanel.Scale.X);

        AddStep("over to the strip", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Horizontal);
        AddStep("pick the song", () => songSelectionScreen.Selector.EnterDifficultySelection());
        AddWaitStep("let it settle", 1000);
        AddStep("back to the rail", () => configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical);
        AddWaitStep("let it settle", 500);
        AddAssert("the panel's the size it was", () => Math.Abs(songSelectionScreen.InfoPanel.Scale.X - byTheRail) < 0.01f);

        AddStep("back to the desktop", () => gameLayout.Current.Value = DisplayLayout.Desktop);
        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    /// <summary>
    /// On a phone: the header is a bar, the list starts under it and is the phone's size; back on the
    /// desktop, everything is the desktop's again.
    /// </summary>
    [Test]
    public void TestPhoneLayout()
    {
        addSongs(30);
        AddStep("a phone, vertical", () =>
        {
            gameLayout.Current.Value = DisplayLayout.Phone;
            configManager.Get<CarouselOrientation>(YuukiSetting.CarouselOrientation).Value = CarouselOrientation.Vertical;
        });
        pushScreen();

        AddUntilStep("the header is a bar", () => songSelectionScreen.Selector.FilterArea.DrawSize.Y == BeatmapFilterControl.PHONE_BAR_HEIGHT);
        AddUntilStep("the song window is the phone's", () => songList.FocusWindowRect.Height == 136);
        AddAssert("the list starts below the bar", () =>
        {
            var filters = songSelectionScreen.Selector.FilterArea.DrawRectangle;
            var list = songSelectionScreen.Selector.ListArea.DrawRectangle;
            return list.Y >= filters.Y + filters.Height - 1;
        });

        AddStep("back to the desktop", () => gameLayout.Current.Value = DisplayLayout.Desktop);
        AddUntilStep("the full header again", () => songSelectionScreen.Selector.FilterArea.DrawSize.Y == BeatmapFilterControl.HEIGHT);
        AddUntilStep("the desktop's window", () => songList.FocusWindowRect.Height == 140);

        AddStep("Remove screen", () => songSelectionScreen.Exit());
    }

    private void sortBy(BeatmapSort sort) => AddStep($"sort by {sort}", () => configManager.Get<BeatmapSort>(YuukiSetting.SongSelectSort).Value = sort);

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
