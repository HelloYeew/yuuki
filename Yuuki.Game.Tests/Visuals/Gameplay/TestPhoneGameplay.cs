// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Gameplay in a phone's window, zoomed in as the menus are (<see cref="GameLayout.GameplayCanvas"/>) so the HUD reads:
/// its widest pieces at the top, the health bar and the score, stay off the highway, whose far end they're beside.
/// </summary>
public partial class TestPhoneGameplay : TestScene
{
    [Cached]
    private readonly GameLayout gameLayout = new GameLayout
    {
        Current = { Value = DisplayLayout.Phone },
        PhoneWindow = { Value = true },
        Touch = { Value = true }
    };

    private GameplayContainer gameplay = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [Test]
    public void TestAPhonePlaysOnTheZoomedCanvas() => AddAssert("1280×720", () => gameLayout.GameplayCanvas == GameLayout.PHONE_CANVAS);

    /// <summary>
    /// On a 16:9 phone's canvas, the narrowest a phone's is, and a wider one's.
    /// </summary>
    [TestCase(16f / 9)]
    [TestCase(19.5f / 9)]
    public void TestTheHudStaysOffTheHighway(float aspect)
    {
        play(aspect);

        AddAssert("the health bar's end isn't on the lanes", () =>
        {
            Quad row = hud.HealthRow.ScreenSpaceDrawQuad;
            return !onTheLanes(row.TopRight) && !onTheLanes(row.BottomRight);
        });
        AddAssert("nor is the score's start", () =>
        {
            Quad score = hud.ScoreBlock.ScreenSpaceDrawQuad;
            return !onTheLanes(score.TopLeft) && !onTheLanes(score.BottomLeft);
        });
    }

    /// <summary>
    /// The combo is as much bigger as the rest, centred between the highway's edge and the screen's, with room either
    /// side for a five-digit combo. (Text has no width in a headless test, so the room is what's checked.)
    /// </summary>
    [TestCase(16f / 9)]
    [TestCase(19.5f / 9)]
    public void TestTheComboIsBiggerWithRoomForIt(float aspect)
    {
        play(aspect);

        AddAssert("half as big again as the field draws it", () => Math.Abs(hud.ComboScale - fieldScale * (float)GameLayout.PHONE_ZOOM) < 0.001f);
        AddAssert("room either side for five digits", () =>
        {
            float edge = hud.ToLocalSpace(playfield.ToScreenSpace(playfield.ComboHighwayEdge)).X;
            float room = Math.Min(hud.ComboCentre.X - edge, hud.DrawWidth - hud.ComboCentre.X);
            return room >= five_digits_half_width * hud.ComboScale;
        });
    }

    /// <summary>
    /// The judgement in the middle of the field ("Critical") is as much bigger as the rest of the HUD.
    /// </summary>
    [Test]
    public void TestTheJudgementIsBigger()
    {
        play(16f / 9);
        AddAssert("half as big again", () => Math.Abs(playfield.JudgementScale - (float)GameLayout.PHONE_ZOOM) < 0.001f);
    }

    // Half of five digits at the combo's 120: Nunito Bold's are about 0.6 em wide.
    private const float five_digits_half_width = 5 * 0.6f * 120 / 2;

    // How big the playfield draws a unit of its own, in the HUD's.
    private float fieldScale => Vector2.Distance(hud.ToLocalSpace(playfield.ToScreenSpace(Vector2.Zero)), hud.ToLocalSpace(playfield.ToScreenSpace(new Vector2(1, 0))));

    private void play(float aspect)
    {
        AddStep("play on a phone's canvas", () =>
        {
            Clear();
            Add(new Container
            {
                Size = new Vector2(GameLayout.PHONE_CANVAS.Y * aspect, GameLayout.PHONE_CANVAS.Y),
                Child = gameplay = new GameplayContainer(new PlayableBeatmap(new[]
                {
                    new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 60_000, EndTime = 60_000 }
                }, laneCount: 4))
                {
                    RelativeSizeAxes = Axes.Both,
                    LeadInTime = 0
                }
            });
        });
        AddUntilStep("laid out", () => gameplay.IsLoaded && hud.HealthRow.DrawWidth > 0);
    }

    private GameplayHud hud => descendants<GameplayHud>(gameplay).Single();

    private Playfield playfield => descendants<Playfield>(gameplay).Single();

    // On the lanes or within half a lane of them, as a touch is taken.
    private bool onTheLanes(Vector2 screenSpace)
    {
        var lanes = new LanePerspective { LaneCount = 4 };
        lanes.LayOut(playfield.DrawWidth, playfield.DrawHeight, touch: true);
        return lanes.LaneAt(playfield.ToLocalSpace(screenSpace)) >= 0;
    }

    private static IEnumerable<T> descendants<T>(Drawable drawable)
    {
        if (drawable is T found)
            yield return found;

        if (drawable is not Container container)
            yield break;

        foreach (Drawable child in container.InternalChildren)
        {
            foreach (T inner in descendants<T>(child))
                yield return inner;
        }
    }
}
