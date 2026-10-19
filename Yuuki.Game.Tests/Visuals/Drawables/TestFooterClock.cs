// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text.RegularExpressions;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Drawables;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.Drawables;

/// <summary>
/// The footer's clock: clicking steps through the modes, the setting picks one (as a
/// controller would, from the settings), and the 24-hour preference rewrites the time at once.
/// </summary>
public partial class TestFooterClock : YuukiManualInputManagerTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private FooterClock clock = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("settings to defaults", () =>
        {
            config.Get<ClockDisplayMode>(YuukiSetting.ClockDisplayMode).Value = ClockDisplayMode.Full;
            config.Get<bool>(YuukiSetting.Prefer24HourTime).Value = true;
        });
        AddStep("add clock", () =>
        {
            TestContent.Add(clock = new FooterClock
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            });
            var palette = DefaultYuukiColorProvider.GetDefaultPalette();
            clock.SetAccent(palette.Text, palette.TextMuted, palette.Surface);
        });
        AddUntilStep("loaded", () => clock.IsLoaded);
    }

    [Test]
    public void TestClickCycles()
    {
        AddAssert("starts on everything", () => clock.DisplayMode == ClockDisplayMode.Full && clock.ShowsFace && clock.ShowsDigits && clock.ShowsRuntime);

        click();
        AddAssert("digital with running time", () => clock.DisplayMode == ClockDisplayMode.DigitalWithRuntime && !clock.ShowsFace && clock.ShowsRuntime);

        click();
        AddAssert("digital", () => clock.DisplayMode == ClockDisplayMode.Digital && !clock.ShowsFace && clock.ShowsDigits && !clock.ShowsRuntime);

        click();
        AddAssert("analog", () => clock.DisplayMode == ClockDisplayMode.Analog && clock.ShowsFace && !clock.ShowsDigits);

        click();
        AddAssert("back to everything", () => clock.DisplayMode == ClockDisplayMode.Full);
        AddAssert("and the click is saved", () => config.Get<ClockDisplayMode>(YuukiSetting.ClockDisplayMode).Value == ClockDisplayMode.Full);
    }

    [Test]
    public void TestSettingPicksMode()
    {
        float fullWidth = 0;

        AddStep("note the full width", () => fullWidth = clock.Width);

        AddStep("set analog", () => config.Get<ClockDisplayMode>(YuukiSetting.ClockDisplayMode).Value = ClockDisplayMode.Analog);
        AddAssert("only the face", () => clock.ShowsFace && !clock.ShowsDigits);
        AddUntilStep("narrows to the face and its padding", () => clock.Width == 42);
        AddAssert("narrower than everything", () => clock.Width < fullWidth);

        AddStep("set digital", () => config.Get<ClockDisplayMode>(YuukiSetting.ClockDisplayMode).Value = ClockDisplayMode.Digital);
        AddAssert("only the digits", () => !clock.ShowsFace && clock.ShowsDigits && !clock.ShowsRuntime);
    }

    [Test]
    public void Test24HourTime()
    {
        AddAssert("24-hour", () => Regex.IsMatch(clock.ShownTime, @"^\d{2}:\d{2}:\d{2}$"));
        AddAssert("running time shown", () => Regex.IsMatch(clock.ShownRunningTime, @"^(\d+\.)?\d{2}:\d{2}:\d{2}$"));

        AddStep("prefer 12-hour", () => config.Get<bool>(YuukiSetting.Prefer24HourTime).Value = false);
        AddAssert("rewritten right away", () => Regex.IsMatch(clock.ShownTime, @"^\d{1,2}:\d{2}:\d{2} (AM|PM)$"));
    }

    private void click()
    {
        AddStep("click the clock", () =>
        {
            InputManager.MoveMouseTo(clock);
            InputManager.PressButton(MouseButton.Left);
            InputManager.ReleaseButton(MouseButton.Left);
        });
    }
}
