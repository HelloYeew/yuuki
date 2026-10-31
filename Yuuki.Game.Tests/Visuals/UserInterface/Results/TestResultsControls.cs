// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Results;
using Yuuki.Game.Resources;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

/// <summary>
/// The results' small controls and strips: the pages' tabs, More info's chips (one that starts on, one off), its header, and
/// the summary's strip of where a run stands among its difficulty's plays.
/// </summary>
public partial class TestResultsControls : YuukiTestScene
{
    private PageTabs tabs = null!;
    private ResultsChip filter = null!, pick = null!;
    private MoreInfoHeader header = null!;
    private FlowContainer strips = null!;
    private int? picked;
    private bool? filtered;

    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    // Where a song's cover is read from (the game's own textures stand in for one), and the settings.
    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<Storage>(new EmbeddedResourceStorage(YuukiResourceAssembly.ResourceAssembly, "Yuuki.Game.Resources"));
        Cache(config);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("show them", () =>
        {
            Clear();

            YuukiColorPalette palette = ResultsTestRuns.Palette;
            picked = null;
            filtered = null;

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#2A2F45") });
            Add(new FlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Left = 48, Right = 48, Top = 24 },
                Direction = FlowDirection.Vertical,
                Spacing = new Vector2(0, 20),
                Children = new Drawable[]
                {
                    header = new MoreInfoHeader(palette),
                    new FlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FlowDirection.Horizontal,
                        Spacing = new Vector2(12, 0),
                        Children = new Drawable[]
                        {
                            filter = new ResultsChip("bloom 477", palette, JudgementColors.BLOOM) { On = true, Toggled = on => filtered = on },
                            pick = new ResultsChip("3", palette)
                        }
                    },
                    tabs = new PageTabs(palette, "Summary", "More info") { PagePicked = page => picked = page },
                    strips = new FlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FlowDirection.Vertical,
                        Spacing = new Vector2(0, 16)
                    }
                }
            });

            header.ShowSong(null, null);
            header.ShowRun(ResultsTestRuns.Rich(), 208);
        });
    }

    [Test]
    public void TestChipsAndTabs()
    {
        AddStep("click the filter", () => filter.Action!());
        AddAssert("off", () => filtered == false && !filter.On);
        AddStep("pick lane 3", () => pick.On = true);
        AddStep("click more info", () => ((Sakura.Framework.Graphics.Containers.ClickableContainer)tabs.Children[1]).Action!());
        AddAssert("picked", () => picked == 1 && tabs.Current == 1);
    }

    /// <summary>
    /// A chip looks on while it is, or while it's one of a set with none picked, which shows them all.
    /// </summary>
    [Test]
    public void TestChipLooks()
    {
        AddAssert("one that's on looks it", () => filter.LooksOn);
        AddStep("turn it off", () => filter.On = false);
        AddAssert("and it doesn't", () => !filter.LooksOn);
        AddAssert("one that's off doesn't", () => !pick.LooksOn);
        AddStep("none of its set picked", () => pick.OnAnyway = true);
        AddAssert("looks on, though it isn't", () => pick.LooksOn && !pick.On);
        AddStep("one of its set picked, not it", () => pick.OnAnyway = false);
        AddAssert("looks off", () => !pick.LooksOn);
        AddStep("it picked", () => pick.On = true);
        AddAssert("looks on", () => pick.LooksOn);
    }

    /// <summary>
    /// Given its song before it loaded, as the results screen gives it, the header still shows the song's cover.
    /// </summary>
    [Test]
    public void TestTheCoverShowsWhenGivenBeforeLoad()
    {
        MoreInfoHeader early = null!;

        AddStep("a header given its song, then shown", () =>
        {
            early = new MoreInfoHeader(ResultsTestRuns.Palette) { Y = 400 };
            early.ShowSong(null, new BeatmapSet { ID = 900, Title = "With a cover", Artist = "Someone", CoverStoragePath = "Textures/Results/petal.png" });
            Add(early);
        });
        AddUntilStep("its cover", () => early.HasCover);
    }

    /// <summary>
    /// The song's title and artist follow the Unicode setting, as song select's do, changing as it's changed.
    /// </summary>
    [Test]
    public void TestTheUnicodeSetting()
    {
        AddStep("romanised names", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = false);
        AddStep("a song with names in its own script", () => header.ShowSong(null, new BeatmapSet
        {
            Title = "Hanabira Signal",
            TitleUnicode = "花びらシグナル",
            Artist = "Tsukuyomi Lab",
            ArtistUnicode = "月読ラボ"
        }));
        AddAssert("romanised", () => header.Title == "Hanabira Signal" && header.Details.StartsWith("Tsukuyomi Lab", StringComparison.Ordinal));
        AddStep("turn the setting on", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = true);
        AddAssert("in its own script, at once", () => header.Title == "花びらシグナル" && header.Details.StartsWith("月読ラボ", StringComparison.Ordinal));
        AddStep("and off", () => config.Get<bool>(YuukiSetting.UseUnicodeInfo).Value = false);
        AddAssert("romanised again", () => header.Title == "Hanabira Signal");
    }

    [Test]
    public void TestWhereARunStands()
    {
        AddStep("a new best, and second of nine", () =>
        {
            YuukiColorPalette palette = ResultsTestRuns.Palette;
            var before = new ScoreInfo { Score = 954_321, Accuracy = 0.9541, Rank = ScoreRank.S, IsFullCombo = true, Date = DateTimeOffset.Now.AddDays(-3) };
            var run = new ScoreInfo { Score = 987_654, Accuracy = 0.9616, Rank = ScoreRank.A, Date = DateTimeOffset.Now };

            PlaysStrip newBest;
            strips.Children = new Drawable[]
            {
                newBest = new PlaysStrip(palette, run, before, true, 1, 9),
                new PlaysStrip(palette, run, before, false, 2, 9),
                new PlaysStrip(palette, run, null, true, 1, 1)
            };

            newBest.Compare(1);
        });
    }
}
