// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

public partial class TestKeycapStrip : YuukiManualInputManagerTestScene
{
    private KeycapCaptureGroup group = null!;
    private KeycapStrip lanes = null!;
    private KeycapStrip swipes = null!;
    private Key[]? saved;

    private static readonly Key[] default_lanes = { Key.D, Key.F, Key.J, Key.K };

    [SetUp]
    public void SetUpStrips()
    {
        AddStep("add strips", () =>
        {
            YuukiColorPalette palette = DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);
            group = new KeycapCaptureGroup();
            saved = null;

            lanes = new KeycapStrip(palette, default_lanes, default_lanes, group)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                IsReserved = key => key == Key.LShift
            };
            lanes.KeysChanged += keys => saved = keys;

            swipes = new KeycapStrip(palette, new[] { Key.LShift, Key.RShift }, null, group)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Y = 80
            };

            TestContent.Add(new KeyHost(group) { RelativeSizeAxes = Axes.Both, Children = new Drawable[] { lanes, swipes } });
        });
        AddUntilStep("caps shown", () => lanes.Children.Count == 4 && swipes.Children.Count == 2);
    }

    [Test]
    public void TestRebindAKey()
    {
        clickCap(() => lanes, 0);
        AddAssert("waiting for a key", () => lanes.IsCapturing);
        pressKey(Key.A);
        AddAssert("lane 1 is A", () => lanes.Keys.SequenceEqual(new[] { Key.A, Key.F, Key.J, Key.K }));
        AddAssert("saved", () => saved != null && saved[0] == Key.A);
        AddAssert("done waiting", () => !lanes.IsCapturing);
        AddAssert("off its default", () => !lanes.IsDefault);
    }

    [Test]
    public void TestAKeyInTheRowSwapsOver()
    {
        clickCap(() => lanes, 0);
        pressKey(Key.K);
        AddAssert("D and K swapped", () => lanes.Keys.SequenceEqual(new[] { Key.K, Key.F, Key.J, Key.D }));
    }

    [Test]
    public void TestReservedKeyIsRefused()
    {
        clickCap(() => lanes, 1);
        pressKey(Key.LShift);
        AddAssert("unchanged", () => lanes.Keys.SequenceEqual(default_lanes) && saved == null);
        AddAssert("still waiting", () => lanes.IsCapturing);
        pressKey(Key.Escape);
        AddAssert("Escape cancels", () => !lanes.IsCapturing && lanes.Keys.SequenceEqual(default_lanes));
    }

    [Test]
    public void TestOneRowWaitsAtATime()
    {
        clickCap(() => lanes, 2);
        clickCap(() => swipes, 0);
        AddAssert("the lanes stopped waiting", () => !lanes.IsCapturing);
        AddAssert("the swipes wait", () => swipes.IsCapturing && group.Capturing == swipes);
        pressKey(Key.Q);
        AddAssert("left swipe is Q", () => swipes.Keys[0] == Key.Q);
        AddAssert("lanes untouched", () => lanes.Keys.SequenceEqual(default_lanes));
    }

    [Test]
    public void TestResetToDefault()
    {
        clickCap(() => lanes, 3);
        pressKey(Key.L);
        AddStep("reset", () => lanes.ResetToDefault());
        AddAssert("default again", () => lanes.IsDefault && saved != null && saved.SequenceEqual(default_lanes));
    }

    private void clickCap(System.Func<KeycapStrip> strip, int index)
    {
        AddStep($"click cap {index + 1}", () =>
        {
            InputManager.MoveMouseTo(strip().Children[index]);
            InputManager.Click(MouseButton.Left);
        });
    }

    private void pressKey(Key key)
    {
        AddStep($"press {key}", () =>
        {
            InputManager.PressKey(key);
            InputManager.ReleaseKey(key);
        });
    }

    /// <summary>Hands key presses to the waiting strip, as the settings overlay and setup screen do.</summary>
    private partial class KeyHost : Container
    {
        private readonly KeycapCaptureGroup group;

        public KeyHost(KeycapCaptureGroup group) => this.group = group;

        public override bool OnKeyDown(KeyEvent e) => group.HandleKey(e) || base.OnKeyDown(e);
    }
}
