// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.ScreenStacks;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.Screens;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class TestUIScale : TestScene
{
    private DrawSizePreservingFillContainer canvas = null!;
    private YuukiScreenStack stack = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    private void build(double uiScale) => AddStep($"canvas at {uiScale}×", () =>
    {
        Clear();
        Add(canvas = new DrawSizePreservingFillContainer
        {
            TargetDrawSize = new Vector2(1920 / (float)uiScale, 1080 / (float)uiScale),
            Child = stack = new YuukiScreenStack { RelativeSizeAxes = Axes.Both }
        });
    });

    [Test]
    public void TestGameplayKeepsItsSize()
    {
        float scaledAtOne = 0, fixedAtOne = 0, scaledAtLarge = 0, fixedAtLarge = 0;

        measure(1, w => scaledAtOne = w, scales: true);
        measure(1, w => fixedAtOne = w, scales: false);
        measure(1.25, w => scaledAtLarge = w, scales: true);
        measure(1.25, w => fixedAtLarge = w, scales: false);

        AddAssert("a menu screen grows with the scale", () => Math.Abs(scaledAtLarge / scaledAtOne - 1.25f) < 0.01f);
        AddAssert("a gameplay screen doesn't", () => Math.Abs(fixedAtLarge / fixedAtOne - 1) < 0.01f);
    }

    private void measure(double uiScale, Action<float> result, bool scales)
    {
        Box box = null!;

        build(uiScale);
        AddStep("push a screen with a 100-wide box", () =>
        {
            YuukiScreen screen = scales ? new MenuLikeScreen() : new GameplayLikeScreen();
            screen.RelativeSizeAxes = Axes.Both;
            screen.Add(box = new Box { Size = new Vector2(100) });
            stack.Push(screen);
        });
        AddUntilStep("laid out", () => box.DrawRectangle.Width > 0);
        AddStep("measure", () => result(box.DrawRectangle.Width));
    }

    [Test]
    public void TestResultsFitAtTheLargestScale()
    {
        ScoreScreen results = null!;

        build(Configurations.YuukiConfigManager.MAX_UI_SCALE);
        AddStep("show results", () => stack.Push(results = new ScoreScreen(new GameplayResult
        {
            CriticalCount = 300,
            BreakCount = 40,
            MissCount = 5
        })
        {
            RelativeSizeAxes = Axes.Both
        }));

        // After the slide-in, compare where things ended up.
        AddWaitStep("settle", 1500);
        AddAssert("ring beside the stats, both above the buttons", () =>
        {
            Drawable ring = descendants(results).OfType<AccuracyCircle>().Single().Parent!;
            Drawable stats = descendants(results).OfType<YuukiScrollableContainer>().Single();
            Drawable back = descendants(results).OfType<LeftBottomShearButton>().Single();

            var r = ring.DrawRectangle;
            var s = stats.DrawRectangle;
            var b = back.DrawRectangle;

            return r.X + r.Width <= s.X + 1
                   && s.Y + s.Height <= b.Y + 1
                   && r.Y + r.Height <= b.Y + 1;
        });
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

    private partial class MenuLikeScreen : YuukiScreen
    {
    }

    private partial class GameplayLikeScreen : YuukiScreen
    {
        protected override bool ScalesWithUI => false;
    }
}
