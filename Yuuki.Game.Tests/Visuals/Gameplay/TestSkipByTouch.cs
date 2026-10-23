// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Skipping an intro by touch: the skip band lies over the lanes, which take the fingers that land on them, so the
/// band takes its own.
/// </summary>
public partial class TestSkipByTouch : YuukiManualInputManagerTestScene
{
    private SkipOverlay overlay = null!;
    private int skips;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUpOverlay()
    {
        AddStep("lanes, with the skip over them", () =>
        {
            skips = 0;
            TestContent.Add(new Lanes { RelativeSizeAxes = Axes.Both });
            TestContent.Add(overlay = new SkipOverlay(-2000, 3000, 500, () => -2000) { RequestSkip = () => skips++ });
        });
        AddUntilStep("it can skip", () => overlay.IsLoaded && overlay.CanSkip);
    }

    [Test]
    public void TestATapSkips()
    {
        AddStep("tap the band", () => InputManager.Tap(overlay.ToScreenSpace(overlay.DrawSize / 2)));
        AddAssert("skipped", () => skips == 1);
    }

    [Test]
    public void TestSlidingOffDoesNot()
    {
        AddStep("press the band, slide off it and lift", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, overlay.ToScreenSpace(overlay.DrawSize / 2));
            InputManager.MoveTouchTo(TouchSource.Touch1, overlay.ToScreenSpace(new Sakura.Framework.Maths.Vector2(overlay.DrawWidth / 2, -200)));
            InputManager.TouchUp(TouchSource.Touch1);
        });
        AddAssert("not skipped", () => skips == 0);
    }

    // Stands in for the lanes: takes every finger that lands on it.
    private partial class Lanes : Container
    {
        public override bool OnTouchDown(TouchEvent e) => true;
    }
}
