// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Audio;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Containers;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestVolumeOverlay : YuukiManualInputManagerTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    [Resolved]
    private IAudioManager audio { get; set; } = null!;

    private VolumeHost host = null!;
    private readonly List<UISound> played = new List<UISound>();

    private VolumeOverlay overlay => host.Overlay;
    private double master => frameworkConfig.Get<double>(FrameworkSetting.MasterVolume).Value;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("every volume at a half, the control hidden", () =>
        {
            frameworkConfig.Get<double>(FrameworkSetting.MasterVolume).Value = 0.5;
            frameworkConfig.Get<double>(FrameworkSetting.TrackVolume).Value = 0.5;
            frameworkConfig.Get<double>(FrameworkSetting.SampleVolume).Value = 0.5;
            config.Get<double>(YuukiSetting.HitsoundVolume).Value = 0.5;

            played.Clear();
            TestContent.Add(host = new VolumeHost());
            host.Sounds.Played += (sound, _) => played.Add(sound);
        });
        AddUntilStep("loaded", () => overlay.IsLoaded);
        AddAssert("hidden", () => overlay.State == Visibility.Hidden);
    }

    [Test]
    public void TestFirstPressOnlyBringsItUp()
    {
        press(Key.Up, KeyModifiers.Alt);
        AddAssert("up, on master", () => overlay.State == Visibility.Visible && overlay.Selected.Label == "Master");
        AddAssert("not turned yet", () => master == 0.5);

        waitForAccelerationReset();
        press(Key.Up, KeyModifiers.Alt);
        AddAssert("a notch up", () => Math.Abs(master - 0.51) < 1e-9);

        waitForAccelerationReset();
        press(Key.Down, KeyModifiers.Alt);
        AddAssert("and back down", () => Math.Abs(master - 0.5) < 1e-9);
    }

    [Test]
    public void TestQuickTurnsSpeedUp()
    {
        AddStep("bring it up", () => overlay.Show());
        AddStep("four quick turns", () =>
        {
            for (int i = 0; i < 4; i++)
                overlay.Adjust(1);
        });
        AddAssert("more than four notches", () => master > 0.5 + 0.04 + 1e-9);

        double before = 0;
        AddStep("note it", () => before = master);
        waitForAccelerationReset();
        AddStep("one more after a pause", () => overlay.Adjust(1));
        AddAssert("one notch again", () => Math.Abs(master - before - 0.01) < 1e-9);
    }

    [Test]
    public void TestTrackpadFractionsGather()
    {
        AddStep("bring it up", () => overlay.Show());
        AddStep("a third of a notch", () => overlay.Adjust(0.3));
        AddAssert("not yet", () => master == 0.5);

        waitForAccelerationReset();
        AddStep("and the same again, twice", () =>
        {
            overlay.Adjust(0.3);
            overlay.Adjust(0.3);
        });
        AddAssert("a whole notch", () => Math.Abs(master - 0.51) < 1e-9);
    }

    [Test]
    public void TestStopsAtTheEnds()
    {
        AddStep("nearly full", () => frameworkConfig.Get<double>(FrameworkSetting.MasterVolume).Value = 0.99);
        AddStep("turn it up a lot", () =>
        {
            for (int i = 0; i < 10; i++)
                overlay.Adjust(1);
        });
        AddAssert("full, no further", () => master == 1);
        AddUntilStep("reads MAX", () => overlay.Selected.ShownText == "MAX");

        AddStep("turn it right down", () =>
        {
            for (int i = 0; i < 60; i++)
                overlay.Adjust(-1);
        });
        AddAssert("silent, no further", () => master == 0);
        AddUntilStep("reads 0", () => overlay.Selected.ShownText == "0");
    }

    [Test]
    public void TestArrowsPickRingsWhileUp()
    {
        press(Key.Right, KeyModifiers.Alt);
        AddAssert("hidden, nothing picked", () => overlay.State == Visibility.Hidden && overlay.Selected.Label == "Master");

        AddStep("bring it up", () => overlay.Show());
        press(Key.Right, KeyModifiers.Alt);
        AddAssert("music", () => overlay.Selected.Label == "Music");
        AddAssert("with a hover sound", () => played.Contains(UISound.Hover));

        press(Key.Left, KeyModifiers.Alt);
        press(Key.Left, KeyModifiers.Alt);
        AddAssert("round to hitsounds", () => overlay.Selected.Label == "Hitsounds");

        waitForAccelerationReset();
        press(Key.Up, KeyModifiers.Alt);
        AddAssert("that's what turns", () => Math.Abs(config.Get<double>(YuukiSetting.HitsoundVolume).Value - 0.51) < 1e-9 && master == 0.5);
    }

    [Test]
    public void TestOpeningAgainPicksMaster()
    {
        AddStep("up on effects", () =>
        {
            overlay.Show();
            overlay.SelectNext();
            overlay.SelectNext();
        });
        AddAssert("effects", () => overlay.Selected.Label == "Effects");
        AddUntilStep("tucks away", () => overlay.State == Visibility.Hidden);

        press(Key.Up, KeyModifiers.Alt);
        AddAssert("back on master", () => overlay.Selected.Label == "Master");
    }

    [Test]
    public void TestMuteKeepsTheSetting()
    {
        press(Key.F4, KeyModifiers.Control);
        AddAssert("muted, and up to say so", () => overlay.Muted.Value && overlay.State == Visibility.Visible);
        AddUntilStep("nothing let through", () => audio.MasterVolume.Value == 0);
        AddAssert("the setting kept", () => master == 0.5);

        press(Key.F4, KeyModifiers.Control);
        AddUntilStep("back as set", () => !overlay.Muted.Value && Math.Abs(audio.MasterVolume.Value - 0.5) < 1e-3);
    }

    [Test]
    public void TestAltScrollAnywhere()
    {
        AddStep("over the page", () => InputManager.MoveMouseTo(host.Page));

        AddStep("scroll", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("the page scrolled, no volume", () => host.Page.Scrolls == 1 && overlay.State == Visibility.Hidden);

        AddStep("hold Alt", () => InputManager.PressKey(Key.AltLeft));
        AddStep("Alt+scroll", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("brings it up instead", () => host.Page.Scrolls == 1 && overlay.State == Visibility.Visible && master == 0.5);

        waitForAccelerationReset();
        AddStep("Alt+scroll again", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("a notch up", () => Math.Abs(master - 0.51) < 1e-9);

        AddStep("over the zoomable part", () => InputManager.MoveMouseTo(host.Zoomable));
        AddStep("Alt+scroll there", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("it zooms; no volume", () => host.Zoomable.Scrolls == 1 && Math.Abs(master - 0.51) < 1e-9);

        AddStep("let go of Alt", () => InputManager.ReleaseKey(Key.AltLeft));
    }

    [Test]
    public void TestPointingPicksAndScrollTurns()
    {
        AddStep("bring it up", () => overlay.Show());
        AddUntilStep("in place", () => overlay.Alpha == 1);

        AddStep("point at effects", () => InputManager.MoveMouseTo(overlay.Meters[2]));
        AddAssert("effects picked", () => overlay.Selected.Label == "Effects");

        AddStep("scroll it", () => InputManager.ScrollBy(new Vector2(0, 1)));
        AddAssert("effects up a notch", () => Math.Abs(frameworkConfig.Get<double>(FrameworkSetting.SampleVolume).Value - 0.51) < 1e-9);
        AddAssert("the page didn't get it", () => host.Page.Scrolls == 0);
        AddUntilStep("it ticked", () => played.Contains(UISound.SliderTick));

        double shownAt = 0;
        AddStep("note the time", () => shownAt = overlay.Clock.CurrentTime);
        AddUntilStep("past its stay", () => overlay.Clock.CurrentTime > shownAt + VolumeOverlay.STAY_DURATION * 1.5);
        AddAssert("still up while pointed at", () => overlay.State == Visibility.Visible);

        AddStep("point away", () => InputManager.MoveMouseTo(host.Page));
        AddUntilStep("tucks away", () => overlay.State == Visibility.Hidden);
    }

    [Test]
    public void TestAChangeElsewhereBringsItUp()
    {
        AddStep("music changed from the settings", () => frameworkConfig.Get<double>(FrameworkSetting.TrackVolume).Value = 0.7);
        AddAssert("up", () => overlay.State == Visibility.Visible);
        AddUntilStep("and away again", () => overlay.State == Visibility.Hidden);
    }

    private void press(Key key, KeyModifiers modifiers)
    {
        Key modifier = modifiers == KeyModifiers.Control ? Key.ControlLeft : Key.AltLeft;

        AddStep($"{modifiers}+{key}", () =>
        {
            InputManager.PressKey(key, modifiers);
            InputManager.ReleaseKey(key, modifiers);
            InputManager.ReleaseKey(modifier);
        });
    }

    private void waitForAccelerationReset()
    {
        double from = 0;
        AddStep("pause", () => from = overlay.Clock.CurrentTime);
        AddUntilStep("a moment later", () => overlay.Clock.CurrentTime > from + 200);
    }

    /// <summary>The game's arrangement: the global keys at the back, a page, the control, Alt+scroll in front.</summary>
    private partial class VolumeHost : Container
    {
        public VolumeOverlay Overlay { get; private set; } = null!;
        public UISoundPlayer Sounds { get; private set; } = null!;
        public ScrollTarget Page { get; private set; } = null!;
        public ZoomTarget Zoomable { get; private set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            Overlay = new VolumeOverlay();
            Cache(Overlay);

            var fader = new InactiveVolumeFader { IsWindowActive = () => true };
            Cache(fader);

            Sounds = new UISoundPlayer();
            Cache(Sounds);

            Children = new Drawable[]
            {
                new YuukiKeyBindingContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new YuukiGlobalActionContainer()
                },
                fader,
                Sounds,
                Page = new ScrollTarget { RelativeSizeAxes = Axes.Both },
                Zoomable = new ZoomTarget
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Size = new Vector2(200)
                },
                Overlay,
                new VolumeScrollCatcher()
            };
        }
    }

    private partial class ScrollTarget : Container
    {
        public int Scrolls;

        public override bool OnScroll(ScrollEvent e)
        {
            Scrolls++;
            return true;
        }
    }

    /// <summary>Stands in for the editor's timeline, which zooms with Alt+scroll.</summary>
    private partial class ZoomTarget : ScrollTarget, IBlockGlobalAltScrollVolume
    {
    }
}
