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
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestSongSelectionPeek : YuukiManualInputManagerTestScene
{
    private readonly BeatmapsCollection beatmapsCollection = new BeatmapsCollection();
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();
    private readonly ScoreStore scores = new ScoreStore(null);

    private YuukiScreenStack screenStack = null!;
    private SongSelectionScreen songSelect = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        beatmapsCollection.BeatmapSets = BeatmapTestUtility.CreateDummyBeatmapSets(5);
        var beatmaps = new List<Beatmap>();
        foreach (BeatmapSet set in beatmapsCollection.BeatmapSets)
            beatmaps.AddRange(BeatmapTestUtility.CreateDummyBeatmaps(set, 2));
        beatmapsCollection.Beatmaps = beatmaps;

        Cache(beatmapsCollection);
        Cache(config);
        Cache(workingBeatmap);
        Cache(scores);

        // More scores than the Ranking tab has room for, full combos with replays, on every difficulty.
        foreach (int beatmapId in beatmaps.Select(b => b.ID).Distinct())
        {
            for (int i = 0; i < 8; i++)
            {
                scores.Add(new ScoreInfo
                {
                    BeatmapId = beatmapId,
                    Score = 950_000 - i * 20_000,
                    Accuracy = 0.97 - i * 0.01,
                    Rank = ScoreRankExtensions.FromAccuracy(0.97 - i * 0.01),
                    MaxCombo = 848,
                    IsFullCombo = true,
                    ReplayId = "replay",
                    Date = DateTimeOffset.Now.AddDays(-i)
                });
            }
        }
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());

        screenStack = new YuukiScreenStack
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            DisposeOnRemoval = false
        };
        Cache(screenStack);
    }

    [Test]
    public void TestHoldHidesTheCursorToo()
    {
        AddStep("show song select", () =>
        {
            TestContent.Add(screenStack);
            screenStack.Push(songSelect = new SongSelectionScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("it's up", () => songSelect.IsLoaded);
        AddAssert("the cursor shows", () => !songSelect.CursorHidden.Value);

        // Over the background below the song panel, clear of the list, the buttons and the footer.
        AddStep("press and hold on the background", () =>
        {
            InputManager.MoveMouseTo(songSelect.ToScreenSpace(new Vector2(songSelect.DrawWidth * 0.3f, songSelect.DrawHeight * 0.7f)));
            InputManager.PressButton(MouseButton.Left);
        });
        AddUntilStep("peeking", () => songSelect.IsPeeking);
        AddAssert("the cursor is tucked away", () => songSelect.CursorHidden.Value);

        AddStep("let go", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("not peeking", () => !songSelect.IsPeeking);
        AddAssert("the cursor is back", () => !songSelect.CursorHidden.Value);

        AddStep("leave", () => songSelect.Exit());
    }
    /// <summary>
    /// By touch nothing is held down (a finger only clicks as it lifts), but a finger held still (a long press)
    /// glances, until it lifts.
    /// </summary>
    [Test]
    public void TestALongPressGlancesByTouch()
    {
        AddStep("show song select", () =>
        {
            TestContent.Add(screenStack);
            screenStack.Push(songSelect = new SongSelectionScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("it's up", () => songSelect.IsLoaded);

        AddStep("a finger on the background", () =>
            InputManager.TouchDown(TouchSource.Touch1, songSelect.ToScreenSpace(new Vector2(songSelect.DrawWidth * 0.3f, songSelect.DrawHeight * 0.7f))));
        AddUntilStep("glancing once it's held", () => songSelect.IsPeeking);
        AddStep("lift it", () => InputManager.TouchUp(TouchSource.Touch1));
        AddUntilStep("back", () => !songSelect.IsPeeking);

        AddStep("leave", () => songSelect.Exit());
    }

    /// <summary>
    /// A glance (holding a press to look at the background) leaves the Ranking tab as it is while the screen
    /// zooms and fades away: the text on each score stays where it is along it, and the list doesn't fill
    /// the room the footer leaves the screen as it goes.
    /// </summary>
    [Test]
    public void TestAGlanceLeavesTheRankingAsItIs()
    {
        List<float> alongBefore = null!;
        int shownBefore = 0;

        AddStep("show song select above the footer", () =>
        {
            screenStack.Padding = new MarginPadding { Bottom = 100 };
            TestContent.Add(screenStack);
            screenStack.Push(songSelect = new SongSelectionScreen
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both
            });
        });
        AddUntilStep("it's up", () => songSelect.IsLoaded);
        AddStep("pick the song, on the Ranking tab", () =>
        {
            songSelect.Selector.EnterDifficultySelection();
            songSelect.InfoPanel.SelectTab(BeatmapInfoPanel.InfoTab.Ranking);
        });
        AddUntilStep("the scores are in", () => !songSelect.InfoPanel.IsLoading && positions().Any());
        AddWaitStep("let the rows settle", 1500);
        AddStep("note the scores", () =>
        {
            alongBefore = positions().Select(t => t.X).ToList();
            shownBefore = positions().Count(shown);
        });

        // Above the song panel, clear of everything.
        AddStep("press and hold on the background", () =>
        {
            InputManager.MoveMouseTo(songSelect.ToScreenSpace(new Vector2(songSelect.DrawWidth * 0.3f, 20)));
            InputManager.PressButton(MouseButton.Left);
        });
        AddUntilStep("glancing", () => songSelect.IsPeeking);
        // As the game does when the footer hides.
        AddStep("the footer goes, leaving its room to the screen", () => screenStack.Padding = new MarginPadding());
        AddWaitStep("the screen zooms and fades", 64);
        AddAssert("the text on each score hasn't moved along it", () =>
            positions().Select(t => t.X).Zip(alongBefore).All(p => Math.Abs(p.First - p.Second) < 0.5f));
        AddAssert("no more scores show", () => positions().Count(shown) == shownBefore);

        AddStep("let go", () => InputManager.ReleaseButton(MouseButton.Left));
        AddStep("the footer's back", () => screenStack.Padding = new MarginPadding { Bottom = 100 });
        AddStep("leave", () => songSelect.Exit());
    }

    // Each score's "#n", in order (the pinned best last).
    private IEnumerable<SpriteText> positions() => descendants(songSelect.InfoPanel).OfType<SpriteText>().Where(t => t.Text.StartsWith('#'));

    // Whether a score shows on the panel: its fade, not the screen's.
    private bool shown(Drawable drawable)
    {
        float alpha = 1;

        for (Drawable? d = drawable; d != null && d != songSelect.InfoPanel; d = d.Parent)
            alpha *= d.Alpha;

        return alpha > 0.01f;
    }

    private static IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (Drawable d in descendants(child))
                    yield return d;
            }
        }
    }
}
