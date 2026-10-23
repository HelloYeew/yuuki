// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Maths;
using Yuuki.Game.Gameplay;

namespace Yuuki.Game.Tests.Gameplay;

/// <summary>
/// Which lane a finger is in: <see cref="LanePerspective.LaneAt"/>.
/// </summary>
[TestFixture]
public class LanePerspectiveTest
{
    private static LanePerspective fourLanes()
    {
        var perspective = new LanePerspective { LaneCount = 4 };
        perspective.LayOut(1000, 1000);
        return perspective;
    }

    [Test]
    public void EachLaneAtTheLine()
    {
        var p = fourLanes();

        for (int lane = 0; lane < 4; lane++)
            Assert.That(p.LaneAt(p.Project(0, p.LaneOffset(lane))), Is.EqualTo(lane));
    }

    /// <summary>
    /// Up the highway the lanes narrow toward the horizon, and a finger is in the lane drawn under it.
    /// </summary>
    [Test]
    public void AlongTheSlantFurtherUp()
    {
        var p = fourLanes();

        for (int lane = 0; lane < 4; lane++)
            Assert.That(p.LaneAt(p.Project(0.6, p.LaneOffset(lane))), Is.EqualTo(lane));
    }

    /// <summary>
    /// Below the line, where a thumb rests, the lanes run straight down.
    /// </summary>
    [Test]
    public void StraightDownBelowTheLine()
    {
        var p = fourLanes();
        Vector2 atLine = p.Project(0, p.LaneOffset(3));

        Assert.That(p.LaneAt(atLine + new Vector2(0, 80)), Is.EqualTo(3));
    }

    /// <summary>
    /// By touch the line sits higher, clear of the strip along a phone's bottom edge.
    /// </summary>
    [Test]
    public void TheLineSitsHigherByTouch()
    {
        var keys = new LanePerspective();
        var touch = new LanePerspective();
        keys.LayOut(1000, 1000);
        touch.LayOut(1000, 1000, touch: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys.PlayerY, Is.EqualTo(900).Within(0.01f));
            Assert.That(touch.PlayerY, Is.EqualTo(850).Within(0.01f));
            Assert.That(touch.HorizonY, Is.EqualTo(keys.HorizonY));
        }
    }

    [Test]
    public void JustOutsideIsTheLaneBesideAndFurtherIsNone()
    {
        var p = fourLanes();
        float laneWidth = 2 * p.BottomHalfWidth / 4;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(p.LaneAt(p.Project(0, p.EdgeOffset(0) - laneWidth * 0.3f)), Is.EqualTo(0));
            Assert.That(p.LaneAt(p.Project(0, p.EdgeOffset(4) + laneWidth * 0.3f)), Is.EqualTo(3));
            Assert.That(p.LaneAt(p.Project(0, p.EdgeOffset(0) - laneWidth * 0.7f)), Is.EqualTo(-1));
        }
    }
}
