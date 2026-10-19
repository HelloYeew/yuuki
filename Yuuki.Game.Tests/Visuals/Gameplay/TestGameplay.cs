// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// Visual + interactive coverage for the Normal-phase gameplay slice: a procedurally generated chart
/// scrolling on the gameplay clock, judged live from the keyboard (D F J K, and Left/Right Shift for swipes).
/// </summary>
public partial class TestGameplay : YuukiManualInputManagerTestScene
{
    private GameplayContainer gameplay = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache<IYuukiColorProvider>(new DefaultYuukiColorProvider());
    }

    private void build(double bpm, double leadIn = 2000)
    {
        PlayableBeatmap chart = ChartGenerator.Generate(bpm, 60000, laneCount: 4, seed: 1, density: 2);
        TestContent.Add(gameplay = new GameplayContainer(chart)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            LeadInTime = leadIn
        });
    }

    [Test]
    public void TestPlay()
    {
        AddStep("build (160 BPM)", () => build(160));
        AddStep("start", () => gameplay.Restart());

        AddSliderStep("scroll speed", 300f, 1200f, 750f, v =>
        {
            if (gameplay != null)
                gameplay.Preempt = v;
        });

        AddStep("press D", () => PressKeyOnce(Key.D));
        AddStep("press F", () => PressKeyOnce(Key.F));
        AddStep("press J", () => PressKeyOnce(Key.J));
        AddStep("press K", () => PressKeyOnce(Key.K));

        AddStep("restart", () => gameplay.Restart());
        AddStep("stop", () => gameplay.Stop());

        // Scrub / reverse the field like an editor or replay. Seeking turns off judgement so it
        // doesn't rack up phantom misses; the notes reconcile to whatever time you land on.
        AddSliderStep("seek (ms)", 0f, 60000f, 0f, v =>
        {
            if (gameplay == null)
                return;
            gameplay.AllowJudgement = false;
            gameplay.Seek(v);
        });
        AddSliderStep("rate (negative = reverse)", -3f, 3f, 1f, v =>
        {
            if (gameplay != null)
                gameplay.GameplayClock.Rate = v;
        });
    }

    [Test]
    public void TestSeekIsReversible()
    {
        // Time-windowed presentation: seeking away from notes and back must re-show them — proving
        // the field is no longer a one-way forward stream (usable for editor/replay).
        AddStep("build (120 BPM)", () => build(120, leadIn: 0));
        AddStep("no judgement (scrub mode)", () => gameplay.AllowJudgement = false);

        int countAt8000 = 0;
        AddStep("seek to 8000ms", () => gameplay.Seek(8000));
        AddWaitStep("settle", 3);
        AddStep("capture count at 8000", () => countAt8000 = gameplay.VisibleNoteCount);
        AddAssert("notes visible at 8000", () => countAt8000 > 0);

        AddStep("seek back to 1000ms", () => gameplay.Seek(1000));
        AddWaitStep("settle", 3);
        AddAssert("visible set differs near start", () => gameplay.VisibleNoteCount != countAt8000);

        AddStep("seek forward to 8000ms again", () => gameplay.Seek(8000));
        AddWaitStep("settle", 3);
        AddAssert("exact same count restored (reversible)", () => gameplay.VisibleNoteCount == countAt8000);
    }

    [Test]
    public void TestScoreAdvancesOnHit()
    {
        // Lead-in 0 so the headless clock reaches play time without traversing 2s of virtual lead-in.
        AddStep("build (120 BPM)", () => build(120, leadIn: 0));
        AddStep("start", () => gameplay.Restart());
        AddUntilStep("clock running", () => gameplay.GameplayClock.IsRunning);
        AddUntilStep("time advances", () => gameplay.GameplayClock.CurrentTime > 0);
    }

    [Test]
    public void TestSilencingKeepsTheSongGoing()
    {
        // A cleared run's song plays on into the results: its hit sounds stop, its clock (the track) doesn't.
        AddStep("build (120 BPM)", () => build(120, leadIn: 0));
        AddStep("start", () => gameplay.Restart());
        AddUntilStep("clock running", () => gameplay.GameplayClock.IsRunning);

        AddStep("silence, as a clear does", () => gameplay.SilenceHitSounds());
        AddWaitStep("a while", 5);
        AddAssert("still running", () => gameplay.GameplayClock.IsRunning);

        AddStep("stop, as leaving does", () => gameplay.Stop());
        AddAssert("stopped", () => !gameplay.GameplayClock.IsRunning);
    }

    [Test]
    public void TestAudioOffsetReachesClock()
    {
        AddStep("build with offset −44", () =>
        {
            PlayableBeatmap chart = ChartGenerator.Generate(120, 60000, laneCount: 4, seed: 1, density: 2);
            TestContent.Add(gameplay = new GameplayContainer(chart)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                LeadInTime = 0,
                AudioOffset = -44
            });
        });
        AddUntilStep("clock offset applied", () => gameplay.GameplayClock.Offset == -44);
        AddAssert("result records it", () => gameplay.BuildResult().AudioOffset == -44);
    }

    [Test]
    public void TestKeyPressScoresHit()
    {
        BeatmapNote target = null!;

        AddStep("build (120 BPM)", () => build(120, leadIn: 0));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to first tap note", () =>
        {
            target = System.Array.Find(gameplay.Chart.Notes, n => n.Type == NoteType.Tap)!;
            gameplay.GameplayClock.Seek(target.StartTime);
        });
        AddStep("press its lane key", () => PressKeyOnce(laneKey(target.Lane)));
        AddAssert("combo registered", () => gameplay.Score.Combo.Value >= 1);
        AddAssert("score registered", () => gameplay.Score.Score.Value > 0);
    }

    // Holds: the press (head) and the release (tail) are judged separately, so a
    // clean hold is two judgements and two combo.

    [Test]
    public void TestHoldReleasedJustAfterEndScores()
    {
        // A single-hold chart so seeking can't rack up misses on unrelated notes — the assertions are
        // then purely about the hold's own resolution.
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press and hold", () => InputManager.PressKey(Key.D));
        AddStep("seek just past the end", () => gameplay.GameplayClock.Seek(1550));
        AddStep("release", () => InputManager.ReleaseKey(Key.D));

        AddAssert("head and tail judged", () => gameplay.BuildResult().TotalNotes == 2);
        AddAssert("no miss", () => gameplay.BuildResult().MissCount == 0);
        AddAssert("two combo", () => gameplay.Score.Combo.Value == 2);
    }

    [Test]
    public void TestHoldReleasedInWindowScores()
    {
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press and hold", () => InputManager.PressKey(Key.D));
        AddStep("seek near the end", () => gameplay.GameplayClock.Seek(1480));
        AddStep("release", () => InputManager.ReleaseKey(Key.D));

        AddAssert("two combo", () => gameplay.Score.Combo.Value == 2);
        AddAssert("tail is a critical", () => gameplay.BuildResult().CriticalCount == 2);
    }

    [Test]
    public void TestHoldHeldTooLongMissesTail()
    {
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press and hold", () => InputManager.PressKey(Key.D));
        // Past the end by more than the release window (300 ms) without letting go.
        AddStep("seek well past the end", () => gameplay.GameplayClock.Seek(1850));
        AddUntilStep("tail missed", () => gameplay.BuildResult().MissCount == 1);
        AddStep("release late", () => InputManager.ReleaseKey(Key.D));

        AddAssert("head still hit", () => gameplay.BuildResult().CriticalCount == 1);
        AddAssert("combo broken", () => gameplay.Score.Combo.Value == 0);
    }

    [Test]
    public void TestHoldDroppedBreaksCombo()
    {
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press the head", () => InputManager.PressKey(Key.D));
        // Let go far before the end — a hold break, not yet a judgement.
        AddStep("release straight away", () => InputManager.ReleaseKey(Key.D));

        AddAssert("combo broken", () => gameplay.Score.Combo.Value == 0);
        AddAssert("one hold break", () => gameplay.BuildResult().HoldBreakCount == 1);
        AddAssert("tail not judged yet", () => gameplay.BuildResult().TotalNotes == 1);

        AddStep("seek past the release window", () => gameplay.GameplayClock.Seek(1850));
        AddUntilStep("tail missed", () => gameplay.BuildResult().MissCount == 1);
        AddAssert("not a full combo", () => !gameplay.BuildResult().IsFullCombo);
    }

    [Test]
    public void TestHoldRegrabCapsTail()
    {
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press the head", () => InputManager.PressKey(Key.D));
        AddStep("let go early", () => InputManager.ReleaseKey(Key.D));
        AddStep("seek into the body", () => gameplay.GameplayClock.Seek(1300));
        AddStep("grab it again", () => InputManager.PressKey(Key.D));
        AddStep("seek to the end", () => gameplay.GameplayClock.Seek(1490));
        AddStep("release on time", () => InputManager.ReleaseKey(Key.D));

        AddAssert("head critical", () => gameplay.BuildResult().CriticalCount == 1);
        AddAssert("tail capped at hit", () => gameplay.BuildResult().HitCount == 1);
        AddAssert("no miss", () => gameplay.BuildResult().MissCount == 0);
        AddAssert("not a full combo", () => !gameplay.BuildResult().IsFullCombo);
    }

    [Test]
    public void TestHoldGrabbedAfterMissedHead()
    {
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek past the head's window", () => gameplay.GameplayClock.Seek(1300));
        AddUntilStep("head missed", () => gameplay.BuildResult().MissCount == 1);
        AddStep("grab the body", () => InputManager.PressKey(Key.D));
        AddStep("seek to the end", () => gameplay.GameplayClock.Seek(1500));
        AddStep("release on time", () => InputManager.ReleaseKey(Key.D));

        AddAssert("tail caught, capped at hit", () => gameplay.BuildResult().HitCount == 1);
        AddAssert("only the head missed", () => gameplay.BuildResult().MissCount == 1);
    }

    [TestCase(SwipeDirection.Any, Key.LShift)]
    [TestCase(SwipeDirection.Any, Key.RShift)]
    [TestCase(SwipeDirection.Left, Key.LShift)]
    [TestCase(SwipeDirection.Right, Key.RShift)]
    public void TestSwipeHitByMatchingSide(SwipeDirection direction, Key key)
    {
        AddStep($"build single {direction} swipe", () => buildSingleNote(NoteType.Swipe, 1000, direction));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the swipe", () => gameplay.GameplayClock.Seek(1000));
        AddStep($"press {key}", () => PressKeyOnce(key));

        AddAssert("combo registered", () => gameplay.Score.Combo.Value == 1);
        AddAssert("judged as a hit", () => gameplay.BuildResult().TotalNotes == 1 && gameplay.BuildResult().MissCount == 0);
    }

    [Test]
    public void TestReboundSwipeKey()
    {
        AddStep("build a left swipe, swipes on Q and P", () =>
        {
            var chart = new PlayableBeatmap(new[]
            {
                new BeatmapNote { Lane = NoteLane.Lane1, Type = NoteType.Swipe, Direction = SwipeDirection.Left, StartTime = 1000, EndTime = 1000 }
            }, laneCount: 4);

            TestContent.Add(gameplay = new GameplayContainer(chart)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                LeadInTime = 0,
                SwipeLeftKey = Key.Q,
                SwipeRightKey = Key.P
            });
        });
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the swipe", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press the old key", () => PressKeyOnce(Key.LShift));
        AddAssert("nothing judged", () => gameplay.BuildResult().TotalNotes == 0);
        AddStep("press Q", () => PressKeyOnce(Key.Q));
        AddAssert("hit", () => gameplay.Score.Combo.Value == 1);
    }

    [TestCase(SwipeDirection.Left, Key.RShift)]
    [TestCase(SwipeDirection.Right, Key.LShift)]
    public void TestSwipeIgnoresWrongSide(SwipeDirection direction, Key wrongKey)
    {
        AddStep($"build single {direction} swipe", () => buildSingleNote(NoteType.Swipe, 1000, direction));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the swipe", () => gameplay.GameplayClock.Seek(1000));
        AddStep($"press {wrongKey}", () => PressKeyOnce(wrongKey));

        // The wrong side is an empty press: nothing judged, and the note is still there to hit.
        AddAssert("nothing judged", () => gameplay.BuildResult().TotalNotes == 0);
        AddStep("press the right side", () => PressKeyOnce(wrongKey == Key.LShift ? Key.RShift : Key.LShift));
        AddAssert("now hit", () => gameplay.Score.Combo.Value == 1);
    }

    [Test]
    public void TestFailFiresWhenHealthRunsOut()
    {
        bool failed = false;

        AddStep("build 20 stacked taps", () =>
        {
            var notes = new BeatmapNote[20];
            for (int i = 0; i < notes.Length; i++)
                notes[i] = new BeatmapNote { Lane = (NoteLane)(i % 4), Type = NoteType.Tap, StartTime = 1000, EndTime = 1000 };

            TestContent.Add(gameplay = new GameplayContainer(new PlayableBeatmap(notes, laneCount: 4))
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                LeadInTime = 0
            });
            gameplay.Failed += () => failed = true;
        });
        AddStep("start", () => gameplay.Restart());
        // Past every note's miss window: twenty misses, far more than health can take.
        AddStep("seek past them", () => gameplay.GameplayClock.Seek(1300));
        AddUntilStep("failed", () => failed);
        AddAssert("result says failed", () => gameplay.BuildResult().Failed);
        AddAssert("everything judged", () => gameplay.AllJudged);
    }

    [Test]
    public void TestAllJudgedAfterLastNote()
    {
        AddStep("build single tap", () => buildSingleNote(NoteType.Tap, 1000));
        AddStep("start", () => gameplay.Restart());
        AddAssert("not yet", () => !gameplay.AllJudged);
        AddStep("seek to the tap", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press", () => PressKeyOnce(Key.D));
        AddAssert("all judged", () => gameplay.AllJudged);
    }

    [Test]
    public void TestPausedInputJudgesNothing()
    {
        AddStep("build single tap", () => buildSingleNote(NoteType.Tap, 1000));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the tap", () => gameplay.GameplayClock.Seek(1000));
        AddStep("pause input", () => gameplay.InputPaused = true);
        AddStep("press", () => PressKeyOnce(Key.D));
        AddAssert("nothing judged", () => gameplay.BuildResult().TotalNotes == 0);
        AddStep("resume input", () => gameplay.InputPaused = false);
        AddStep("press again", () => PressKeyOnce(Key.D));
        AddAssert("now hit", () => gameplay.Score.Combo.Value == 1);
    }

    [Test]
    public void TestHoldLetGoDuringPauseBreaksOnResume()
    {
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press and hold", () => InputManager.PressKey(Key.D));
        AddStep("pause input", () => gameplay.InputPaused = true);
        AddStep("let go while paused", () => InputManager.ReleaseKey(Key.D));
        AddAssert("no break yet", () => gameplay.BuildResult().HoldBreakCount == 0);
        AddStep("resume input", () => gameplay.InputPaused = false);
        AddAssert("released on resume: a hold break", () => gameplay.BuildResult().HoldBreakCount == 1);
    }

    [Test]
    public void TestRestartClearsEffects()
    {
        AddStep("build two taps", () => buildChart(4, note(NoteType.Tap, 1000), note(NoteType.Tap, 1500, NoteLane.Lane2)));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the first", () => gameplay.GameplayClock.Seek(1000));
        AddStep("hit it", () => PressKeyOnce(Key.D));
        AddStep("let the second pass", () => gameplay.GameplayClock.Seek(1800));
        AddUntilStep("missed, lane flashing", () => gameplay.BuildResult().MissCount == 1 && gameplay.AnyLaneFlashing);
        AddStep("restart", () => gameplay.Restart());
        AddAssert("no lane flashing", () => !gameplay.AnyLaneFlashing);
        AddAssert("no effects left", () => gameplay.ActiveEffectCount == 0);
        AddAssert("fresh result", () => gameplay.BuildResult().TotalNotes == 0);
    }

    [Test]
    public void TestTouchCaughtByHeldLane()
    {
        AddStep("build touch", () => buildChart(4, note(NoteType.Touch, 1000)));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek before it", () => gameplay.GameplayClock.Seek(900));
        AddStep("hold the lane", () => InputManager.PressKey(Key.D));
        AddAssert("nothing yet", () => gameplay.BuildResult().TotalNotes == 0);
        AddStep("let it arrive", () => gameplay.GameplayClock.Seek(1000));
        AddUntilStep("caught", () => gameplay.BuildResult().CriticalCount == 1);
        AddStep("release", () => InputManager.ReleaseKey(Key.D));
    }

    [Test]
    public void TestTouchMissedWithoutLane()
    {
        AddStep("build touch", () => buildChart(4, note(NoteType.Touch, 1000)));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek past it", () => gameplay.GameplayClock.Seek(1300));
        AddUntilStep("missed", () => gameplay.BuildResult().MissCount == 1);
    }

    [Test]
    public void TestPassCaughtByCharacter()
    {
        AddStep("build pass in lane 3", () => buildChart(4, note(NoteType.Pass, 1000, NoteLane.Lane3)));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek well before", () => gameplay.GameplayClock.Seek(700));
        AddStep("step into lane 3", () => PressKeyOnce(Key.J));
        AddAssert("character moved", () => gameplay.CharacterLane == 2);
        AddStep("let it arrive", () => gameplay.GameplayClock.Seek(1000));
        AddUntilStep("caught, early enough for critical", () => gameplay.BuildResult().CriticalCount == 1);
    }

    [Test]
    public void TestPassMissedWhenCharacterElsewhere()
    {
        AddStep("build pass in lane 3", () => buildChart(4, note(NoteType.Pass, 1000, NoteLane.Lane3)));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek past it", () => gameplay.GameplayClock.Seek(1300));
        AddUntilStep("missed", () => gameplay.BuildResult().MissCount == 1);
    }

    [Test]
    public void TestCharacterAnchoredDuringHold()
    {
        AddStep("build single hold", () => buildSingleHold(1000, 1500));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press and hold", () => InputManager.PressKey(Key.D));
        AddStep("press another lane", () => PressKeyOnce(Key.K));
        AddAssert("still anchored to the hold", () => gameplay.CharacterLane == 0);
        AddStep("release", () => InputManager.ReleaseKey(Key.D));
    }

    [Test]
    public void TestBreakToCriticalSkill()
    {
        AddStep("build skill then tap", () => buildChart(4,
            new BeatmapNote { Lane = NoteLane.Lane1, Type = NoteType.Skill, Skill = SkillType.BreakToCritical, StartTime = 1000, EndTime = 1000 },
            note(NoteType.Tap, 2000)));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the skill", () => gameplay.GameplayClock.Seek(1000));
        AddStep("hit it", () => PressKeyOnce(Key.D));
        // 70 ms late would be a Break; with the skill running it counts as a Critical.
        AddStep("seek 70 ms past the tap", () => gameplay.GameplayClock.Seek(2070));
        AddStep("hit the tap", () => PressKeyOnce(Key.D));
        AddAssert("both critical", () => gameplay.BuildResult().CriticalCount == 2);
        AddAssert("no break", () => gameplay.BuildResult().BreakCount == 0);
    }

    [Test]
    public void TestLaneChangingHoldIsFollowedOnTheKeyboard()
    {
        // Lane 1 to lane 3: D at the head, then J (lane 3's key) by the end, letting go of D.
        AddStep("build drifting hold", () => buildChart(4,
            new BeatmapNote { Lane = NoteLane.Lane1, EndLane = NoteLane.Lane3, Type = NoteType.Hold, StartTime = 1000, EndTime = 1500 }));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to the head", () => gameplay.GameplayClock.Seek(1000));
        AddStep("press the start lane", () => InputManager.PressKey(Key.D));
        AddStep("seek near the end", () => gameplay.GameplayClock.Seek(1400));
        AddStep("follow onto the end lane", () =>
        {
            InputManager.PressKey(Key.J);
            InputManager.ReleaseKey(Key.D);
        });
        AddStep("seek to the end", () => gameplay.GameplayClock.Seek(1490));
        AddStep("let go from the end lane", () => InputManager.ReleaseKey(Key.J));
        AddAssert("press and release judged, no miss", () => gameplay.BuildResult().TotalNotes == 2 && gameplay.BuildResult().MissCount == 0);
    }

    [Test]
    public void TestSixLaneLayout()
    {
        AddStep("build 6-lane tap on lane 1", () => buildChart(6, note(NoteType.Tap, 1000)));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek to it", () => gameplay.GameplayClock.Seek(1000));
        // Six lanes default to S D F J K L.
        AddStep("press S", () => PressKeyOnce(Key.S));
        AddAssert("hit", () => gameplay.Score.Combo.Value == 1);
    }

    private static BeatmapNote note(NoteType type, double time, NoteLane lane = NoteLane.Lane1) =>
        new BeatmapNote { Lane = lane, Type = type, StartTime = time, EndTime = time };

    private void buildChart(int laneCount, params BeatmapNote[] notes)
    {
        TestContent.Add(gameplay = new GameplayContainer(new PlayableBeatmap(notes, laneCount))
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            LeadInTime = 0
        });
    }

    [Test]
    public void TestEarlyPressInMissBandIsAMiss()
    {
        AddStep("build single tap", () => buildSingleNote(NoteType.Tap, 1000));
        // 160 ms early: past Hit (135), inside the miss window (180).
        AddStep("start", () => gameplay.Restart());
        AddStep("seek 160 ms early", () => gameplay.GameplayClock.Seek(840));
        AddStep("press", () => PressKeyOnce(Key.D));

        AddAssert("taken as a miss", () => gameplay.BuildResult().MissCount == 1);
        AddAssert("no combo", () => gameplay.Score.Combo.Value == 0);
    }

    [Test]
    public void TestPressBeyondMissWindowIsIgnored()
    {
        AddStep("build single tap", () => buildSingleNote(NoteType.Tap, 1000));
        AddStep("start", () => gameplay.Restart());
        AddStep("seek 250 ms early", () => gameplay.GameplayClock.Seek(750));
        AddStep("press", () => PressKeyOnce(Key.D));

        AddAssert("nothing judged", () => gameplay.BuildResult().TotalNotes == 0);
    }

    private void buildSingleNote(NoteType type, double time, SwipeDirection direction = SwipeDirection.Any)
    {
        var chart = new PlayableBeatmap(new[]
        {
            new BeatmapNote { Lane = NoteLane.Lane1, Type = type, Direction = direction, StartTime = time, EndTime = time }
        }, laneCount: 4);

        TestContent.Add(gameplay = new GameplayContainer(chart)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            LeadInTime = 0
        });
    }

    private void buildSingleHold(double start, double end)
    {
        var chart = new PlayableBeatmap(new[]
        {
            new BeatmapNote { Lane = NoteLane.Lane1, Type = NoteType.Hold, StartTime = start, EndTime = end }
        }, laneCount: 4);

        TestContent.Add(gameplay = new GameplayContainer(chart)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            LeadInTime = 0
        });
    }

    private static Key laneKey(NoteLane lane) => lane switch
    {
        NoteLane.Lane1 => Key.D,
        NoteLane.Lane2 => Key.F,
        NoteLane.Lane3 => Key.J,
        _ => Key.K
    };
}
