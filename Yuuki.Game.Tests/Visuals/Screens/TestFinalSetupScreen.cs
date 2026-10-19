// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The pre-play setup screen with a real config and beatmap: its song header, cover and note counts,
/// the scroll speed / audio offset / background dim sliders bound to their settings, and the revert
/// buttons that show beside a setting (or the lane keys) while it's off its default.
/// </summary>
public partial class TestFinalSetupScreen : TestYuukiScreenStack
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();
    private readonly WorkingBeatmap workingBeatmap = new WorkingBeatmap();

    [BackgroundDependencyLoader]
    private void load()
    {
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
                new BeatmapNote { Type = NoteType.Hold, StartTime = 1500, EndTime = 2000 },
                new BeatmapNote { Type = NoteType.Swipe, StartTime = 2500, EndTime = 2500 }
            }
        };
        Cache(workingBeatmap);
    }

    [Test]
    public void TestLoadsWithBeatmapAndSettings()
    {
        AddStep("set offset −44", () => config.Get<int>(YuukiSetting.AudioOffset).Value = -44);
        AddStep("push setup", () => screenStack.Push(new FinalSetupScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("setup is current", () => screenStack.CurrentScreen is FinalSetupScreen);
        AddAssert("in-memory notes kept", () => workingBeatmap.Beatmap.Notes.Length == 3);
        AddAssert("offset setting untouched by loading", () => config.Get<int>(YuukiSetting.AudioOffset).Value == -44);
        AddStep("exit", () => screenStack.CurrentScreen!.Exit());
    }

    [Test]
    public void TestRevertOffset()
    {
        resetSettings();
        AddStep("set offset −44", () => config.Get<int>(YuukiSetting.AudioOffset).Value = -44);
        pushSetup();
        AddUntilStep("one revert shows", () => enabledReverts().Count() == 1);
        AddStep("click it", () => enabledReverts().Single().Action());
        AddAssert("offset back to 0", () => config.Get<int>(YuukiSetting.AudioOffset).Value == 0);
        AddUntilStep("no reverts show", () => !enabledReverts().Any());
        AddStep("exit", () => screenStack.CurrentScreen!.Exit());
    }

    [Test]
    public void TestRevertLaneKeys()
    {
        resetSettings();
        AddStep("rebind the lanes", () => config.Get<string>(YuukiSetting.LaneKeys4).Value = "A,S,K,L");
        pushSetup();
        // Anything scrolled out of view stops updating, revert buttons included.
        AddStep("scroll down to the controls", () => settingsScroll().ScrollToEnd(false));
        AddUntilStep("one revert shows", () => enabledReverts().Count() == 1);
        AddStep("click it", () => enabledReverts().Single().Action());
        AddAssert("default layout saved", () =>
            LaneKeyLayouts.Parse(config.Get<string>(YuukiSetting.LaneKeys4).Value, 4).SequenceEqual(LaneKeyLayouts.Default(4)));
        AddUntilStep("no reverts show", () => !enabledReverts().Any());
        AddStep("exit", () => screenStack.CurrentScreen!.Exit());
    }

    [Test]
    public void TestSettingsStayOnScreen()
    {
        resetSettings();
        pushSetup();
        AddUntilStep("the settings sit inside the screen, above Start", () =>
        {
            var screen = (Drawable)screenStack.CurrentScreen!;
            RectangleF bounds = screen.DrawRectangle;
            RectangleF settings = settingsScroll().DrawRectangle;
            float startTop = all(screen).OfType<RightBottomShearButton>().Single().DrawRectangle.Y;

            return settings.Y >= bounds.Y
                   && settings.X + settings.Width <= bounds.X + bounds.Width
                   && settings.Y + settings.Height <= startTop;
        });
        AddStep("exit", () => screenStack.CurrentScreen!.Exit());
    }

    // The config outlives each test, so a test that counts revert buttons starts from the defaults.
    private void resetSettings() => AddStep("settings to defaults", () =>
    {
        foreach (YuukiSetting setting in new[] { YuukiSetting.ScrollSpeed, YuukiSetting.AudioOffset, YuukiSetting.BackgroundDim, YuukiSetting.BackgroundBlur })
            config.Get<int>(setting).Value = config.Get<int>(setting).Default;

        config.Get<bool>(YuukiSetting.ScrollSpeedFollowsBpm).Value = false;
        config.Get<bool>(YuukiSetting.DisableGameplayVideo).Value = false;
        config.Get<string>(YuukiSetting.LaneKeys4).Value = string.Empty;
    });

    private void pushSetup()
    {
        AddStep("push setup", () => screenStack.Push(new FinalSetupScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("setup is current", () => screenStack.CurrentScreen is FinalSetupScreen { IsLoaded: true });
    }

    // The song panel's Ranking tab scrolls too; the settings' scroll is the one holding the sliders.
    private YuukiScrollableContainer settingsScroll() =>
        all(screenStack.CurrentScreen!).OfType<YuukiScrollableContainer>().Single(s => all(s).OfType<YuukiSliderBar<int>>().Any());

    private IEnumerable<RevertToDefaultButton> enabledReverts() =>
        all(screenStack.CurrentScreen!).OfType<RevertToDefaultButton>().Where(b => b.Enabled.Value);

    private static IEnumerable<Drawable> all(Drawable root)
    {
        yield return root;

        if (root is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (Drawable descendant in all(child))
                    yield return descendant;
            }
        }
    }
}
