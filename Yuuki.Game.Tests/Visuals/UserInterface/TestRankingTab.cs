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
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestRankingTab : YuukiManualInputManagerTestScene
{
    private static readonly BeatmapSet set = new BeatmapTestUtility().ThemeSet;
    private static readonly Beatmap beatmap = new Beatmap { ID = 5, BeatmapSet = set, DifficultyName = "Hard", Creator = "HelloYeew" };
    private static readonly Beatmap otherBeatmap = new Beatmap { ID = 6, BeatmapSet = set, DifficultyName = "Easy", Creator = "HelloYeew" };

    private readonly ScoreStore scores = new ScoreStore(null);
    private BeatmapInfoPanel panel = null!;
    private ScoreInfo? selected;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());

        foreach (long value in new long[] { 640_000, 910_000, 780_000 })
        {
            scores.Add(new ScoreInfo
            {
                BeatmapId = beatmap.ID,
                BeatmapSetId = set.ID,
                Score = value,
                Accuracy = value / 1_000_000.0,
                Rank = ScoreRankExtensions.FromAccuracy(value / 1_000_000.0),
                Date = DateTimeOffset.Now.AddDays(-2)
            });
        }

        scores.Add(new ScoreInfo
        {
            BeatmapId = otherBeatmap.ID,
            BeatmapSetId = set.ID,
            Score = 500_000,
            Accuracy = 0.8,
            Rank = ScoreRank.B,
            Date = DateTimeOffset.Now.AddDays(-1)
        });

        Cache(scores);
    }

    [Test]
    public void TestPlayedAgoCountsUpLive()
    {
        var fresh = new Beatmap { ID = 7, BeatmapSet = set, DifficultyName = "Normal", Creator = "HelloYeew" };

        AddStep("a play from three seconds ago", () => scores.Add(new ScoreInfo
        {
            BeatmapId = fresh.ID,
            BeatmapSetId = set.ID,
            Score = 700_000,
            Accuracy = 0.9,
            Rank = ScoreRank.A,
            Date = DateTimeOffset.Now.AddSeconds(-3)
        }));
        AddStep("show it on the Ranking tab", () =>
        {
            TestContent.Add(panel = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48)
            });
            panel.ShowBeatmap(fresh);
            panel.SelectTab(BeatmapInfoPanel.InfoTab.Ranking);
        });

        string first = "";
        AddUntilStep("seconds shown", () => (first = texts(panel).FirstOrDefault(t => t.EndsWith("s ago")) ?? "") != "");
        // Real seconds pass while the headless frames run, so allow a generous step timeout.
        AddUntilStep("it counts up on its own", () => texts(panel).Any(t => t.EndsWith("s ago") && t != first), 60_000);
    }

    [Test]
    public void TestSwitchingBeatmapLoadsThenShows()
    {
        AddStep("add panel on the Ranking tab", () =>
        {
            TestContent.Add(panel = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48)
            });
            panel.ShowBeatmap(beatmap);
            panel.SelectTab(BeatmapInfoPanel.InfoTab.Ranking);
            panel.AnimateIn();
        });
        AddAssert("this beatmap's scores", () => texts(panel).Contains("Personal Best (#1 of 3)"));

        AddStep("switch beatmap", () => panel.ShowBeatmap(otherBeatmap));
        AddAssert("loading", () => panel.IsLoading);
        // The old scores stay up (stepping aside) until the new ones are filled in behind the spinner.
        AddAssert("not swapped mid-exit", () => texts(panel).Contains("Personal Best (#1 of 3)"));

        AddUntilStep("loaded", () => !panel.IsLoading);
        AddAssert("the other beatmap's scores", () => texts(panel).Contains("Personal Best (#1 of 1)") && !texts(panel).Contains("Personal Best (#1 of 3)"));
    }

    [Test]
    public void TestClickingAScoreSelectsIt()
    {
        AddStep("add panel on the Ranking tab", () =>
        {
            selected = null;
            TestContent.Add(panel = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48)
            });
            panel.ShowBeatmap(beatmap);
            panel.SelectTab(BeatmapInfoPanel.InfoTab.Ranking);
            panel.AnimateIn();
            panel.ScoreSelected += score => selected = score;
        });

        AddAssert("personal best pinned", () => texts(panel).Contains("Personal Best (#1 of 3)"));

        AddUntilStep("panels step left going down, all one width", () =>
        {
            var rows = rowsOf(panel).Select(r => r.DrawRectangle).ToArray();
            return rows.Length == 4
                   && rows.Zip(rows.Skip(1)).All(pair => pair.First.X > pair.Second.X + 1)
                   && rows.All(r => Math.Abs(r.Width - rows[0].Width) < 1);
        });
        AddAssert("best first", () => positionOf("#1") < positionOf("#2") && positionOf("#2") < positionOf("#3"));

        // Let the rows slide in before aiming at one.
        AddWaitStep("settle", 1500);
        AddStep("click the second score", () =>
        {
            Drawable row = descendants(panel).OfType<SpriteText>().First(t => t.Text == "#2").Parent!;
            InputManager.MoveMouseTo(row);
            InputManager.PressButton(MouseButton.Left);
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddAssert("the second-best score was picked", () => selected?.Score == 780_000);
    }

    [Test]
    public void TestBestSitsAtTheBottomLimit()
    {
        AddStep("add panel reaching down to 900", () =>
        {
            TestContent.Add(panel = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48),
                RankingBottom = () => TestContent.ToScreenSpace(new Vector2(0, 900)).Y
            });
            panel.ShowBeatmap(beatmap);
            panel.SelectTab(BeatmapInfoPanel.InfoTab.Ranking);
            panel.AnimateIn();
        });

        AddUntilStep("best just above the limit", () =>
        {
            Drawable? best = rowsOf(panel).LastOrDefault();
            if (best == null)
                return false;

            float bottom = TestContent.ToLocalSpace(new Vector2(0, best.DrawRectangle.Y + best.DrawRectangle.Height)).Y;
            return bottom > 850 && bottom <= 900;
        });

        // The panel tucks its left ends past the screen's edge; the best, against that edge, keeps its
        // position in view all the same.
        AddWaitStep("let the rows settle", 1500);
        // Where each text starts, not its rectangle: headless, a text has no size.
        AddAssert("every position starts on screen", () =>
            descendants(panel).OfType<SpriteText>().Where(t => t.Text.StartsWith('#')).All(t => TestContent.ToLocalSpace(t.ScreenSpaceDrawQuad.TopLeft).X >= 0));
    }

    /// <summary>
    /// A score's badges (a full combo, a replay) sit by the score, clear of its stats however narrow the
    /// panels get (beside a phone's rail they're as narrow as they go).
    /// </summary>
    [Test]
    public void TestBadgesKeepClearOfTheStats()
    {
        var played = new Beatmap { ID = 8, BeatmapSet = set, DifficultyName = "Expert", Creator = "HelloYeew" };

        AddStep("a full combo with a replay", () => scores.Add(new ScoreInfo
        {
            BeatmapId = played.ID,
            BeatmapSetId = set.ID,
            Score = 967_807,
            Accuracy = 0.9642,
            Rank = ScoreRank.S,
            MaxCombo = 848,
            IsFullCombo = true,
            ReplayId = "replay",
            Date = DateTimeOffset.Now.AddDays(-1)
        }));
        AddStep("show it on panels as narrow as they go", () =>
        {
            TestContent.Add(panel = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48),
                RankingMaxRight = () => TestContent.ToScreenSpace(new Vector2(300, 0)).X
            });
            panel.ShowBeatmap(played);
            panel.SelectTab(BeatmapInfoPanel.InfoTab.Ranking);
            panel.AnimateIn();
        });
        AddWaitStep("let the rows settle", 1500);
        // Where the badges start, not their rectangle: headless, a text has no size. The stats' boxes do.
        AddAssert("the badges start past the accuracy", () => rowsOf(panel).Count() == 2 && rowsOf(panel).All(row =>
        {
            Drawable badges = descendants(row).OfType<SpriteText>().Single(t => t.Text == "FC").Parent!;
            Drawable accuracy = descendants(row).OfType<SpriteText>().Single(t => t.Text == "ACCURACY").Parent!;
            return badges.ScreenSpaceDrawQuad.TopLeft.X >= accuracy.DrawRectangle.X + accuracy.DrawRectangle.Width;
        }));
    }

    // Each score panel's visible part, top to bottom (the pinned best last): the parent of its "#n".
    private static IEnumerable<Drawable> rowsOf(Drawable root) =>
        descendants(root).OfType<SpriteText>().Where(t => t.Text.StartsWith('#')).Select(t => t.Parent!).OrderBy(r => r.DrawRectangle.Y);

    // The order the rows sit in their list (the pinned best comes after them all).
    private int positionOf(string label) => descendants(panel).OfType<SpriteText>().Select(t => t.Text).ToList().IndexOf(label);

    private static IEnumerable<string> texts(Drawable root) => descendants(root).OfType<SpriteText>().Select(t => t.Text);

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
