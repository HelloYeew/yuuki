// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

/// <summary>
/// A Full Bloom's rings round the ring: drawn by a shader, so only seen in the test browser, not headless.
/// </summary>
public partial class TestFullBloomRings : YuukiTestScene
{
    private Container stage = null!;
    private FullBloomRings rings = null!;
    private BlossomRing ring = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("show the ring", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(stage = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(440),
                Children = new Drawable[]
                {
                    ring = new BlossomRing { Anchor = Anchor.Centre, Origin = Anchor.Centre, Scale = new Vector2(440 / BlossomRing.RING_SIZE), Fill = 1 },
                    rings = new FullBloomRings { RelativeSizeAxes = Axes.Both }
                }
            });
        });
    }

    [Test]
    public void TestABloom()
    {
        AddSliderStep("zoom", 0.25f, 2f, 1f, scale => stage.Scale = new Vector2(scale));
        AddStep("gather, then bloom", () =>
        {
            double now = rings.Clock.CurrentTime;
            rings.Gather(now, 560);
            rings.Bloom(now + 640);
            ring.BloomWave(now + 640);
        });
        AddStep("bloom straight away", () => rings.Bloom());
        AddStep("clear", () => rings.Clear());
    }
}
