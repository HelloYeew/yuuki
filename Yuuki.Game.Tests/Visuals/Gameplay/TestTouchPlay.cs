// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Replays;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Playing by touch: a finger on the lanes presses the lane it's in, and a flick is a swipe.
/// </summary>
public partial class TestTouchPlay : YuukiManualInputManagerTestScene
{
    [Cached]
    private readonly GameLayout gameLayout = new GameLayout { Touch = { Value = true } };

    private readonly ManualClock time = new ManualClock();
    private readonly List<JudgementResult> judgements = new List<JudgementResult>();
    private readonly List<ReplayInput> inputs = new List<ReplayInput>();
    private int breaks;
    private Playfield playfield = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    [Test]
    public void TestATapHitsTheLaneUnderTheFinger()
    {
        play(new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane2, StartTime = 1000, EndTime = 1000 });

        at(1000);
        AddStep("tap the second lane", () => InputManager.Tap(lane(1)));
        AddAssert("hit", () => judgements.Count == 1 && judgements[0].Result.IsHit());
    }

    /// <summary>
    /// A lane is held while any finger is in it: the first of two lifting doesn't let a hold go.
    /// </summary>
    [Test]
    public void TestALaneIsHeldTillItsLastFingerLifts()
    {
        play(new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane1, StartTime = 1000, EndTime = 2000 });

        at(1000);
        AddStep("two fingers in the first lane", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, lane(0));
            InputManager.TouchDown(TouchSource.Touch2, lane(0, 40));
        });
        at(1500);
        AddStep("lift one", () => InputManager.TouchUp(TouchSource.Touch1));
        at(2000);
        AddStep("lift the other", () => InputManager.TouchUp(TouchSource.Touch2));
        // A hold let go early is judged later on (it can be grabbed again); one let go on time is judged then.
        AddAssert("head and tail, both on time", () => judgements.Count == 2 && judgements.All(j => j.Result == HitResult.Critical));
    }

    /// <summary>
    /// A hold that drifts across the lanes is followed by sliding the finger along with it: crossing into each
    /// lane it covers carries it on, and letting go in its end lane judges the tail.
    /// </summary>
    [Test]
    public void TestSlidingAlongADriftingHold()
    {
        play(new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane1, EndLane = NoteLane.Lane3, StartTime = 1000, EndTime = 2000 });

        at(1000);
        AddStep("touch the first lane", () => InputManager.TouchDown(TouchSource.Touch1, lane(0)));
        at(1400);
        AddStep("slide into the second", () => InputManager.MoveTouchTo(TouchSource.Touch1, lane(1)));
        at(1800);
        AddStep("and the third", () => InputManager.MoveTouchTo(TouchSource.Touch1, lane(2)));
        at(2000);
        AddStep("lift", () => InputManager.TouchUp(TouchSource.Touch1));
        AddAssert("never broken", () => breaks == 0);
        AddAssert("head and tail, both on time", () => judgements.Count == 2 && judgements.All(j => j.Result == HitResult.Critical));
    }

    /// <summary>
    /// A drifting hold has to be followed: a finger let go of it where it started, not where it ended up, at
    /// the end misses the tail.
    /// </summary>
    [Test]
    public void TestADriftingHoldNotFollowedMisses()
    {
        play(new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane1, EndLane = NoteLane.Lane3, StartTime = 1000, EndTime = 2000 });

        at(1000);
        AddStep("touch the first lane", () => InputManager.TouchDown(TouchSource.Touch1, lane(0)));
        at(2000);
        AddStep("lift, still there", () => InputManager.TouchUp(TouchSource.Touch1));
        AddAssert("the tail misses", () => judgements.Select(j => j.Result).SequenceEqual(new[] { HitResult.Critical, HitResult.Miss }));
    }

    /// <summary>
    /// Two holds crossing in an X, a finger each: touch both starts, slide the fingers in past each other through
    /// the middle and out to the ends. Each hold is its finger's, though both run through the same lanes at once.
    /// </summary>
    [Test]
    public void TestAnXIsPlayedAFingerEach()
    {
        play(new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane1, EndLane = NoteLane.Lane4, StartTime = 1000, EndTime = 2000 },
            new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane4, EndLane = NoteLane.Lane1, StartTime = 1000, EndTime = 2000 });

        at(1000);
        AddStep("touch both starts", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, lane(0));
            InputManager.TouchDown(TouchSource.Touch2, lane(3));
        });
        at(1300);
        AddStep("in towards the middle", () =>
        {
            InputManager.MoveTouchTo(TouchSource.Touch1, lane(1));
            InputManager.MoveTouchTo(TouchSource.Touch2, lane(2));
        });
        at(1500);
        AddStep("past each other", () =>
        {
            InputManager.MoveTouchTo(TouchSource.Touch1, lane(2));
            InputManager.MoveTouchTo(TouchSource.Touch2, lane(1));
        });
        at(1800);
        AddStep("out to the ends", () =>
        {
            InputManager.MoveTouchTo(TouchSource.Touch1, lane(3));
            InputManager.MoveTouchTo(TouchSource.Touch2, lane(0));
        });
        at(2000);
        AddStep("lift both", () =>
        {
            InputManager.TouchUp(TouchSource.Touch1);
            InputManager.TouchUp(TouchSource.Touch2);
        });
        AddAssert("never broken", () => breaks == 0);
        AddAssert("both heads and tails on time", () => judgements.Count == 4 && judgements.All(j => j.Result == HitResult.Critical));
    }

    /// <summary>
    /// A thumb drifts: a finger holding a hold keeps it, though it wanders out of the hold's lane.
    /// </summary>
    [Test]
    public void TestAFingerDriftingOffAHoldKeepsIt()
    {
        play(new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane2, StartTime = 1000, EndTime = 2000 });

        at(1000);
        AddStep("touch the second lane", () => InputManager.TouchDown(TouchSource.Touch1, lane(1)));
        at(1500);
        AddStep("drift into the third", () => InputManager.MoveTouchTo(TouchSource.Touch1, lane(2)));
        at(2000);
        AddStep("lift", () => InputManager.TouchUp(TouchSource.Touch1));
        AddAssert("never broken", () => breaks == 0);
        AddAssert("head and tail, both on time", () => judgements.Count == 2 && judgements.All(j => j.Result == HitResult.Critical));
    }

    /// <summary>
    /// Only a fresh touch hits a note: a finger slid into a lane doesn't, or one finger swept across the lanes
    /// would hit everything in them.
    /// </summary>
    [Test]
    public void TestSlidingIntoALaneHitsNothing()
    {
        play(new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane2, StartTime = 1000, EndTime = 1000 });

        at(980);
        AddStep("touch the first lane", () => InputManager.TouchDown(TouchSource.Touch1, lane(0)));
        at(1000);
        AddStep("slide into the second", () => InputManager.MoveTouchTo(TouchSource.Touch1, lane(1)));
        AddAssert("not hit", () => judgements.Count == 0);
        AddStep("lift", () => InputManager.TouchUp(TouchSource.Touch1));
    }

    /// <summary>
    /// What a slide does catch: a Touch note, which only wants its lane held.
    /// </summary>
    [Test]
    public void TestSlidingIntoALaneCatchesATouchNote()
    {
        play(new BeatmapNote { Type = NoteType.Touch, Lane = NoteLane.Lane2, StartTime = 1000, EndTime = 1000 });

        at(950);
        AddStep("touch the first lane", () => InputManager.TouchDown(TouchSource.Touch1, lane(0)));
        at(980);
        AddStep("slide into the second", () => InputManager.MoveTouchTo(TouchSource.Touch1, lane(1)));
        at(1000);
        AddAssert("caught", () => judgements.Count == 1 && judgements[0].Result.IsHit());
        AddStep("lift", () => InputManager.TouchUp(TouchSource.Touch1));
    }

    /// <summary>
    /// Every finger touching down is a press of its own, a second one in a lane another's still on too: a jack
    /// played with two fingers.
    /// </summary>
    [Test]
    public void TestASecondFingerInAHeldLaneHits()
    {
        play(new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 1000, EndTime = 1000 },
            new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 1150, EndTime = 1150 });

        at(1000);
        AddStep("one finger", () => InputManager.TouchDown(TouchSource.Touch1, lane(0)));
        at(1150);
        AddStep("another, the first still down", () => InputManager.Tap(lane(0, 40), TouchSource.Touch2));
        AddAssert("both hit", () => judgements.Count == 2 && judgements.All(j => j.Result.IsHit()));
        AddStep("lift", () => InputManager.TouchUp(TouchSource.Touch1));
    }

    /// <summary>
    /// A finger following a hold moves as fast as the hold drifts; that isn't a flick.
    /// </summary>
    [Test]
    public void TestFollowingAHoldIsNoFlick()
    {
        play(new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane1, EndLane = NoteLane.Lane4, StartTime = 1000, EndTime = 1300 });

        at(1000);
        AddStep("touch the first lane", () => InputManager.TouchDown(TouchSource.Touch1, lane(0)));
        at(1100);
        AddStep("slide fast to the last", () => InputManager.MoveTouchTo(TouchSource.Touch1, lane(3)));
        at(1300);
        AddStep("lift", () => InputManager.TouchUp(TouchSource.Touch1));
        AddAssert("no swipe", () => inputs.All(i => i.Kind is not (ReplayInputKind.SwipeLeft or ReplayInputKind.SwipeRight)));
        AddAssert("held through", () => breaks == 0 && judgements.Count == 2 && judgements.All(j => j.Result.IsHit()));
    }

    /// <summary>
    /// A slide is recorded as one, and a replay of a touch run judges it the same.
    /// </summary>
    [Test]
    public void TestASlideIsReplayedAsOne()
    {
        var chart = new[]
        {
            new BeatmapNote { Type = NoteType.Hold, Lane = NoteLane.Lane1, EndLane = NoteLane.Lane2, StartTime = 1000, EndTime = 2000 },
            new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane3, StartTime = 1500, EndTime = 1500 }
        };
        play(chart);

        at(1000);
        AddStep("touch the first lane", () => InputManager.TouchDown(TouchSource.Touch1, lane(0)));
        at(1500);
        AddStep("slide into the second", () => InputManager.MoveTouchTo(TouchSource.Touch1, lane(1)));
        AddStep("tap the third", () => InputManager.Tap(lane(2), TouchSource.Touch2));
        at(2000);
        AddStep("lift", () => InputManager.TouchUp(TouchSource.Touch1));
        AddAssert("recorded as a slide, by its finger", () => inputs.Count(i => i.Kind == ReplayInputKind.TouchSlide && i.Finger == (int)TouchSource.Touch1) == 1);

        List<HitResult> played = null!;
        AddStep("replay it", () =>
        {
            played = judgements.Select(j => j.Result).ToList();
            List<ReplayInput> recorded = inputs.ToList();
            playNow(chart);
            playfield.ReplayInputs = recorded;
        });

        // On by steps, as a replay plays: inputs are taken against the notes on screen.
        for (int t = 250; t <= 2500; t += 250)
            at(t);

        AddAssert("judged the same", () => judgements.Select(j => j.Result).SequenceEqual(played) && played.Count == 3);
    }

    /// <summary>
    /// For now a flick takes a swipe whichever way the swipe points.
    /// </summary>
    [Test]
    public void TestAFlickHitsASwipeWhicheverWayItPoints()
    {
        play(new BeatmapNote { Type = NoteType.Swipe, Direction = SwipeDirection.Left, StartTime = 1000, EndTime = 1000 });

        at(1000);
        AddStep("flick right", () =>
        {
            InputManager.TouchDown(TouchSource.Touch1, lane(2));
            InputManager.MoveTouchTo(TouchSource.Touch1, lane(2) + new Vector2(60, 0));
            InputManager.TouchUp(TouchSource.Touch1);
        });
        AddAssert("the swipe is hit", () => judgements.Count == 1 && judgements[0].Note.Type == NoteType.Swipe && judgements[0].Result.IsHit());
    }

    /// <summary>
    /// At the line the highway spans nearly the whole field, but up toward the horizon it narrows, and a
    /// finger beside it there isn't on the lanes.
    /// </summary>
    [Test]
    public void TestATapBesideTheHighwayIsNotTheField()
    {
        play(new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 1000, EndTime = 1000 });

        at(1000);
        AddStep("tap the top-left corner", () => InputManager.Tap(playfield.ToScreenSpace(new Vector2(4, 4))));
        AddAssert("nothing hit", () => judgements.Count == 0);
    }

    /// <summary>
    /// By touch the judgement line sits higher, clear of a phone's gesture strip; with keys it's back down.
    /// </summary>
    [Test]
    public void TestTheLineSitsHigherByTouch()
    {
        play(new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 60_000, EndTime = 60_000 });

        at(60_000);
        AddAssert("the note due now at 85% down", () => noteAt(0.85f));
        AddStep("keys", () => gameLayout.Touch.Value = false);
        AddWaitStep("a frame", 50);
        AddAssert("at 90%", () => noteAt(0.9f));
    }

    /// <summary>
    /// The screen's measured delay is drawn ahead of too, so a note is seen at the line on its time.
    /// </summary>
    [Test]
    public void TestNotesAreDrawnAheadByTheScreensDelay()
    {
        play(new BeatmapNote { Type = NoteType.Tap, Lane = NoteLane.Lane1, StartTime = 60_000, EndTime = 60_000 });

        AddStep("a screen 50 ms behind", () => playfield.DisplayLatency = 50);
        at(59_950);
        AddAssert("drawn at the line already", () => noteAt(0.85f));
    }

    private bool noteAt(float down)
    {
        DrawableNote note = descendants<DrawableNote>(playfield).Single();
        Drawable bar = note.InternalChildren.OfType<Container>().Single().InternalChildren.OfType<Circle>().First();

        return Math.Abs(bar.DrawRectangle.Center.Y - playfield.ToScreenSpace(new Vector2(0, playfield.DrawHeight * down)).Y) < 0.5f;
    }

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

    private void play(params BeatmapNote[] notes) => AddStep("a chart", () => playNow(notes));

    private void playNow(BeatmapNote[] notes)
    {
        judgements.Clear();
        inputs.Clear();
        breaks = 0;
        TestContent.Clear();
        gameLayout.Touch.Value = true;
        time.CurrentTime = 0;

        TestContent.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Clock = new FramedClock(time),
            Child = playfield = new Playfield(new PlayableBeatmap(notes, laneCount: 4))
        });

        playfield.NewJudgement += judgements.Add;
        playfield.InputReceived += inputs.Add;
        playfield.HoldBroken += _ => breaks++;
    }

    private void at(double gameplayTime)
    {
        AddStep($"at {gameplayTime} ms", () => time.CurrentTime = gameplayTime);
        // A frame for the field to see the time, and the notes due by then.
        AddWaitStep("a frame", 50);
    }

    // The middle of a lane at the judgement line, nudged down by some, on screen.
    private Vector2 lane(int index, float down = 0)
    {
        var p = new LanePerspective { LaneCount = 4 };
        p.LayOut(playfield.DrawWidth, playfield.DrawHeight, touch: true);
        return playfield.ToScreenSpace(p.Project(0, p.LaneOffset(index)) + new Vector2(0, down));
    }
}
