// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Overlay;
using Yuuki.Game.Graphics.UserInterface.Results;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The screens on a phone's canvas (a 16:9 phone's, 1280×720) and on the smallest canvas there is (the
/// largest UI scale, about 1097×617), less the footer's 100 along the bottom: everything stays on the
/// screen and out of everything else's way.
/// </summary>
public partial class TestPhoneCanvas : TestScene
{
    [Cached]
    private readonly GameLayout gameLayout = new GameLayout();

    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    private Container canvas = null!;
    private YuukiScreenStack stack = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        gameLayout.Current.Value = DisplayLayout.Phone;
        gameLayout.Touch.Value = true;

        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(config);

        BeatmapSet theme = new BeatmapTestUtility().ThemeSet;
        workingBeatmap.BeatmapSet = theme;
        workingBeatmap.Beatmap = new Beatmap
        {
            BeatmapSet = theme,
            DifficultyName = "Hard",
            DifficultyRating = 7.4,
            Creator = "HelloYeew",
            Notes = new[]
            {
                new BeatmapNote { Type = NoteType.Tap, StartTime = 1000, EndTime = 1000 },
                new BeatmapNote { Type = NoteType.Hold, StartTime = 1500, EndTime = 2000 }
            }
        };
        Cache(workingBeatmap);

        // Built once for the fixture and re-added by each test's SetUp, so removal mustn't dispose it.
        canvas = new Container
        {
            Size = GameLayout.PHONE_CANVAS,
            DisposeOnRemoval = false,
            Child = stack = new YuukiScreenStack
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Bottom = 100 }
            }
        };
        Cache(stack);
    }

    [SetUp]
    public void SetUp() => AddStep("add the canvas", () => Add(canvas));

    private void sizeCanvas(bool smallest) => AddStep(smallest ? "the smallest canvas" : "a phone's canvas",
        () => canvas.Size = smallest ? GameLayout.MIN_CANVAS : GameLayout.PHONE_CANVAS);

    [TestCase(false)]
    [TestCase(true)]
    public void TestStartScreenAsksForATap(bool smallest)
    {
        sizeCanvas(smallest);
        StartScreen start = null!;

        push(() => start = new StartScreen());
        AddAssert("tap, not a key", () => start.ScreenHelperText.Value == "Tap anywhere to start");
        AddStep("exit", () => start.Exit());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestMainMenuButtonsFit(bool smallest)
    {
        sizeCanvas(smallest);
        MainMenuScreen menu = null!;

        push(() => menu = new MainMenuScreen());
        AddUntilStep("the menu's buttons are inside the screen", () => buttonsInside(menu, 4));
        AddStep("exit", () => menu.Exit());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestPlayScreenButtonsFit(bool smallest)
    {
        sizeCanvas(smallest);
        PlayScreen play = null!;

        push(() => play = new PlayScreen());
        AddUntilStep("the modes' buttons are inside the screen", () => buttonsInside(play, 3));
        AddStep("exit", () => play.Exit());
    }

    /// <summary>
    /// The song panel shrinks to the room the settings and the buttons leave it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TestSetupFits(bool smallest)
    {
        sizeCanvas(smallest);
        FinalSetupScreen setup = null!;

        push(() => setup = new FinalSetupScreen());
        AddUntilStep("the song panel is left of the settings and above the buttons", () =>
        {
            var song = descendants(setup).OfType<BeatmapInfoPanel>().Single();
            var settings = descendants(setup).OfType<YuukiScrollableContainer>().Single(s => descendants(s).OfType<YuukiSliderBar<int>>().Any());
            var back = descendants(setup).OfType<LeftBottomShearButton>().Single();

            var p = song.DrawRectangle;
            var s = settings.DrawRectangle;

            return !song.IsLoading
                   && song.Scale.X < 1
                   && p.X + p.Width <= s.X + 1
                   && p.Y + p.Height <= back.DrawRectangle.Y + 1;
        });
        AddStep("exit", () => setup.Exit());
    }

    /// <summary>
    /// The ring moves up under the song header and shrinks to fit above the buttons; the header and the
    /// ring both stay left of the stats.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TestResultsFit(bool smallest)
    {
        sizeCanvas(smallest);
        ScoreScreen results = null!;

        push(() => results = new ScoreScreen(new GameplayResult
        {
            CriticalCount = 300,
            BreakCount = 40,
            MissCount = 5
        }));
        AddWaitStep("settle", 1500);
        AddAssert("header and ring beside the stats, all above the buttons", () =>
        {
            var header = descendants(results).OfType<BeatmapInfoPanel>().Single().DrawRectangle;
            var ring = descendants(results).OfType<AccuracyCircle>().Single().Parent!.DrawRectangle;
            var stats = descendants(results).OfType<YuukiScrollableContainer>().First().DrawRectangle;
            var back = descendants(results).OfType<LeftBottomShearButton>().Single().DrawRectangle;

            return header.X + header.Width <= stats.X + 1
                   && ring.X + ring.Width <= stats.X + 1
                   && ring.Y >= header.Y + header.Height - 1
                   && ring.Y + ring.Height <= back.Y + 1
                   && stats.Y + stats.Height <= back.Y + 1;
        });
        AddStep("exit", () => results.Exit());
    }

    /// <summary>
    /// The settings' sidebar ends above the back button, and the rows' panel starts right of it.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TestSettingsFit(bool smallest)
    {
        SettingsOverlay settings = null!;

        sizeCanvas(smallest);
        AddStep("open settings", () =>
        {
            canvas.Add(settings = new SettingsOverlay());
            settings.Show();
        });
        AddUntilStep("the sidebar clears the back button and the panel", () =>
        {
            var sidebar = settings.Sidebar.DrawRectangle;
            var back = descendants(settings).OfType<LeftBottomShearButton>().Single().DrawRectangle;
            var panel = descendants(settings).OfType<YuukiScrollableContainer>().First().DrawRectangle;

            return settings.IsLoaded
                   && sidebar.Y + sidebar.Height <= back.Y + 1
                   && sidebar.X + sidebar.Width <= panel.X + 1;
        });
        AddStep("close", () => canvas.Remove(settings, true));
    }

    private void push(System.Func<YuukiScreen> create) => AddStep("push", () =>
    {
        YuukiScreen screen = create();
        screen.RelativeSizeAxes = Axes.Both;
        stack.Push(screen);
    });

    /// <summary>
    /// Whether the screen's <paramref name="count"/> big menu buttons (not the small ones, like Exit)
    /// are all inside it, side to side.
    /// </summary>
    private static bool buttonsInside(Drawable screen, int count)
    {
        var bounds = screen.DrawRectangle;
        var buttons = descendants(screen).OfType<MenuShearButton>().Where(b => b.DrawRectangle.Height > 80).ToList();

        return buttons.Count == count
               && buttons.All(b => b.DrawRectangle.X >= bounds.X && b.DrawRectangle.X + b.DrawRectangle.Width <= bounds.X + bounds.Width);
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
