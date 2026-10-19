// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Alignment inside the gameplay HUD: the accuracy hangs off the score's right edge, EARLY/LATE
/// centres under the judgement, and the pause menu's title centres over its buttons. All three once
/// sat in vertical flows, which reset their children's anchors to top-left on every layout and so put
/// them flush left. Headless runs have no fonts (text measures 0×0), so these check what broke —
/// the anchors surviving layout — and the look itself is for the test browser.
/// </summary>
public partial class TestHudAlignment : TestScene
{
    private static readonly YuukiColorPalette palette = DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

    [Test]
    public void TestAccuracyRightAlignedUnderScore()
    {
        GameplayHud hud = null!;

        AddStep("add HUD", () =>
        {
            Clear();
            Add(hud = new GameplayHud(new ScoreProcessor(), new HealthProcessor(), new SkillProcessor(), () => 0, 60_000));
        });
        AddWaitStep("let layout run", 100);

        AddAssert("score and accuracy both hang off the right", () =>
            find(hud, t => t.Text.Length == 7 && t.Text.All(char.IsDigit))?.Anchor == Anchor.TopRight
            && find(hud, t => t.Text.EndsWith('%') && t.Text.Contains('.'))?.Anchor == Anchor.TopRight);
    }

    [Test]
    public void TestTimingCentredUnderJudgement()
    {
        JudgementPopup popup = null!;

        AddStep("add popup", () =>
        {
            Clear();
            Add(popup = new JudgementPopup { Anchor = Anchor.Centre });
        });
        AddStep("show an early break", () => popup.Show(new JudgementResult(new BeatmapNote { Type = NoteType.Tap }, HitResult.Break, -30)));
        AddWaitStep("let layout run", 100);

        AddAssert("BREAK and EARLY both centred", () =>
            find(popup, t => t.Text == "BREAK")?.Anchor == Anchor.TopCentre
            && find(popup, t => t.Text == "EARLY")?.Anchor == Anchor.TopCentre);
    }

    [Test]
    public void TestPauseMenuTitleCentred()
    {
        GameplayMenuOverlay menu = null!;

        AddStep("add pause menu", () =>
        {
            Clear();
            Add(menu = new GameplayMenuOverlay("Paused", "Take a breath.", palette,
                (IconUsage.PlayArrow, "Continue", () => { }),
                (IconUsage.Replay, "Retry", () => { }),
                (IconUsage.Logout, "Quit", () => { })));
            menu.Show();
        });
        AddWaitStep("let layout run", 100);

        AddAssert("title and subtitle centred", () =>
            find(menu, t => t.Text == "PAUSED")?.Anchor == Anchor.TopCentre
            && find(menu, t => t.Text == "Take a breath.")?.Anchor == Anchor.TopCentre);
    }

    /// <summary>The health bar shakes when hurt. It sits in a flow, which used to put it straight back.</summary>
    [Test]
    public void TestHurtShakeShows()
    {
        GameplayHud hud = null!;
        HealthProcessor health = null!;
        float rest = 0;
        float furthest = 0;

        AddStep("add HUD", () =>
        {
            Clear();
            Add(hud = new GameplayHud(new ScoreProcessor(), health = new HealthProcessor(), new SkillProcessor(), () => 0, 60_000));
        });
        AddWaitStep("let layout run", 10);
        AddStep("note where the bar rests", () => rest = barX(hud));

        AddStep("take a hit", () => health.Health.Value = 0.8);
        AddUntilStep("the bar shakes", () =>
        {
            furthest = Math.Max(furthest, Math.Abs(barX(hud) - rest));
            return furthest > 1;
        });
        AddUntilStep("and settles back", () => Math.Abs(barX(hud) - rest) < 0.5f);
    }

    /// <summary>The name tab runs 24px off the left edge, its slanted end out of sight, as the design has it.</summary>
    [Test]
    public void TestNameTabHangsOffTheEdge()
    {
        GameplayHud hud = null!;

        AddStep("add HUD", () =>
        {
            Clear();
            Add(hud = new GameplayHud(new ScoreProcessor(), new HealthProcessor(), new SkillProcessor(), () => 0, 60_000));
        });
        AddWaitStep("let layout run", 10);

        AddAssert("24px past the left edge", () =>
        {
            Drawable tab = find(hud, t => t.Text == GameplayHud.DEFAULT_CHARACTER_NAME)!.Parent!;
            return Math.Abs(hud.ToLocalSpace(new Vector2(tab.DrawRectangle.X, tab.DrawRectangle.Y)).X + 24) < 0.5f;
        });
    }

    /// <summary>
    /// Where the health bar is, across the HUD: the layer holding the bar and its heart (found through
    /// the heart, since an icon has no glyph to measure headless).
    /// </summary>
    private static float barX(GameplayHud hud)
    {
        var bar = descendants(hud).OfType<IconSprite>().First(icon => icon.Icon == IconUsage.Favorite).Parent!;
        return hud.ToLocalSpace(new Vector2(bar.DrawRectangle.X, bar.DrawRectangle.Y)).X;
    }

    private static SpriteText? find(Drawable root, Func<SpriteText, bool> match) => descendants(root).OfType<SpriteText>().FirstOrDefault(match);

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
