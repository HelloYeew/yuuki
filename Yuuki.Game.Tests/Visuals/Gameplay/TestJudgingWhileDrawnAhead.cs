// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Testing;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Notes are judged by the music's time, wherever they're drawn: with the notes drawn far ahead (a large visual offset,
/// a slow screen) a late tap still counts and a missed note is still missed, and with them drawn behind at a fast scroll
/// an early tap counts.
/// </summary>
public partial class TestJudgingWhileDrawnAhead : TestScene
{
    private const double frame_length = 1000 / 60.0;
    private const double note_time = 1000;

    private SteppedHost host = null!;
    private Playfield playfield = null!;
    private readonly List<JudgementResult> judgements = new List<JudgementResult>();

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    /// <summary>
    /// One tap in the first lane at <see cref="note_time"/>, played with the notes drawn <paramref name="drawnAhead"/> ms
    /// along, pressing at <paramref name="pressAt"/> (or never), through to <paramref name="until"/>.
    /// </summary>
    private void play(double drawnAhead, double? pressAt, double until, double preempt = 750)
    {
        AddStep($"play, drawn {drawnAhead} ms along", () =>
        {
            judgements.Clear();
            Clear();

            var chart = new PlayableBeatmap(new[] { new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = note_time, EndTime = note_time } }, laneCount: 4);

            Add(host = new SteppedHost(0)
            {
                Size = PlayfieldAdjustmentContainer.BASE_SIZE,
                Child = playfield = new Playfield(chart)
                {
                    VisualOffset = drawnAhead,
                    DisplayLatency = 0,
                    Preempt = preempt
                }
            });

            playfield.NewJudgement += judgements.Add;

            bool pressed = false;

            host.BeforeFrame = time =>
            {
                if (pressAt is not double at || pressed || time < at)
                    return;

                pressed = true;
                playfield.PressLane(0, time);
                playfield.ReleaseLane(0, time);
            };

            host.EndTime = until;
        });

        AddUntilStep("played through", () => host.Finished, 60_000);
    }

    [Test]
    public void TestALateTapCountsWithTheNotesDrawnFarAhead()
    {
        // 110 ms late (the first frame past it, about 117), a Hit. Drawn 330 ms along (a 300 ms visual offset and a 30 ms
        // screen), the note left the screen 400 ms past its time on what's drawn: 70 ms into the music, before the tap.
        play(330, note_time + 110, note_time + 400);
        AddAssert("judged once", () => judgements.Count == 1);
        AddAssert("as a hit, late", () => judgements[0].Result != HitResult.Miss && judgements[0].TimingError > 100);
    }

    [Test]
    public void TestANoteNobodyTapsIsMissedWithTheNotesDrawnFarAhead()
    {
        play(300, null, note_time + 400);
        AddAssert("missed once", () => judgements.Count == 1 && judgements[0].Result == HitResult.Miss);
    }

    [Test]
    public void TestAnEarlyTapCountsWithTheNotesDrawnBehindAtAFastScroll()
    {
        // 125 ms early (the first frame past it, about 117), a Hit. Drawn 300 ms behind with a 400 ms scroll, the note
        // isn't on screen yet on what's drawn.
        play(-300, note_time - 125, note_time + 400, preempt: 400);
        AddAssert("judged once", () => judgements.Count == 1);
        AddAssert("as a hit, early", () => judgements[0].Result != HitResult.Miss && judgements[0].TimingError < -100);
    }

    private partial class SteppedHost : Container
    {
        private readonly ManualClock time = new ManualClock();

        public Action<double>? BeforeFrame;
        public double EndTime;
        public bool Finished { get; private set; }

        public SteppedHost(double start)
        {
            time.CurrentTime = start - frame_length;
            Clock = new FramedClock(time);
        }

        public override void UpdateSubTree()
        {
            if (!Finished)
                time.CurrentTime += frame_length;

            BeforeFrame?.Invoke(time.CurrentTime);
            base.UpdateSubTree();

            Finished = time.CurrentTime >= EndTime;
        }
    }
}
