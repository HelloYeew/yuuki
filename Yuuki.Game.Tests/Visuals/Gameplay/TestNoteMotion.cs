// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// What's drawn, frame by frame, against where the projection says it should be.
/// </summary>
public partial class TestNoteMotion : TestScene
{
    private const double frame_length = 1000 / 60.0;

    private SteppedHost host = null!;
    private Playfield playfield = null!;

    // Problems found, by frame; the asserts report the first few.
    private readonly List<string> problems = new List<string>();

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    /// <summary>
    /// Plays <paramref name="chart"/> from <paramref name="from"/> to <paramref name="to"/>, calling
    /// <paramref name="input"/> before each frame (with the frame's time) and <paramref name="check"/>
    /// after it.
    /// </summary>
    private void play(PlayableBeatmap chart, double from, double to, Action<double>? input, Action<double> check)
    {
        AddStep("play", () =>
        {
            problems.Clear();
            Clear();

            Add(host = new SteppedHost(from)
            {
                Size = PlayfieldAdjustmentContainer.BASE_SIZE,
                Child = playfield = new Playfield(chart)
            });

            host.BeforeFrame = time => input?.Invoke(time);
            host.AfterFrame = check;
            host.EndTime = to;
        });

        AddUntilStep("played through", () => host.Finished, 60_000);
    }

    private void noProblems() => AddStep("drawn where it should be", () => Assert.That(problems, Is.Empty, () => string.Join("\n", problems.Take(8))));

    private LanePerspective perspective
    {
        get
        {
            var p = new LanePerspective { LaneCount = 4 };
            p.LayOut(playfield.DrawSize.X, playfield.DrawSize.Y);
            return p;
        }
    }

    private float screenY(Vector2 playfieldPosition) => playfield.ToScreenSpace(playfieldPosition).Y;

    private float lineY => screenY(new Vector2(0, perspective.PlayerY));

    private IEnumerable<NotePieces> notes => descendants<DrawableNote>(playfield).Select(n => new NotePieces(n));

    private static IEnumerable<T> descendants<T>(Container container)
    {
        foreach (Drawable child in container.InternalChildren)
        {
            if (child is T found)
                yield return found;

            if (child is Container nested && child is not T)
            {
                foreach (T inner in descendants<T>(nested))
                    yield return inner;
            }
        }
    }

    private void problem(double time, string message) => problems.Add($"{time:F1} ms: {message}");

    [Test]
    public void TestApproachingNotesDrawnOnTheirSpot()
    {
        // Taps in every lane, close enough that pooled drawables are reused all through.
        var notes = new List<BeatmapNote>();
        for (int i = 0; i < 24; i++)
            notes.Add(new BeatmapNote { Type = NoteType.Tap, Lane = (NoteLane)(i % 4), StartTime = 800 + i * 120, EndTime = 800 + i * 120 });

        int checkedPieces = 0;

        play(new PlayableBeatmap(notes.ToArray(), laneCount: 4), 0, 4000, null, time =>
        {
            LanePerspective p = perspective;

            foreach (NotePieces note in this.notes)
            {
                double z = (note.Note.HitObject.StartTime - time) / playfield.Preempt;
                if (z < 0.05 || note.Bar.DrawAlpha <= 0)
                    continue;

                float expected = screenY(p.Project(z, p.LaneOffset((int)note.Note.HitObject.Lane)));
                float drawn = note.Bar.DrawRectangle.Center.Y;
                checkedPieces++;

                if (Math.Abs(drawn - expected) > 0.5f)
                    problem(time, $"note at {note.Note.HitObject.StartTime} drawn at y {drawn:F1}, projected at {expected:F1}");
            }
        });

        AddAssert("notes were checked", () => checkedPieces > 100);
        noProblems();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestHoldIsDrawnInOnePiece(bool crossing)
    {
        // Lane 2, or lane 2 across to lane 4.
        var chart = new PlayableBeatmap(new[]
        {
            new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane2, EndLane = crossing ? NoteLane.Lane4 : null, StartTime = 1000, EndTime = 2000 }
        }, laneCount: 4);

        int endLane = crossing ? 3 : 1;
        double releasedAt = double.NaN;
        bool checkedRelease = false;

        play(chart, 300, 2300, time =>
        {
            // Hold from the head, drop it, grab the body again, let go on time. By key, one drifting across is
            // held by the one key all the way.
            if (atFrame(time, 1000))
                playfield.PressLane(1, time);

            if (atFrame(time, 1400))
            {
                playfield.ReleaseLane(1, time);
                releasedAt = time;
            }

            if (atFrame(time, 1500))
                playfield.PressLane(1, time);

            if (atFrame(time, 2000))
                playfield.ReleaseLane(1, time);
        }, time =>
        {
            if (!notes.Any())
                return;

            NotePieces note = notes.Single();
            if (note.Note.DrawAlpha <= 0.01f)
                return;

            LanePerspective p = perspective;
            float head = note.Bar.DrawRectangle.Center.Y;
            bool headShown = note.Bar.DrawAlpha > 0;
            bool bodyShown = note.Body.DrawAlpha > 0 && note.Body.DrawRectangle.Height > 0;

            // The body runs from the head to the tail, on the same frame as both.
            if (headShown && bodyShown)
            {
                float bodyNearEnd = note.Body.DrawRectangle.Y + note.Body.DrawRectangle.Height;
                if (Math.Abs(bodyNearEnd - head) > 0.5f)
                    problem(time, $"body ends at y {bodyNearEnd:F1}, head at {head:F1}");
            }

            double tailZ = (2000 - time) / playfield.Preempt;

            if (bodyShown && tailZ > 0.05 && tailZ < 0.95)
            {
                float tail = screenY(p.Project(tailZ, p.LaneOffset(endLane)));
                if (Math.Abs(note.Body.DrawRectangle.Y - tail) > 0.5f)
                    problem(time, $"body reaches y {note.Body.DrawRectangle.Y:F1}, its tail is at {tail:F1}");
            }

            bool held = (time >= 1000 && time < 1400) || (time >= 1500 && time < 2000);
            bool popping = time >= 2000;

            if ((held || popping) && headShown && Math.Abs(head - lineY) > 0.5f)
                problem(time, $"head at y {head:F1} while {(held ? "held" : "popping")}, the line is at {lineY:F1}");

            // Let go, it scrolls on from the line: one frame later it's one frame's travel past it.
            if (time > releasedAt && !checkedRelease && headShown)
            {
                checkedRelease = true;
                float travel = screenY(p.Project(-frame_length / playfield.Preempt, 0)) - lineY;

                if (head < lineY || head - lineY > travel * 1.5f)
                    problem(time, $"head jumped to {head - lineY:F1} px past the line on release (one frame is {travel:F1})");
            }
        });

        AddAssert("the release was seen", () => checkedRelease);
        noProblems();
    }

    [Test]
    public void TestMissedNotesDoNotLingerBelowTheLine()
    {
        var chart = new PlayableBeatmap(new[]
        {
            new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 1000, EndTime = 1000 },
            new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane3, StartTime = 1000, EndTime = 1300 }
        }, laneCount: 4);

        play(chart, 300, 1700, null, time =>
        {
            foreach (NotePieces note in notes)
            {
                // Past the line it fades out within a short stretch: 100 ms on, nothing of it shows below.
                if (time - note.Note.HitObject.StartTime > 100 && note.Bar.DrawAlpha > 0)
                    problem(time, $"the head of the note at {note.Note.HitObject.StartTime} still shows {time - note.Note.HitObject.StartTime:F0} ms after it");

                if (time - note.Note.HitObject.EndTime > 100 && note.Body.DrawAlpha > 0 && note.Body.DrawRectangle.Height > 0)
                    problem(time, $"the body of the hold at {note.Note.HitObject.StartTime} still shows after its end");
            }
        });

        noProblems();
    }

    private static bool atFrame(double time, double at) => time >= at && time < at + frame_length;

    /// <summary>The drawn pieces of a <see cref="DrawableNote"/>: its bar and, for a hold, its body.</summary>
    private readonly struct NotePieces
    {
        public readonly DrawableNote Note;
        public readonly Drawable Bar;
        public readonly HoldNoteBody Body;

        public NotePieces(DrawableNote note)
        {
            Note = note;
            Body = note.InternalChildren.OfType<HoldNoteBody>().Single();
            Bar = note.InternalChildren.OfType<Container>().Single().InternalChildren.OfType<Circle>().First();
        }
    }

    /// <summary>
    /// Runs its subtree on a manual clock that steps one 60 fps frame per update, with hooks either side
    /// of the frame: input goes in before (as the OS delivers it), checks run once everything has updated.
    /// </summary>
    private partial class SteppedHost : Container
    {
        private readonly ManualClock time = new ManualClock();

        public Action<double>? BeforeFrame;
        public Action<double>? AfterFrame;
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

            if (Finished)
                return;

            AfterFrame?.Invoke(time.CurrentTime);
            Finished = time.CurrentTime >= EndTime;
        }
    }
}
