// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.UserInterface.Results;

namespace Yuuki.Game.Tests.Graphics;

/// <summary>
/// Where the timing graph draws a judgement: in the band of the judgement it got. A hold's release has windows 1.5 times
/// as wide as a press's, so drawn at its own timing a Critical release could sit in Break's band.
/// </summary>
[TestFixture]
public class TimingScatterTest
{
    private static readonly HitWindows windows = new HitWindows();

    private static PlayPoint release(double error) => new PlayPoint(1000, error, HitResult.Critical, 0, NoteType.Hold, true);

    private static PlayPoint press(double error) => new PlayPoint(1000, error, HitResult.Critical, 0, NoteType.Tap, false);

    /// <summary>
    /// The one the graph showed wrongly: a Critical release 60 ms early, drawn at 40, in Critical's band (22–45).
    /// </summary>
    [Test]
    public void AReleaseSitsInItsJudgementsBand()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(windows.ResultForRelease(-60), Is.EqualTo(HitResult.Critical));
            Assert.That(windows.ResultFor(-60), Is.EqualTo(HitResult.Break), "where it was drawn before");
            Assert.That(TimingScatter.PlottedError(release(-60), -60, windows), Is.EqualTo(-40).Within(1e-9));
        }
    }

    /// <summary>
    /// Every release timing out to where a release can still grade the tail, either way: the band it's drawn in is the
    /// judgement it got. A press is drawn where it was.
    /// </summary>
    [Test]
    public void EveryReleaseAndPressWhereItsJudgementIs()
    {
        using (Assert.EnterMultipleScope())
        {
            for (double error = -windows.ReleaseWindow; error <= windows.ReleaseWindow; error += 0.5)
            {
                Assert.That(windows.ResultFor(TimingScatter.PlottedError(release(error), error, windows)), Is.EqualTo(windows.ResultForRelease(error)), $"release at {error}");
                Assert.That(TimingScatter.PlottedError(press(error), error, windows), Is.EqualTo(error), $"press at {error}");
            }
        }
    }
}
