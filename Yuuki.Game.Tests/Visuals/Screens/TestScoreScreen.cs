// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Primitives;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Configurations;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Screens;

namespace Yuuki.Game.Tests.Visuals.Screens;

public partial class TestScoreScreen : TestYuukiScreenStack
{
    // A config so the "set audio offset" suggestion shows (the built runs lean late).
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

    private void showResult(string name, GameplayResult result, Func<GameplayResult, bool> expectation)
    {
        AddAssert($"{name} result is as built", () => expectation(result));
        AddStep("clear previous results", () =>
        {
            if (screenStack.CurrentScreen is ScoreScreen previous)
                previous.Exit();
        });
        AddStep($"push {name}", () => screenStack.Push(new ScoreScreen(result)
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
