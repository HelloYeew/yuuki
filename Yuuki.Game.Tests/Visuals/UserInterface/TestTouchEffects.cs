// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.Cursor;

namespace Yuuki.Game.Tests.Visuals.UserInterface;

/// <summary>
/// The touch effects, where a desktop can try them: the mouse stands in for a finger (press for a tap, hold and
/// drag for a trail), over a light half and a dark half, in each screen's colors. They're the size the game draws
/// them, in screen units, however the test browser scales its scenes.
/// </summary>
public partial class TestTouchEffects : YuukiTestScene
{
    private TouchEffects effects = null!;
    private Box light = null!;
    private Box dark = null!;

    // How many pieces were up right after the last step made them: the browser's steps take their time, and the
    // pieces don't wait.
    private int madeNow;

    [SetUp]
    public void SetUp()
    {
        AddStep("an area to touch", () =>
        {
            Clear();

            effects = new TouchEffects { RelativeSizeAxes = Axes.Both };

            Add(light = new Box { RelativeSizeAxes = Axes.Both, Width = 0.5f });
            Add(dark = new Box { RelativeSizeAxes = Axes.Both, Width = 0.5f, Anchor = Anchor.TopRight, Origin = Anchor.TopRight });
            Add(new MouseFinger(effects) { RelativeSizeAxes = Axes.Both });
            Add(effects);

            colors(YuukiScreenPart.MainMenu);
        });
    }

    /// <summary>
    /// To try by hand: tap and drag with the mouse, in each screen's colors and at any size.
    /// </summary>
    [Test]
    public void TestEachScreensColors()
    {
        AddSliderStep("size", 0.5f, 2.5f, 1.25f, size => effects.Size = size);

        foreach (YuukiScreenPart part in Enum.GetValues<YuukiScreenPart>())
        {
            AddStep($"{part} colors", () => colors(part));
            tap(0.25f, 0.5f);
            tap(0.75f, 0.5f);
        }
    }

    [Test]
    public void TestATapBurstsThenClears()
    {
        tap(0.5f, 0.5f);
        AddAssert("eight stars, two outlines and a petal", () => madeNow == 11);
        AddUntilStep("all gone in a moment", () => effects.ActiveParticles == 0);
    }

    [Test]
    public void TestADragLeavesATrail()
    {
        AddStep("drag across", () =>
        {
            effects.FingerDown(TouchSource.Touch1, at(0.2f, 0.5f));

            for (int i = 1; i <= 10; i++)
                effects.FingerMove(TouchSource.Touch1, at(0.2f, 0.5f) + new Vector2(20 * i, 0));

            effects.FingerUp(TouchSource.Touch1);
            madeNow = effects.ActiveParticles;
        });
        AddAssert("stars all along it", () => madeNow >= 11 + 2 * 10);
    }

    [Test]
    public void TestEachFingerHasItsOwnTrail()
    {
        AddStep("two fingers down, the second moving", () =>
        {
            effects.FingerDown(TouchSource.Touch1, at(0.3f, 0.3f));
            effects.FingerDown(TouchSource.Touch2, at(0.7f, 0.7f));
            // A little over one spacing of the trail (14 at size 1, 17.5 at the default 1.25).
            effects.FingerMove(TouchSource.Touch2, at(0.7f, 0.7f) + new Vector2(20, 0));
            madeNow = effects.ActiveParticles;
        });
        // Two bursts, and one drop of the second finger's trail: measured from where it went down, not the first.
        AddAssert("two bursts and a drop", () => madeNow == 2 * 11 + 2);
    }

    [Test]
    public void TestTheStarsTakeTheScreensColors()
    {
        AddStep("song select's colors", () => colors(YuukiScreenPart.Play));
        tap(0.5f, 0.5f);
        AddAssert("every star in one of them, outlines and the petal white", () =>
        {
            var palette = DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);
            Color[] allowed = { palette.Accent, palette.TextMuted, palette.AccentLight, palette.TextLight, Color.White };

            return effects.Children.OfType<Sprite>().Where(s => s.Alpha > 0).All(s => allowed.Contains(s.Color));
        });
    }

    [Test]
    public void TestNoneInPlay()
    {
        AddStep("a song is playing", () => effects.Suppressed.Value = true);
        tap(0.5f, 0.5f);
        AddAssert("nothing", () => madeNow == 0);
    }

    [Test]
    public void TestNoneWhenTurnedOff()
    {
        AddStep("turned off", () => effects.Enabled.Value = false);
        tap(0.5f, 0.5f);
        AddAssert("nothing", () => madeNow == 0);
    }

    /// <summary>
    /// The petal is 30 screen units long at size 1, however the effects are scaled where they're drawn.
    /// </summary>
    [TestCase(1f)]
    [TestCase(2f)]
    public void TestTheSizeIsOnScreen(float scale)
    {
        AddStep($"drawn at {scale}×, at size 1", () =>
        {
            effects.Scale = new Vector2(scale);
            effects.Size = 1;
        });
        tap(0.25f, 0.25f);
        AddAssert("a 30-long petal", () =>
        {
            Sprite petal = effects.Children.OfType<Sprite>().Single(s => s.Alpha > 0 && s.Size.Y > s.Size.X);
            float screenUnitsPerUnit = Vector2.Distance(effects.ToScreenSpace(Vector2.Zero), effects.ToScreenSpace(new Vector2(0, 1)));

            return Math.Abs(petal.Size.Y * screenUnitsPerUnit - 30) < 0.5f;
        });
    }

    private void tap(float x, float y) => AddStep($"tap at {x}, {y}", () =>
    {
        effects.FingerDown(TouchSource.Touch1, at(x, y));
        effects.FingerUp(TouchSource.Touch1);
        madeNow = effects.ActiveParticles;
    });

    // A point on the area, as a fraction of it, on screen.
    private Vector2 at(float x, float y) => effects.ToScreenSpace(effects.DrawSize * new Vector2(x, y));

    private void colors(YuukiScreenPart part)
    {
        YuukiColorPalette palette = DefaultYuukiColorProvider.GetDefaultPaletteForPart(part);

        effects.Palette.Value = palette;
        light.Color = palette.Surface;
        dark.Color = palette.Backdrop;
    }

    /// <summary>
    /// The mouse as a finger: pressing is one going down, moving while pressed is it dragging, letting go is it
    /// lifting.
    /// </summary>
    private partial class MouseFinger : Container
    {
        private readonly TouchEffects effects;
        private bool pressed;

        public MouseFinger(TouchEffects effects)
        {
            this.effects = effects;
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            pressed = true;
            effects.FingerDown(TouchSource.Touch1, e.ScreenSpaceMousePosition);
            return true;
        }

        public override bool OnMouseMove(MouseEvent e)
        {
            if (pressed)
                effects.FingerMove(TouchSource.Touch1, e.ScreenSpaceMousePosition);

            return base.OnMouseMove(e);
        }

        public override bool OnMouseUp(MouseButtonEvent e)
        {
            pressed = false;
            effects.FingerUp(TouchSource.Touch1);
            return true;
        }
    }
}
