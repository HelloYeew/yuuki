// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Input;
using Yuuki.Game.Gameplay;

namespace Yuuki.Game.Tests.Gameplay;

[TestFixture]
public class LaneKeyLayoutsTest
{
    [Test]
    public void Defaults_ForEveryLaneCount()
    {
        for (int lanes = LaneKeyLayouts.MIN_LANES; lanes <= LaneKeyLayouts.MAX_LANES; lanes++)
            Assert.That(LaneKeyLayouts.Default(lanes), Has.Length.EqualTo(lanes));

        Assert.That(LaneKeyLayouts.Default(4), Is.EqualTo(new[] { Key.D, Key.F, Key.J, Key.K }));
        Assert.That(LaneKeyLayouts.Default(5)[2], Is.EqualTo(Key.Space), "odd counts put Space in the middle");
    }

    [Test]
    public void Parse_RoundTripsFormat()
    {
        var keys = new[] { Key.A, Key.S, Key.K, Key.L };

        Assert.That(LaneKeyLayouts.Parse(LaneKeyLayouts.Format(keys), 4), Is.EqualTo(keys));
    }

    [TestCase("")]
    [TestCase("D,F,J")]
    [TestCase("D,F,NotAKey,K")]
    public void Parse_FallsBackToDefault(string saved)
    {
        Assert.That(LaneKeyLayouts.Parse(saved, 4), Is.EqualTo(LaneKeyLayouts.Default(4)));
    }
}
