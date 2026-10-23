// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestBeatmapInfoPanel : TestScene
{
    private BeatmapInfoPanel panel = null!;

    private readonly BeatmapSet set = new BeatmapTestUtility().ThemeSet;

    private Beatmap beatmap => new Beatmap
    {
        BeatmapSet = set,
        DifficultyName = "Hard",
        DifficultyRating = 7.4,
        Creator = "HelloYeew",
        Notes = new[]
        {
            new BeatmapNote { Type = NoteType.Tap, StartTime = 1000, EndTime = 1000 },
            new BeatmapNote { Type = NoteType.Hold, StartTime = 1500, EndTime = 2000 },
            new BeatmapNote { Type = NoteType.Swipe, StartTime = 2500, EndTime = 2500 }
        }
    };

    private const int plays = 5;

    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    [BackgroundDependencyLoader]
    private void load()
    {
        var scores = new ScoreStore(null);
        for (int i = 0; i < plays; i++)
            scores.Add(new ScoreInfo { BeatmapId = beatmap.ID, BeatmapSetId = set.ID });

        Cache(scores);
        Cache(config);
    }

    private void add(BeatmapInfoPanel.PanelLayout layout = BeatmapInfoPanel.PanelLayout.Full, bool showCover = false, string tag = "Solo Play")
    {
        AddStep("add panel", () =>
        {
            Clear();
            Add(panel = new BeatmapInfoPanel(tag, DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play), layout, showCover)
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48)
            });
            panel.AnimateIn();
        });
    }

    /// <summary>
    /// The title and the rows under it end on one straight slanted line, the rows' own slant, however many of
    /// them are on show.
    /// </summary>
    [TestCase(BeatmapInfoPanel.PanelLayout.Full)]
    [TestCase(BeatmapInfoPanel.PanelLayout.Compact)]
    public void TestTheRowsEndInAStraightLine(BeatmapInfoPanel.PanelLayout layout)
    {
        add(layout);
        AddStep("show beatmap", () => panel.ShowBeatmap(beatmap));
        AddUntilStep("all on one line", () =>
        {
            Quad[] shapes = panel.RowShapes.ToArray();

            return shapes.Length == (layout == BeatmapInfoPanel.PanelLayout.Full ? 4 : 2)
                   && shapes.Skip(1).All(s => onTheEdgeOf(shapes[0], s));
        });
    }

    /// <summary>
    /// The chart under the note counts ends on the counts' slanted line too.
    /// </summary>
    [Test]
    public void TestTheDetailRowsEndInAStraightLine()
    {
        add();
        AddStep("show beatmap", () => panel.ShowBeatmap(beatmap));
        AddUntilStep("loaded", () => !panel.IsLoading);
        AddUntilStep("on one line", () =>
        {
            Quad[] shapes = panel.DetailRowShapes.ToArray();
            return onTheEdgeOf(shapes[0], shapes[1]);
        });
    }

    // Whether a shape's slanted right end lies along another's, drawn on screen: both its corners there on the line
    // through that end's.
    private static bool onTheEdgeOf(Quad above, Quad shape) =>
        offLine(shape.TopRight, above.TopRight, above.BottomRight) < 0.5f && offLine(shape.BottomRight, above.TopRight, above.BottomRight) < 0.5f;

    private static float offLine(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 along = Vector2.Normalize(b - a);
        Vector2 off = point - a;
        return Math.Abs(off.X * along.Y - off.Y * along.X);
    }

    [Test]
    public void TestSetThenBeatmap()
    {
        add();
        AddStep("show set", () => panel.ShowSet(set));
        AddStep("show beatmap", () => panel.ShowBeatmap(beatmap));
        AddStep("boss tab", () => panel.SelectTab(BeatmapInfoPanel.InfoTab.Boss));
        AddStep("ranking tab", () => panel.SelectTab(BeatmapInfoPanel.InfoTab.Ranking));
        AddStep("detail tab", () => panel.SelectTab(BeatmapInfoPanel.InfoTab.Detail));
        AddStep("back to set", () => panel.ShowSet(set));
        AddStep("unicode", () => panel.UseUnicode = true);
        AddStep("animate out", () => panel.AnimateOut());
        AddStep("animate in", () => panel.AnimateIn());
    }

    /// <summary>
    /// The setup screen hands the panel its beatmap straight after constructing it, before the panel
    /// can resolve the score store. What it shows must still catch up once it loads, without a tab
    /// click to refresh it.
    /// </summary>
    [Test]
    public void TestBeatmapGivenBeforeLoad()
    {
        AddStep("add panel already showing a beatmap", () =>
        {
            Clear();

            var created = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48)
            };
            created.ShowBeatmap(beatmap);

            Add(panel = created);
            panel.AnimateIn();
        });
        AddUntilStep("play counts shown", () => texts(panel).Count(t => t == plays.ToString()) == 2);
    }

    /// <summary>
    /// The setup and results screens build the panel once and never touch it again, so the panel has
    /// to follow the unicode setting itself when it's changed from the settings overlay. Read off the
    /// artist: the test song's title is the same both ways ("2:23 AM"), its artist isn't.
    /// </summary>
    [Test]
    public void TestFollowsUnicodeSetting()
    {
        AddAssert("the artist differs between the forms", () => set.ArtistUnicode != set.Artist);

        AddStep("romanized", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = false);
        AddStep("add panel already showing a beatmap", () =>
        {
            Clear();

            var created = new BeatmapInfoPanel("Solo Play", DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Position = new Vector2(-30, 48)
            };
            created.ShowBeatmap(beatmap);

            Add(panel = created);
            panel.AnimateIn();
        });
        AddUntilStep("romanized artist", () => texts(panel).Contains(set.Artist));

        AddStep("switch to unicode", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = true);
        AddAssert("unicode artist", () => panel.UseUnicode && texts(panel).Contains(set.ArtistUnicode) && !texts(panel).Contains(set.Artist));

        AddStep("switch back", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = false);
        AddAssert("romanized again", () => !panel.UseUnicode && texts(panel).Contains(set.Artist) && !texts(panel).Contains(set.ArtistUnicode));
    }

    private static IEnumerable<string> texts(Drawable drawable)
    {
        if (drawable is SpriteText text)
            yield return text.Text;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (string t in texts(child))
                    yield return t;
            }
        }
    }

    [Test]
    public void TestCompact()
    {
        add(BeatmapInfoPanel.PanelLayout.Compact);
        AddStep("show beatmap", () => panel.ShowBeatmap(beatmap));
    }

    [Test]
    public void TestWithCover()
    {
        add(showCover: true);
        AddStep("show beatmap", () => panel.ShowBeatmap(beatmap));
    }

    [Test]
    public void TestTagFitsTheScreenName()
    {
        SpriteText name = null!;

        add(tag: "Edit");
        AddStep("show set", () => panel.ShowSet(set));
        AddStep("find the name", () => name = find(panel, "EDIT")!);

        // The name's holder slides; the row around it is the tag.
        AddUntilStep("slid in", () => name.Parent!.X == 0 && name.Parent.Alpha == 1);
        AddAssert("the tag is the name and its margins wide", () =>
        {
            Drawable row = name.Parent!.Parent!;
            return Precision.AlmostEquals(row.DrawWidth, name.DrawWidth + name.Margin.Total.X, 0.5f);
        });
        AddAssert("not the old fixed width", () => name.Parent!.Parent!.DrawWidth < 220);
    }

    private static SpriteText? find(Drawable drawable, string text) => drawable switch
    {
        SpriteText sprite when sprite.Text == text => sprite,
        Container container => container.Children.Select(child => find(child, text)).FirstOrDefault(found => found != null),
        _ => null
    };

    [Test]
    public void TestNothingSelected()
    {
        add();
        AddStep("show nothing", () => panel.ShowSet(null));
    }
}
