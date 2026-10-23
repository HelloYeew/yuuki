// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The offset guide: what Sakura measures by itself, a beat on its way to the player without offsets and with both, and
/// what the player's own say.
/// </summary>
public partial class TestOffsetGuideScreen : TestYuukiScreenStack
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private OffsetGuideScreen guide = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
    }

    [SetUp]
    public void SetUpGuide()
    {
        AddStep("no offsets", () =>
        {
            config.Get<int>(YuukiSetting.AudioOffset).Value = 0;
            config.Get<int>(YuukiSetting.VisualOffset).Value = 0;
        });
        AddStep("nothing else open", () =>
        {
            while (screenStack.CurrentScreen != null)
                screenStack.CurrentScreen.Exit();
        });
        AddStep("open the guide", () => screenStack.Push(guide = new OffsetGuideScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("open", () => guide.IsLoaded && screenStack.CurrentScreen == guide);
    }

    /// <summary>
    /// Without offsets the sound and the picture reach the player apart and a tap on what's heard is judged late. The
    /// audio offset alone judges the tap on the beat but moves the notes with it, to be seen after the beat's heard by
    /// the tap's delay and the picture's. With both, the notes are seen as the beat is heard too.
    /// </summary>
    [TestCase(DelaySetup.Speakers)]
    [TestCase(DelaySetup.Bluetooth)]
    [TestCase(DelaySetup.Tv)]
    public void TestTheOffsetsBringItAllOntoTheBeat(DelaySetup setup)
    {
        showDiagram();
        AddStep($"{setup}, without offsets", () =>
        {
            guide.Diagram.ShowSetup(setup);
            guide.Diagram.ShowPhase(DelayPhase.WithoutOffsets);
        });
        AddAssert("apart, and judged late", () => guide.Diagram.PictureSeen != guide.Diagram.SoundHeard && guide.Diagram.TapJudged == guide.Diagram.TapArrives);

        AddStep("the audio offset alone", () => guide.Diagram.ShowPhase(DelayPhase.AudioOnly));
        AddAssert("judged on the beat, the notes seen late", () =>
        {
            var (_, picture, tap) = guide.Diagram.Delays;
            return guide.Diagram.TapJudged == 0 && guide.Diagram.PictureSeen - guide.Diagram.SoundHeard == tap + picture;
        });

        AddStep("with both", () => guide.Diagram.ShowPhase(DelayPhase.WithOffsets));
        AddAssert("together, and judged on the beat", () => guide.Diagram.PictureSeen == guide.Diagram.SoundHeard && guide.Diagram.TapJudged == 0);
    }

    /// <summary>
    /// However late the tap gets to the game, it's back on the beat before the next step, Bluetooth's included.
    /// </summary>
    [TestCase(DelaySetup.Speakers)]
    [TestCase(DelaySetup.Bluetooth)]
    [TestCase(DelaySetup.Tv)]
    public void TestTheTapSettlesOnTheBeat(DelaySetup setup)
    {
        showDiagram();
        AddStep($"{setup}, both", () =>
        {
            guide.Diagram.ShowSetup(setup);
            guide.Diagram.ShowPhase(DelayPhase.WithOffsets);
        });
        AddUntilStep("on the beat", () => guide.Diagram.TapShownAt == 0, 20_000);
    }

    [Test]
    public void TestItGoesFromOneToTheOtherByItself()
    {
        showDiagram();
        AddAssert("without offsets first", () => guide.Diagram.Phase == DelayPhase.WithoutOffsets);
        AddUntilStep("then the audio offset alone", () => guide.Diagram.Phase == DelayPhase.AudioOnly, 20_000);
        AddUntilStep("then both", () => guide.Diagram.Phase == DelayPhase.WithOffsets, 20_000);
        AddUntilStep("and round again", () => guide.Diagram.Phase == DelayPhase.WithoutOffsets, 20_000);

        AddStep("pick without", () => guide.Diagram.ShowPhase(DelayPhase.WithoutOffsets));
        AddWaitStep("longer than a phase", 6000);
        AddAssert("stays", () => guide.Diagram.Phase == DelayPhase.WithoutOffsets);
    }

    /// <summary>
    /// The audio offset makes up for the sound's delay and the tap's, the visual offset for the picture's and the
    /// tap's, so together they say how far apart the sound and the picture reach the player. Shown as they change.
    /// </summary>
    [Test]
    public void TestWhatYoursSay()
    {
        AddAssert("not calibrated", () => guide.YoursText.StartsWith("Not calibrated"));

        offsets(-37, 17);
        AddAssert("sound 20 ms after the picture", () => guide.YoursText.Contains("sound reaches you about 20 ms after the picture"));

        offsets(-10, 60);
        AddAssert("picture 50 ms after the sound", () => guide.YoursText.Contains("picture reaches you about 50 ms after the sound"));

        offsets(-30, 28);
        AddAssert("together", () => guide.YoursText.Contains("together"));
    }

    /// <summary>
    /// Titled, the title sliding in at its own height from the start, not along the top.
    /// </summary>
    [Test]
    public void TestItsTitled()
    {
        // Where it's drawn, not its Y: the slide sets the position for drawing, whatever Y says after.
        AddAssert("drawn at its height while it slides in", () =>
        {
            ScreenTitleTab tab = descendants(guide).OfType<ScreenTitleTab>().Single();
            return tab.X < -41 && Math.Abs(tab.ScreenSpaceDrawQuad.TopLeft.Y - tab.Parent!.ToScreenSpace(new Vector2(0, 48)).Y) < 0.5f;
        });
        AddUntilStep("titled", () =>
            descendants(guide).OfType<ScreenTitleTab>().Single() is ScreenTitleTab tab && tab.Alpha > 0
                                                                    && descendants(tab).OfType<SpriteText>().Any(t => t.Text == "Offsets"));
    }

    /// <summary>
    /// On a desktop, where there's a window mode to choose.
    /// </summary>
    [Test]
    public void TestFullscreenIsRecommended() => AddAssert("a tip for it", () => descendants(guide).OfType<SpriteText>().Any(t => t.Text == "Play in Fullscreen"));

    /// <summary>
    /// What Sakura measures of each delay, and what it leaves the offsets: all of it, where nothing's measured. What's
    /// measured is added up with the offsets (judged later by the sound's, drawn ahead by the picture's), and counts in
    /// what they say together; everything's shown as it changes.
    /// </summary>
    [Test]
    public void TestWhatSakuraMeasures()
    {
        AddStep("nothing measured", () =>
        {
            guide.SoundDelay = double.NaN;
            guide.PictureDelay = double.NaN;
        });
        AddUntilStep("said so", () => guide.SoundMeasuredText == "not measured here" && guide.PictureMeasuredText == "not measured here");
        AddAssert("a sentence starts with a capital", () => guide.PictureNote.StartsWith("This renderer can't tell"));
        AddAssert("the offsets cover it all", () =>
            guide.SoundNote.Contains("your audio offset covers all of the sound's delay") && guide.PictureNote.Contains("your visual offset covers all of the picture's delay"));

        offsets(-37, 17);
        AddStep("20 ms of the sound and 40 of the picture measured", () =>
        {
            guide.SoundDelay = 20;
            guide.PictureDelay = 40;
        });
        AddUntilStep("shown", () => guide.SoundMeasuredText == "20 ms now" && guide.PictureMeasuredText == "40 ms now");
        AddAssert("what it does with them", () => guide.SoundNote.Contains("taps are judged against the music as it reaches you")
                                                    && guide.PictureNote.Contains("notes are drawn that far ahead"));
        // 20 + 37 and 40 + 17.
        AddUntilStep("added up with the offsets", () => guide.StackSummary.Contains("taps judged 57 ms later · notes drawn 57 ms ahead"));
        // 37 - 17 + 20 - 40.
        AddAssert("together, with them", () => guide.YoursText.Contains("together"));
    }

    [Test]
    public void TestCalibratingComesBackHere()
    {
        AddStep("calibrate", () => guide.Calibrate());
        AddUntilStep("calibrating", () => screenStack.CurrentScreen is CalibrationScreen);
        AddStep("back", () => screenStack.CurrentScreen.Exit());
        AddUntilStep("the guide again", () => screenStack.CurrentScreen == guide);
    }

    // Down the page, where it's kept going: scrolled away, it's masked, and nothing masked away is updated.
    private void showDiagram() => AddStep("scroll to the diagram", () =>
        descendants(guide).OfType<ScrollableContainer>().First().ScrollIntoView(guide.Diagram, false));

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

    private void offsets(int audio, int visual) => AddStep($"offsets {audio} and {visual}", () =>
    {
        config.Get<int>(YuukiSetting.AudioOffset).Value = audio;
        config.Get<int>(YuukiSetting.VisualOffset).Value = visual;
    });
}
