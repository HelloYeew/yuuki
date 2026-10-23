// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Screens;
using Yuuki.Game.Graphics.UserInterface;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestScoreScreen : TestYuukiScreenStack
{
    // A config so the "shift timing" suggestion shows (the built runs lean late).
    private readonly YuukiConfigManager config = new YuukiInMemoryConfigManager();

    [BackgroundDependencyLoader]
    private void load()
    {
        Cache(config);
    }

    [Test]
    public void TestAllPerfect() => showResult("all perfect", build(critical: 400, seed: 1), r => r.IsAllPerfect);

    [Test]
    public void TestFullCombo() => showResult("full combo", build(critical: 320, breaks: 60, hits: 20, seed: 2), r => r.IsFullCombo && !r.IsAllPerfect);

    [Test]
    public void TestMixedRun() => showResult("mixed run", build(critical: 210, breaks: 90, hits: 40, misses: 25, seed: 3), r => !r.IsFullCombo);

    [Test]
    public void TestSLostToMiss() => showResult("S lost to a miss", build(critical: 380, breaks: 15, misses: 1, seed: 5), r => r.RankLostToMiss && r.Rank == ScoreRank.A);

    [Test]
    public void TestPoorRun() => showResult("poor run", build(critical: 60, breaks: 80, hits: 90, misses: 120, seed: 4), r => r.Rank == ScoreRank.D);

    [Test]
    public void TestEmptyRun() => showResult("empty run", new GameplayResult(), r => r.TotalNotes == 0);

    /// <summary>
    /// The offer moves the judging alone: both offsets by the run's lean, the other way about.
    /// </summary>
    [Test]
    public void TestShiftingTheTimingKeepsTheNotesInPlace()
    {
        showResult("25 ms late, at -20 and +10", leaning(25, audio: -20, visual: 10), r => r.SuggestedOffsets == (-45, 35));
        AddAssert("no calibrating offered", () => pillOrNull("Calibrate") == null);

        AddStep("shift", () => pill("Shift timing 25 ms later · every device").Action());
        AddAssert("audio -45, visual +35", () => config.Get<int>(YuukiSetting.AudioOffset).Value == -45 && config.Get<int>(YuukiSetting.VisualOffset).Value == 35);
        AddAssert("says so", () => pillOrNull("Audio -45 ms · Visual +35 ms") is { Enabled.Value: false });
    }

    /// <summary>
    /// One play leans this far by chance now and then, so on its own it isn't worth a shift.
    /// </summary>
    [Test]
    public void TestOnePlaysSmallLeanOffersNothing()
    {
        showResult("17 ms late", leaning(17), r => r.SuggestedOffsets != null);
        AddAssert("no shift offered", () => descendants(shown!).OfType<PillButton>().All(p => !p.Text.StartsWith("Shift timing", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A lean that cost Criticals says so, and points to how the offsets fix it; one that didn't, doesn't, and nor does
    /// a small one, which over a long song costs a few at the window's edge.
    /// </summary>
    [Test]
    public void TestALeanThatCostCriticalsPointsToTheGuide()
    {
        showResult("5 ms late", leaning(5), r => r.CriticalsLostToLean == 0);
        AddAssert("no guide", () => pillOrNull("How offsets work") == null);

        showResult("8 ms late over a long song", spread(8), r => r.CriticalsLostToLean == 30);
        AddAssert("no guide", () => pillOrNull("How offsets work") == null);

        showResult("60 ms late", leaning(60), r => r.CriticalsLostToLean == 30);
        AddStep("open the guide", () => pill("How offsets work").Action());
        AddUntilStep("the guide", () => screenStack.CurrentScreen is OffsetGuideScreen);
        AddStep("back", () => screenStack.CurrentScreen.Exit());
        AddUntilStep("results again", () => screenStack.CurrentScreen is ScoreScreen);
    }

    /// <summary>
    /// An error this large is more likely a new delay than the player's lean: calibrating is offered too, and once
    /// chosen, the shift worked out against the old offsets is withdrawn.
    /// </summary>
    [Test]
    public void TestALargeErrorOffersCalibrating()
    {
        showResult("60 ms late", leaning(60), r => r.MeanError > 40);

        AddStep("calibrate", () => pill("Calibrate").Action());
        AddUntilStep("calibrating", () => screenStack.CurrentScreen is CalibrationScreen);
        AddAssert("the shift withdrawn", () => !pill("Shift timing 60 ms later · every device").Enabled.Value);
        AddStep("back", () => screenStack.CurrentScreen.Exit());
        AddUntilStep("results again", () => screenStack.CurrentScreen is ScoreScreen);
    }

    // Thirty presses, all the same distance off.
    private static GameplayResult leaning(double error, double audio = 0, double visual = 0) => new GameplayResult
    {
        CriticalCount = 30,
        Judgements = Enumerable.Range(0, 30).Select(i => new JudgementResult(new BeatmapNote { Type = NoteType.Tap, StartTime = 1000 + i * 500 }, HitResult.Critical, error)).ToArray(),
        AudioOffset = audio,
        VisualOffset = visual,
        ChartLength = 16_000
    };

    /// <summary>
    /// 800 presses spread evenly from 40 ms before <paramref name="error"/> to 40 ms after it, as a player's spread: those
    /// past the Critical window are Breaks.
    /// </summary>
    private static GameplayResult spread(double error)
    {
        var windows = new HitWindows();
        var judgements = Enumerable.Range(0, 800).Select(i =>
        {
            double timing = error - 40 + 80.0 * i / 799;
            var note = new BeatmapNote { Type = NoteType.Tap, StartTime = 1000 + i * 200 };
            return new JudgementResult(note, Math.Abs(timing) <= windows.Critical ? HitResult.Critical : HitResult.Break, timing);
        }).ToArray();

        return new GameplayResult
        {
            CriticalCount = judgements.Count(j => j.Result == HitResult.Critical),
            BreakCount = judgements.Count(j => j.Result == HitResult.Break),
            Judgements = judgements,
            ChartLength = 1000 + 800 * 200
        };
    }

    private ScoreScreen? shown;

    private PillButton pill(string text) => pillOrNull(text)!;

    private PillButton? pillOrNull(string text) => shown == null ? null : descendants(shown).OfType<PillButton>().FirstOrDefault(p => p.Text == text);

    private static IEnumerable<Drawable> descendants(Drawable drawable)
    {
        yield return drawable;

        if (drawable is Container container)
        {
            foreach (Drawable child in container.Children)
            {
                foreach (Drawable d in descendants(child))
                    yield return d;
            }
        }
    }

    private void showResult(string name, GameplayResult result, Func<GameplayResult, bool> expectation)
    {
        AddAssert($"{name} result is as built", () => expectation(result));
        AddStep("clear previous results", () =>
        {
            if (screenStack.CurrentScreen is ScoreScreen previous)
                previous.Exit();
        });
        AddStep($"push {name}", () => screenStack.Push(shown = new ScoreScreen(result)
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both
        }));
        AddUntilStep("results is current", () => screenStack.CurrentScreen is ScoreScreen screen && screen.Result == result);
    }

    /// <summary>
    /// A plausible run: notes spread over the chart in a shuffled order, each judgement's timing error
    /// drawn from inside its window with a slight late lean, and a mix of note types.
    /// </summary>
    private static GameplayResult build(int critical = 0, int breaks = 0, int hits = 0, int misses = 0, int seed = 0)
    {
        var random = new Random(seed);
        var windows = new HitWindows();
        var results = new List<HitResult>();

        results.AddRange(repeat(HitResult.Critical, critical));
        results.AddRange(repeat(HitResult.Break, breaks));
        results.AddRange(repeat(HitResult.Hit, hits));
        results.AddRange(repeat(HitResult.Miss, misses));

        // Shuffle so misses land throughout the song rather than all at the end.
        for (int i = results.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (results[i], results[j]) = (results[j], results[i]);
        }

        NoteType[] types = { NoteType.Tap, NoteType.Tap, NoteType.Tap, NoteType.Hold, NoteType.Swipe };
        var judgements = new JudgementResult[results.Count];
        int combo = 0, maxCombo = 0;
        double valueTotal = 0;

        for (int i = 0; i < results.Count; i++)
        {
            HitResult result = results[i];
            double time = 1000 + i * 300;

            double error = result switch
            {
                HitResult.Critical => between(random, 0, windows.Critical),
                HitResult.Break => between(random, windows.Critical, windows.Break),
                HitResult.Hit => between(random, windows.Break, windows.Hit),
                _ => windows.MissWindow + 1
            };

            // Most hits land a touch late; misses are never pressed so stay positive.
            if (result != HitResult.Miss && random.NextDouble() < 0.4)
                error = -error;

            var note = new BeatmapNote { Lane = (NoteLane)(i % 4), Type = types[i % types.Length], StartTime = time, EndTime = time };
            judgements[i] = new JudgementResult(note, result, error);

            combo = result.IsHit() ? combo + 1 : 0;
            maxCombo = Math.Max(maxCombo, combo);
            valueTotal += result.Value();
        }

        return new GameplayResult
        {
            // Roughly what the normalised scorer would give: mostly accuracy, a little combo.
            Score = (int)(ScoreProcessor.MAX_SCORE * (0.9 * valueTotal / Math.Max(1, results.Count) + 0.1 * maxCombo / Math.Max(1.0, results.Count))),
            MaxCombo = maxCombo,
            CriticalCount = critical,
            BreakCount = breaks,
            HitCount = hits,
            MissCount = misses,
            Judgements = judgements,
            HitWindows = windows,
            ChartLength = 1000 + results.Count * 300
        };
    }

    private static IEnumerable<HitResult> repeat(HitResult result, int count)
    {
        for (int i = 0; i < count; i++)
            yield return result;
    }

    private static double between(Random random, double min, double max) => min + random.NextDouble() * (max - min);
}
