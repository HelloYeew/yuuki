// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Testing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// The word above the judgement line, and EARLY or LATE under it: always under a Break or a Hit, under a Critical by the
/// setting, since now a Bloom is what's on time and a Critical missed it one way or the other.
/// </summary>
public partial class TestJudgementPopup : TestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private JudgementPopup popup = null!;

    [BackgroundDependencyLoader]
    private void load() => Cache(config);

    [SetUp]
    public void SetUp()
    {
        AddStep("Show a popup, with the setting on", () =>
        {
            config.Get<bool>(YuukiSetting.ShowCriticalTiming).Value = true;

            Clear();
            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(popup = new JudgementPopup { Anchor = Anchor.Centre });
        });
    }

    private void show(HitResult result, double error, NoteType type = NoteType.Tap, bool holdTail = false) =>
        popup.Show(new JudgementResult(new BeatmapNote { Type = type }, result, error, holdTail));

    /// <summary>
    /// What it says under the word, as shown: an empty or hidden word says nothing.
    /// </summary>
    private string? saying => descendants(popup).OfType<SpriteText>().FirstOrDefault(t => t.Alpha > 0 && t.Text is "EARLY" or "LATE")?.Text;

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

    [Test]
    public void TestACriticalSaysWhichWayItMissed()
    {
        AddStep("A Critical 30 ms early", () => show(HitResult.Critical, -30));
        AddAssert("EARLY", () => saying == "EARLY");
        AddStep("A Critical 30 ms late", () => show(HitResult.Critical, 30));
        AddAssert("LATE", () => saying == "LATE");
    }

    /// <summary>
    /// Off, a Critical is back to saying nothing, and it counts from the next judgement: a player can change it while paused.
    /// </summary>
    [Test]
    public void TestItsSetting()
    {
        AddStep("Turn it off", () => config.Get<bool>(YuukiSetting.ShowCriticalTiming).Value = false);
        AddStep("A Critical 30 ms early", () => show(HitResult.Critical, -30));
        AddAssert("It says nothing", () => saying == null);
        AddStep("A Break 60 ms early", () => show(HitResult.Break, -60));
        AddAssert("A Break still says", () => saying == "EARLY");
        AddStep("A Hit 100 ms late", () => show(HitResult.Hit, 100));
        AddAssert("And a Hit", () => saying == "LATE");
        AddStep("Turn it on, with one showing", () =>
        {
            show(HitResult.Critical, -30);
            config.Get<bool>(YuukiSetting.ShowCriticalTiming).Value = true;
        });
        AddStep("The next Critical", () => show(HitResult.Critical, 30));
        AddAssert("It says", () => saying == "LATE");
    }

    [Test]
    public void TestWhatNeverSays()
    {
        AddStep("A Bloom a little early", () => show(HitResult.Bloom, -5));
        AddAssert("A Bloom is on time", () => saying == null);
        AddStep("A Miss", () => show(HitResult.Miss, -150));
        AddAssert("A Miss has nothing to time", () => saying == null);
        AddStep("A Pass note's Critical", () => show(HitResult.Critical, 100, NoteType.Pass));
        AddAssert("A Pass isn't pressed", () => saying == null);
        AddStep("A Touch note's Critical", () => show(HitResult.Critical, 30, NoteType.Touch));
        AddAssert("Nor a Touch", () => saying == null);
        AddStep("A Critical right on time", () => show(HitResult.Critical, 0));
        AddAssert("Neither early nor late", () => saying == null);
    }

    /// <summary>
    /// The word left over from a Critical that said something doesn't carry on under one that doesn't.
    /// </summary>
    [Test]
    public void TestOneThatSaysThenOneThatDoesnt()
    {
        AddStep("A Critical 30 ms early", () => show(HitResult.Critical, -30));
        AddStep("A Bloom", () => show(HitResult.Bloom, 2));
        AddAssert("It's gone", () => saying == null);
        AddAssert("Not just hidden", () => descendants(popup).OfType<SpriteText>().All(t => t.Text != "EARLY"));
        AddAssert("And out of the way, leaving the word alone to set the size", () => descendants(popup).OfType<SpriteText>().Last().Alpha == 0);
    }

    /// <summary>
    /// It's what BLOOM is for, so a player who hasn't asked for less gets it.
    /// </summary>
    [Test]
    public void TestItsOnUnlessTurnedOff() => AddAssert("On", () => new YuukiInMemoryConfigManager().Get<bool>(YuukiSetting.ShowCriticalTiming).Value);

    /// <summary>
    /// A hold's release is timed against the hold's end, and a Break the skill made a Critical is still as far off as it was.
    /// </summary>
    [Test]
    public void TestOtherCriticals()
    {
        AddStep("A hold let go late", () => show(HitResult.Critical, 50, NoteType.Hold, holdTail: true));
        AddAssert("LATE", () => saying == "LATE");
        AddStep("A Break the skill made a Critical", () => show(HitResult.Critical, -70));
        AddAssert("EARLY", () => saying == "EARLY");
    }
}
