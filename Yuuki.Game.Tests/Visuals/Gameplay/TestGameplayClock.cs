// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Testing;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Verifies the <see cref="GameplayClockContainer"/> advances its subtree on gameplay time and
/// responds to seek / rate / start / stop.
/// </summary>
public partial class TestGameplayClock : TestScene
{
    private GameplayClockContainer clock = null!;

    [Test]
    public void TestRunsSeeksAndScales()
    {
        AddStep("add clock container", () =>
        {
            Clear();
            Add(clock = new GameplayClockContainer
            {
                RelativeSizeAxes = Axes.Both,
                LeadInTime = 0,
                Child = new TimeReadout()
            });
        });

        AddStep("restart", () => clock.Restart());
        AddUntilStep("time advances past 0", () => clock.CurrentTime > 0);

        AddStep("seek to 10000", () => clock.Seek(10000));
        AddAssert("time near 10000", () => clock.CurrentTime >= 9500);

        AddSliderStep("rate", 0.25f, 2f, 1f, v =>
        {
            if (clock != null)
                clock.Rate = v;
        });

        AddStep("stop", () => clock.Stop());
        AddAssert("not running", () => !clock.IsRunning);
    }

    /// <summary>
    /// A restart reads back at once, not a frame later. The gameplay screen updates before this
    /// subtree, so retrying from the results (where the finished run's clock plays on with its song)
    /// read the old time, past the chart's end, and ended the new run on the spot with nothing hit.
    /// </summary>
    [Test]
    public void TestRestartReadsBackAtOnce()
    {
        double timeAfterRestart = double.NaN;

        AddStep("add clock container", () =>
        {
            Clear();
            Add(clock = new GameplayClockContainer
            {
                RelativeSizeAxes = Axes.Both,
                LeadInTime = 2000
            });
        });

        AddStep("a run well past its end", () =>
        {
            clock.Restart();
            clock.Seek(60000);
        });
        AddAssert("there", () => clock.CurrentTime >= 60000);

        AddStep("restart", () =>
        {
            clock.Restart();
            timeAfterRestart = clock.CurrentTime;
        });
        AddAssert("back at the lead-in straight away", () => timeAfterRestart <= -1900);
    }

    private partial class TimeReadout : SpriteText
    {
        public TimeReadout()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 48, FontWeight.Bold);
            Color = Color.White;
        }

        public override void Update()
        {
            base.Update();
            Text = $"{Clock.CurrentTime:0} ms";
        }
    }
}
