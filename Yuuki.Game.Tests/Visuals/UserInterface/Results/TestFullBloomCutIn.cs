// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

public partial class TestFullBloomCutIn : YuukiTestScene
{
    private FullBloomCutIn cutIn = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the screen", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(cutIn = new FullBloomCutIn());
        });
    }

    [Test]
    public void TestPlaying()
    {
        AddStep("a Full Bloom", () => cutIn.Play());
        AddWaitStep("let it play out", (int)FullBloomCutIn.DURATION + 200);
        AddAssert("gone again", () => cutIn.Alpha == 0);
        AddStep("again", () => cutIn.Play());
        AddStep("clear", () => cutIn.Clear());
    }

    /// <summary>
    /// Played from a time already past, it shows as it would be by now: on screen halfway through, gone after its end.
    /// </summary>
    [Test]
    public void TestPlayingFromThePast()
    {
        AddStep("from half way through", () => cutIn.Play(cutIn.Clock.CurrentTime - FullBloomCutIn.DURATION / 2));
        AddUntilStep("on screen", () => cutIn.Alpha == 1);
        AddStep("from before its end", () => cutIn.Play(cutIn.Clock.CurrentTime - FullBloomCutIn.DURATION - 10));
        AddUntilStep("gone", () => cutIn.Alpha == 0);
    }
}
