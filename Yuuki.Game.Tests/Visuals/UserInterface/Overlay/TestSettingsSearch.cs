// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Configurations;
using Yuuki.Game.Graphics.Colors;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Overlay;

public partial class TestSettingsSearch : YuukiManualInputManagerTestScene
{
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    private SettingsOverlay overlay = null!;
    private KeyRecorder behind = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
        Cache(config);
    }

    [SetUp]
    public void SetUp()
    {
        AddStep("open settings over a screen", () =>
        {
            // Added first, so it hears keys before the settings do, as the screen stack does in the game.
            TestContent.Add(behind = new KeyRecorder { RelativeSizeAxes = Axes.Both });
            TestContent.Add(overlay = new SettingsOverlay());
            overlay.Show();
        });
        AddUntilStep("the search has the keyboard", () => overlay.SearchFocused);
    }

    [Test]
    public void TestSectionNameShowsWholeSection()
    {
        AddStep("search \"audio\"", () => overlay.SearchText = "audio");
        AddAssert("all of the Audio section", () => new[] { "Master volume", "Inactive volume", "Music volume", "Effects volume", "Hitsound volume", "Audio driver" }
                                                    .All(overlay.ShownSettings.Contains));
        AddAssert("and rows elsewhere that say it", () => overlay.ShownSettings.Contains("Audio offset") && overlay.ShownSettings.Contains("Audio buffer size"));
        AddAssert("but nothing else", () => !overlay.ShownSettings.Contains("Background dim") && !overlay.ShownSections.Contains("Interface"));
    }

    [Test]
    public void TestSectionNameNarrowsTheRest()
    {
        AddStep("search \"gameplay offset\"", () => overlay.SearchText = "gameplay offset");
        AddAssert("just the offsets in Gameplay", () => overlay.ShownSettings.SequenceEqual(new[] { "Audio offset", "Visual offset", "How offsets work", "Calibrate offsets", "Offsets by device" }));
    }

    [Test]
    public void TestKeywordsDescriptionsAndChoicesMatch()
    {
        AddStep("search a keyword: \"fps\"", () => overlay.SearchText = "fps");
        AddAssert("frame limiter and the performance overlay", () =>
            overlay.ShownSettings.SequenceEqual(new[] { "Frame limiter", "Performance overlay" }));

        AddStep("search a dropdown choice: \"vsync\"", () => overlay.SearchText = "vsync");
        AddAssert("the frame limiter", () => overlay.ShownSettings.Contains("Frame limiter"));

        AddStep("search words of an explanation: \"crackling\"", () => overlay.SearchText = "crackling");
        AddAssert("the audio buffer size", () => overlay.ShownSettings.SequenceEqual(new[] { "Audio buffer size" }));

        AddStep("search nonsense", () => overlay.SearchText = "zzzz");
        AddAssert("nothing", () => !overlay.ShownSections.Any() && !overlay.ShownSettings.Any());

        AddStep("clear the search", () => overlay.SearchText = string.Empty);
        AddAssert("everything back", () => overlay.ShownSections.Count() == 6);
    }

    [Test]
    public void TestTypingStaysInTheSearch()
    {
        AddStep("type \"p\" (main menu's play key)", () =>
        {
            InputManager.PressKey(Key.P);
            InputManager.TypeText("p");
        });
        AddAssert("typed into the search", () => overlay.SearchText == "p");
        AddAssert("the screen behind never heard it", () => behind.Keys.Count == 0);
    }

    [Test]
    public void TestEscapeClearsThenCloses()
    {
        AddStep("search \"blur\"", () => overlay.SearchText = "blur");

        AddStep("press Escape", () => InputManager.PressKey(Key.Escape));
        AddAssert("the search is empty", () => overlay.SearchText == string.Empty);
        AddAssert("the settings are still open", () => overlay.State == Visibility.Visible);

        AddStep("press Escape again", () => InputManager.PressKey(Key.Escape));
        AddAssert("the settings closed", () => overlay.State == Visibility.Hidden);
        AddAssert("the search let the keyboard go", () => !overlay.SearchFocused);
        AddAssert("neither Escape reached the screen behind", () => behind.Keys.Count == 0);
    }

    [Test]
    public void TestTypingAnywhereSearches()
    {
        AddStep("click away from the search", () =>
        {
            InputManager.MoveMouseTo(overlay);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("the search let go", () => !overlay.SearchFocused);

        AddStep("type \"d\"", () =>
        {
            InputManager.PressKey(Key.D);
            InputManager.TypeText("d");
        });
        AddAssert("the search took it", () => overlay.SearchFocused && overlay.SearchText == "d");
    }

    [Test]
    public void TestReopenSelectsLastSearch()
    {
        AddStep("search \"cursor\"", () => overlay.SearchText = "cursor");
        AddStep("close", () => overlay.Hide());
        AddStep("open again", () => overlay.Show());
        AddUntilStep("the search has the keyboard", () => overlay.SearchFocused);
        AddAssert("still searching \"cursor\"", () => overlay.SearchText == "cursor");

        AddStep("type \"dim\"", () => InputManager.TypeText("dim"));
        AddAssert("it replaced the old search", () => overlay.SearchText == "dim");
    }

    /// <summary>Stands in for the screen behind the settings: it takes, and notes, every key it's offered.</summary>
    private partial class KeyRecorder : Container
    {
        public readonly List<Key> Keys = new List<Key>();

        public override bool OnKeyDown(KeyEvent e)
        {
            Keys.Add(e.Key);
            return true;
        }
    }
}
