// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Yuuki.Game.Configurations;
using Yuuki.Game.Database;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.Screens;

/// <summary>
/// The offsets by device: what's played on now with its two offsets, then a line for each device and song offset kept,
/// whose choices open under it on asking.
/// </summary>
public partial class TestOffsetDevicesScreen : TestYuukiScreenStack
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    // One for the fixture, cached for the page to resolve, and added again after each test clears the scene.
    private readonly DevicesOffsetManager offsets = new DevicesOffsetManager(new OffsetStore(null)) { DisposeOnRemoval = false };

    private OffsetDevicesScreen page = null!;

    private Reactive<int> audio => config.Get<int>(YuukiSetting.AudioOffset);

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
        Cache<OffsetManager>(offsets);
    }

    /// <summary>
    /// A player who's played on speakers and AirPods, the laptop's screen and a TV, and given two difficulties an offset;
    /// on the speakers and the laptop now.
    /// </summary>
    [SetUp]
    public void SetUpPage()
    {
        AddStep("nothing else open", () =>
        {
            while (screenStack.CurrentScreen != null)
                screenStack.CurrentScreen.Exit();
        });
        AddStep("the offsets", () =>
        {
            if (offsets.Parent == null)
                Add(offsets);
        });
        AddUntilStep("loaded", () => offsets.IsLoaded);

        AddStep("start over", () =>
        {
            offsets.PerDevice = true;
            use("Speakers", "Built-in");
        });
        AddUntilStep("on the speakers", () => offsets.AudioDevice == "Speakers" && offsets.Display == "Built-in");
        AddStep("forget the rest", () =>
        {
            foreach (string device in offsets.AudioDevices.Keys.Where(d => d != "Speakers").ToList())
                offsets.ForgetAudioDevice(device);

            foreach (string display in offsets.Displays.Keys.Where(d => d != "Built-in").ToList())
                offsets.ForgetDisplay(display);

            foreach (int id in offsets.SongOffsets.Keys.ToList())
                offsets.SetSongOffset(id, 0);

            offsets.SetStartAudioDevice("Speakers");
            offsets.SetStartDisplay("Built-in");
            audio.Value = -10;
        });

        AddStep("play on the AirPods and the TV", () => use("AirPods", "TV"));
        AddUntilStep("on them", () => offsets.AudioDevice == "AirPods" && offsets.Display == "TV");
        AddStep("calibrated there", () => audio.Value = -150);
        AddStep("back on the speakers", () => use("Speakers", "Built-in"));
        AddUntilStep("on the speakers again", () => offsets.AudioDevice == "Speakers" && offsets.Display == "Built-in");
        AddStep("two song offsets", () =>
        {
            offsets.SetSongOffset(5, 10);
            offsets.SetSongOffset(6, -5);
        });

        AddStep("open the page", () => screenStack.Push(page = new OffsetDevicesScreen
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            SoundDelay = 20,
            PictureDelay = 10
        }));
        AddUntilStep("open", () => page.IsLoaded && screenStack.CurrentScreen == page);
    }

    /// <summary>
    /// Every device and song is a line, and until one's opened, all there is to press is what's played on now and the
    /// page's own few: nothing about any one device is on show.
    /// </summary>
    [Test]
    public void TestALineEachAndFewButtons()
    {
        AddAssert("a line each", () => new[] { "audio:Speakers", "audio:AirPods", "display:Built-in", "display:TV", "song:5", "song:6" }
            .All(page.Rows.ContainsKey));
        AddAssert("only what's played on now's, and the page's", () => pills(page).Select(p => p.Text).Distinct().OrderBy(t => t)
            .SequenceEqual(new[] { "+", "Calibrate", "Edit", "I hit early", "I hit late", "−" }.OrderBy(t => t)));
        AddAssert("Sakura's share said", () => notes(page).Any(t => t.Contains("Sakura already takes off the 20 ms")));
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
            ScreenTitleTab tab = descendants(page).OfType<ScreenTitleTab>().Single();
            return tab.X < -41 && Math.Abs(tab.ScreenSpaceDrawQuad.TopLeft.Y - tab.Parent!.ToScreenSpace(new Vector2(0, 48)).Y) < 0.5f;
        });
        AddUntilStep("titled", () =>
            descendants(page).OfType<ScreenTitleTab>().Single() is ScreenTitleTab tab && tab.Alpha > 0 && texts(tab).Contains("Offsets by device"));
    }

    /// <summary>
    /// The game speaks English: dates are Gregorian with English month names even on a phone set to the Thai
    /// calendar, which would say 2569.
    /// </summary>
    [Test]
    public void TestDatesAreGregorianOnAThaiPhone()
    {
        CultureInfo? before = null;

        AddStep("a Thai phone", () =>
        {
            before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
        });
        AddStep("edit the AirPods, which shows them again", () => pill("audio:AirPods", "Edit").Action());
        AddUntilStep("this year, in English", () => notes(page.Rows["audio:AirPods"]).Any(t =>
            t.Contains($"Last used {DateTime.Now.Day} {DateTime.Now.ToString("MMM", CultureInfo.InvariantCulture)} {DateTime.Now.Year}")));
        AddStep("back as it was", () => CultureInfo.CurrentCulture = before!);
    }

    /// <summary>
    /// A device not in use opens to its offset and everything else that can be done with it, each with a sentence, and
    /// is gone when forgotten.
    /// </summary>
    [Test]
    public void TestOpeningOneAndForgettingIt()
    {
        AddStep("edit the AirPods", () => pill("audio:AirPods", "Edit").Action());
        AddUntilStep("open", () => pillOrNull("audio:AirPods", "Done") != null);
        AddAssert("its own offset to step", () => descendants(page.Rows["audio:AirPods"]).OfType<OffsetStepper>().Single().Current.Value == -150);
        AddAssert("and the rest", () => new[] { "It's the same as…", "Start new ones from it", "Forget" }.All(t => pillOrNull("audio:AirPods", t) != null));

        AddStep("forget them", () => pill("audio:AirPods", "Forget").Action());
        AddUntilStep("gone", () => !page.Rows.ContainsKey("audio:AirPods") && !offsets.AudioDevices.ContainsKey("AirPods"));
    }

    /// <summary>
    /// The one in use has its offset up top already, and can't be forgotten from under the player.
    /// </summary>
    [Test]
    public void TestTheOneInUse()
    {
        AddStep("edit the speakers", () => pill("audio:Speakers", "Edit").Action());
        AddUntilStep("open", () => pillOrNull("audio:Speakers", "Done") != null);
        AddAssert("no second stepper", () => !descendants(page.Rows["audio:Speakers"]).OfType<OffsetStepper>().Any());
        AddAssert("not forgettable", () => pillOrNull("audio:Speakers", "Forget") == null);
        AddAssert("already where new ones start", () => pillOrNull("audio:Speakers", "Start new ones from it") == null);
    }

    [Test]
    public void TestSayingItsTheSameDevice()
    {
        AddStep("edit the AirPods", () => pill("audio:AirPods", "Edit").Action());
        AddUntilStep("open", () => pillOrNull("audio:AirPods", "It's the same as…") != null);
        AddStep("the same as…", () => pill("audio:AirPods", "It's the same as…").Action());
        AddUntilStep("the speakers offered", () => pillOrNull("audio:AirPods", "Speakers") != null);
        AddStep("the speakers", () => pill("audio:AirPods", "Speakers").Action());

        AddUntilStep("goes by theirs", () => offsets.AudioDevices["AirPods"].SameAs == "Speakers"
                                             && pillOrNull("audio:AirPods", "Give it its own offset") != null);
        AddAssert("shows theirs", () => texts(page.Rows["audio:AirPods"]).Contains("-10 ms"));
        AddAssert("says so on its line", () => notes(page.Rows["audio:AirPods"]).Any(t => t.Contains("goes by Speakers' offset")));

        AddStep("its own again", () => pill("audio:AirPods", "Give it its own offset").Action());
        AddUntilStep("its own", () => offsets.AudioDevices["AirPods"].SameAs == null);
    }

    /// <summary>
    /// Stepping an offset changes what shows it, and nothing's built again: the line the player's at stays.
    /// </summary>
    [Test]
    public void TestSteppingKeepsThePage()
    {
        Drawable? line = null;

        AddStep("note the speakers' line", () => line = page.Rows["audio:Speakers"]);
        AddStep("step the audio offset up", () => pills(page).First(p => p.Text == "+").Action());
        AddAssert("the speakers' offset", () => audio.Value == -5 && offsets.AudioDevices["Speakers"].Offset == -5);
        AddUntilStep("shown on its line", () => texts(page.Rows["audio:Speakers"]).Contains("-5 ms"));
        AddAssert("the same line", () => page.Rows["audio:Speakers"] == line);
    }

    /// <summary>
    /// A song stepped to no offset stays where it is until it's removed, rather than going from under the finger.
    /// </summary>
    [Test]
    public void TestASongSteppedToNothingStays()
    {
        AddStep("edit the song", () => pill("song:5", "Edit").Action());
        AddUntilStep("open", () => pillOrNull("song:5", "Done") != null);
        AddStep("step it to 0", () =>
        {
            var stepper = descendants(page.Rows["song:5"]).OfType<OffsetStepper>().Single();
            pills(stepper).First(p => p.Text == "−").Action();
            pills(stepper).First(p => p.Text == "−").Action();
        });
        AddAssert("no offset", () => offsets.GetSongOffset(5) == 0 && !offsets.SongOffsets.ContainsKey(5));
        AddWaitStep("a few frames", 3);
        AddAssert("still listed", () => page.Rows.ContainsKey("song:5") && pillOrNull("song:5", "Done") != null);

        AddStep("remove it", () => pill("song:5", "Remove").Action());
        AddUntilStep("gone", () => !page.Rows.ContainsKey("song:5"));
        AddAssert("the other's still there", () => page.Rows.ContainsKey("song:6"));
    }

    /// <summary>
    /// Late on every device: the judging moves later on all of them, each audio offset down.
    /// </summary>
    [Test]
    public void TestHittingLateEverywhere()
    {
        AddStep("I hit late", () => pills(page).First(p => p.Text == "I hit late").Action());
        AddAssert("every device's moved", () => offsets.AudioDevices["Speakers"].Offset == -15 && offsets.AudioDevices["AirPods"].Offset == -155);
        AddUntilStep("shown", () => texts(page.Rows["audio:AirPods"]).Contains("-155 ms"));
    }

    [Test]
    public void TestOneForEveryDevice()
    {
        AddStep("one for all", () => offsets.PerDevice = false);
        AddUntilStep("no devices listed", () => !page.Rows.Keys.Any(k => k.StartsWith("audio:") || k.StartsWith("display:")));
        AddAssert("the songs still are", () => page.Rows.ContainsKey("song:5"));
        AddAssert("for every device", () => notes(page).Contains("Every audio device") && texts(page).Contains("Playing with"));
        AddStep("one each again", () => offsets.PerDevice = true);
        AddUntilStep("listed again", () => page.Rows.ContainsKey("audio:AirPods"));
    }

    private void use(string audioDevice, string display)
    {
        offsets.AudioNow = audioDevice;
        offsets.DisplayNow = display;
    }

    private PillButton pill(string row, string text) => pillOrNull(row, text)!;

    private PillButton? pillOrNull(string row, string text) =>
        page.Rows.TryGetValue(row, out Drawable? line) ? pills(line).FirstOrDefault(p => p.Text == text) : null;

    private static IEnumerable<PillButton> pills(Drawable drawable) => descendants(drawable).OfType<PillButton>();

    // Every word on show, a sprite at a time.
    private static List<string> texts(Drawable drawable) => descendants(drawable).OfType<SpriteText>().Select(t => t.Text).ToList();

    // What the explanations say, each a flow of words, each word with the space after it.
    private static IEnumerable<string> notes(Drawable drawable) =>
        descendants(drawable).OfType<TextFlowContainer>().Select(f => string.Concat(f.Children.OfType<SpriteText>().Select(t => t.Text)));

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

    /// <summary>
    /// Plays on the devices a test says, rather than the ones the machine has.
    /// </summary>
    private partial class DevicesOffsetManager : OffsetManager
    {
        public string AudioNow = "Speakers";
        public string DisplayNow = "Built-in";

        public DevicesOffsetManager(OffsetStore store)
            : base(store)
        {
        }

        protected override string CurrentAudioDevice => AudioNow;

        protected override string CurrentDisplay => DisplayNow;
    }
}
