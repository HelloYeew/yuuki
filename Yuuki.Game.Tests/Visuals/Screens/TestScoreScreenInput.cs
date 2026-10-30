// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

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
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The results screen by the mouse: a click on nothing skips the reveal, and a click on a button in the stats (which sit
/// in the layer whose color drains as an S breaks) still reaches it.
/// </summary>
public partial class TestScoreScreenInput : YuukiManualInputManagerTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private YuukiScreenStack screenStack = null!;
    private ScoreScreen screen = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);

        screenStack = new YuukiScreenStack
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            DisposeOnRemoval = false
        };
        Cache(screenStack);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("show results 25 ms late", () =>
        {
            TestContent.Add(screenStack);
            screenStack.Push(screen = new ScoreScreen(leaning(25)) { RelativeSizeAxes = Axes.Both });
        });
        AddUntilStep("results is current", () => screenStack.CurrentScreen == screen);
    }

    [Test]
    public void TestAClickOnNothingSkips()
    {
        AddStep("click by the header", () =>
        {
            InputManager.MoveMouseTo(screen.ToScreenSpace(new Vector2(screen.DrawSize.X / 2, 20)));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("skipped to just before the rank", () => screen.RevealTime >= screen.Timeline.FirstLanding - RevealTimeline.SKIP_LEAD);
    }

    [Test]
    public void TestAClickReachesTheStats()
    {
        PillButton shift = null!;

        AddUntilStep("the shift offered", () => (shift = descendants(screen).OfType<PillButton>().FirstOrDefault(p => p.Text.StartsWith("Shift timing", System.StringComparison.Ordinal))!) != null);
        AddWaitStep("let the stats slide in", 800);
        // The window's short (800 by 600 headless): the timing is below the stats' fold.
        AddStep("scroll to it", () => descendants(screen).OfType<YuukiScrollableContainer>().First().ScrollToEnd(false));
        AddStep("click it", () =>
        {
            InputManager.MoveMouseTo(shift);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("the timing moved", () => config.Get<int>(YuukiSetting.AudioOffset).Value == -25);
        AddAssert("and it didn't skip", () => screen.RevealTime < screen.Timeline.FirstLanding - RevealTimeline.SKIP_LEAD);
    }

    private static GameplayResult leaning(double error) => new GameplayResult
    {
        CriticalCount = 30,
        Judgements = Enumerable.Range(0, 30).Select(i => new JudgementResult(new BeatmapNote { Type = NoteType.Tap, StartTime = 1000 + i * 500 }, HitResult.Critical, error)).ToArray(),
        ChartLength = 16_000
    };

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
