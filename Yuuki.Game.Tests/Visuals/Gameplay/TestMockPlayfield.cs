// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay;
using Yuuki.Game.Gameplay.Scoring;

namespace Yuuki.Game.Tests.Visuals.Gameplay;

/// <summary>
/// The field calibration taps along on, and the one a skin's preview will run: a chart going round on its own clock.
/// </summary>
public partial class TestMockPlayfield : YuukiTestScene
{
    private MockPlayfield field = null!;

    [Test]
    public void TestRunsEveryKindOfNote()
    {
        AddStep("Run a generated chart", () =>
        {
            Clear();
            Add(field = new MockPlayfield(ChartGenerator.Generate(120, 8000, seed: 3, density: 2), 8000, 6000)
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            });
        });

        AddUntilStep("Loaded", () => field.IsLoaded);
        AddStep("Three seconds in", () => holdAt(3000));
        AddUntilStep("Notes come down", () => field.Playfield.VisibleNoteCount > 0);
    }

    [Test]
    public void TestGoesRoundWithoutStopping()
    {
        AddStep("Run a short loop", () =>
        {
            Clear();
            Add(field = new MockPlayfield(beats(20), 1250, 1000, null) { Preempt = 500 });
        });

        AddUntilStep("Loaded", () => field.IsLoaded);
        AddStep("Just past the loop's end", () => holdAt(1300));
        AddUntilStep("Back round, as far past its start", () => Math.Abs(field.GameplayClock.CurrentTime - 300) < 1);
        AddUntilStep("With its notes coming down again", () => field.Playfield.VisibleNoteCount > 0);
    }

    /// <summary>
    /// A visual offset draws the notes further along without moving the judging: a tap on a note's own time is still
    /// right on it.
    /// </summary>
    [Test]
    public void TestVisualOffsetMovesTheNotesNotTheJudging()
    {
        JudgementResult? judged = null;

        AddStep("A note at 2 s, a second of scroll", () =>
        {
            Clear();
            Add(field = new MockPlayfield(new PlayableBeatmap([tap(2000)], 4), 10000, 5000, null)
            {
                Preempt = 1000,
                AllowJudgement = true
            });
        });

        AddUntilStep("Loaded", () => field.IsLoaded);
        AddStep("Hold the clock at 0.8 s", () => holdAt(800));
        AddAssert("Not on screen yet", () => field.Playfield.VisibleNoteCount == 0);

        AddStep("Draw 300 ms along", () => field.VisualOffset = 300);
        AddUntilStep("On screen already", () => field.Playfield.VisibleNoteCount == 1);

        AddStep("Tap on its time", () =>
        {
            field.Playfield.NewJudgement += j => judged = j;
            holdAt(2000);
            field.Playfield.PressLane(0, 2000);
        });
        AddAssert("Right on it", () => judged?.Result == HitResult.Critical);
    }

    private void holdAt(double time)
    {
        field.GameplayClock.Stop();
        field.GameplayClock.Seek(time);
        field.GameplayClock.ProcessFrame();
    }

    private static PlayableBeatmap beats(int count)
    {
        var notes = new BeatmapNote[count];

        for (int i = 0; i < count; i++)
            notes[i] = tap(250 * (i + 1), (NoteLane)(i % 4));

        return new PlayableBeatmap(notes, 4);
    }

    private static BeatmapNote tap(double time, NoteLane lane = NoteLane.Lane1) => new BeatmapNote
    {
        Lane = lane,
        Type = NoteType.Tap,
        StartTime = time,
        EndTime = time
    };
}
