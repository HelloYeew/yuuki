// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.HUD;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

public partial class TestHudCombo : YuukiTestScene
{
    private ScoreProcessor score = null!;

    private PlayfieldAdjustmentContainer adjustment = null!;
    private Playfield playfield = null!;
    private GameplayHud hud = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("add a playfield and its HUD", () =>
        {
            Clear();
            score = new ScoreProcessor();

            var chart = new PlayableBeatmap(new[] { new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 60_000 } }, laneCount: 4);

            Add(adjustment = new PlayfieldAdjustmentContainer
            {
                Child = playfield = new Playfield(chart)
            });
            Add(hud = new GameplayHud(score, new HealthProcessor(), new SkillProcessor(), () => 0, 60_000)
            {
                ComboSpace = playfield
            });
        });
        AddUntilStep("laid out", () => hud.ComboScale > 0 && playfield.DrawSize.X > 0);
    }

    [Test]
    public void TestBesideTheHighway()
    {
        AddAssert("right of the playfield's middle", () => hud.ComboCentre.X > hud.ToLocalSpace(playfield.ToScreenSpace(new Vector2(playfield.DrawSize.X / 2, 0))).X);
        AddAssert("inside the playfield's right edge", () => hud.ComboCentre.X < hud.ToLocalSpace(playfield.ToScreenSpace(new Vector2(playfield.DrawSize.X, 0))).X);
        AddAssert("above the middle of the screen", () => hud.ComboCentre.Y < hud.DrawSize.Y / 2);
    }

    [Test]
    public void TestFollowsPlayfieldSize()
    {
        float fullScale = 0;

        AddStep("note the full size", () => fullScale = hud.ComboScale);
        AddStep("halve the playfield", () => adjustment.PlayfieldSize = 0.5f);
        AddUntilStep("the combo halves too", () => System.Math.Abs(hud.ComboScale - fullScale / 2) < 0.01f);

        AddStep("push the playfield left", () => adjustment.PlayfieldPosition = new Vector2(0, 0.5f));
        AddUntilStep("the combo goes with it", () => hud.ComboCentre.X < hud.DrawSize.X / 2);
    }

    [Test]
    public void TestShowsOnlyWithACombo()
    {
        AddAssert("hidden at the start", () => !hud.ComboShown);

        AddStep("hit three", () => score.Combo.Value = 3);
        AddAssert("shows the number alone", () => hud.ComboShown && hud.ComboText == "3");

        AddStep("break it", () => score.Combo.Value = 0);
        AddUntilStep("shakes out", () => !hud.ComboShown);
        AddAssert("the last number stayed as it went", () => hud.ComboText == "3");

        AddStep("hit again", () => score.Combo.Value = 1);
        AddAssert("back at once", () => hud.ComboShown && hud.ComboText == "1");
    }

    [Test]
    public void TestFreezesWithTheGameplayClock()
    {
        GameplayClockContainer clock = null!;

        AddStep("a HUD on a stopped gameplay clock", () =>
        {
            Clear();
            score = new ScoreProcessor();

            Add(clock = new GameplayClockContainer());
            Add(hud = new GameplayHud(score, new HealthProcessor(), new SkillProcessor(), () => 0, 60_000)
            {
                ComboClock = clock.GameplayClock
            });
        });
        AddUntilStep("loaded", () => hud.IsLoaded);

        AddStep("hit three, then break it", () =>
        {
            score.Combo.Value = 3;
            score.Combo.Value = 0;
        });
        AddWaitStep("well past the fade", 40);
        AddAssert("still up, as the game is paused", () => hud.ComboShown);

        AddStep("resume", () => clock.Start());
        AddUntilStep("now it goes", () => !hud.ComboShown);
    }
}
