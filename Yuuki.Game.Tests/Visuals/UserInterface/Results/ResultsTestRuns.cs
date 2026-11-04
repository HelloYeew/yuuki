// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Gameplay.Scoring;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Tests.Visuals.UserInterface.Results;

/// <summary>
/// Runs for the results' tests to show, as real ones go: the timing drifting through the song, one lane pressed later
/// than the rest, holds with their releases, a rough patch or two, and holds let go of.
/// </summary>
public static class ResultsTestRuns
{
    public static YuukiColorPalette Palette => DefaultYuukiColorProvider.GetDefaultPaletteForPart(YuukiScreenPart.Play);

    /// <param name="seed">Which run.</param>
    /// <param name="rough">With its rough patches (misses, and two holds let go of), or clean (a full combo).</param>
    /// <param name="length">How long the song is, in ms.</param>
    public static GameplayResult Rich(int seed = 3, bool rough = true, double length = 130_000)
    {
        var random = new Random(seed);
        var windows = new HitWindows();
        var judgements = new List<JudgementResult>();

        double gauss() => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());

        for (double time = 1500; time < length - 1000; time += 120 + random.NextDouble() * 160)
        {
            int lane = random.Next(4);
            bool hold = random.NextDouble() < 0.12;
            bool patch = rough && (Math.Abs(time - length * 0.55) < 2500 || Math.Abs(time - length * 0.89) < 1500);
            double mean = 6 + 9 * Math.Sin(time / 21_000) + (lane == 2 ? 10 : 0);
            double error = mean + gauss() * (patch ? 50 : 17);

            NoteType type = hold ? NoteType.Hold : random.NextDouble() < 0.05 ? NoteType.Swipe : NoteType.Tap;
            var note = new BeatmapNote { Lane = (NoteLane)lane, Type = type, StartTime = time, EndTime = hold ? time + 400 + random.NextDouble() * 600 : time };

            HitResult result = patch && random.NextDouble() < 0.35 ? HitResult.Miss : windows.ResultFor(error);
            if (!rough && result == HitResult.Miss)
                result = HitResult.Hit;

            judgements.Add(new JudgementResult(note, result, result == HitResult.Miss ? windows.MissWindow + 3 : error));

            if (!hold)
                continue;

            double release = -25 + gauss() * 30;
            HitResult tail = windows.ResultForRelease(release);
            judgements.Add(new JudgementResult(note, !rough && tail == HitResult.Miss ? HitResult.Hit : tail, release, true));
        }

        double[] holdBreaks = rough ? new[] { length * 0.62, length * 0.985 } : Array.Empty<double>();
        var counted = new PlayAnalysis(new GameplayResult { Judgements = judgements.ToArray(), HoldBreakTimes = holdBreaks, ChartLength = length });

        return new GameplayResult
        {
            Score = 987_654,
            MaxCombo = counted.MaxCombo,
            BloomCount = judgements.Count(j => j.Result == HitResult.Bloom),
            CriticalCount = judgements.Count(j => j.Result == HitResult.Critical),
            BreakCount = judgements.Count(j => j.Result == HitResult.Break),
            HitCount = judgements.Count(j => j.Result == HitResult.Hit),
            MissCount = judgements.Count(j => j.Result == HitResult.Miss),
            HoldBreakCount = holdBreaks.Length,
            HoldBreakTimes = holdBreaks,
            Judgements = judgements.ToArray(),
            HitWindows = windows,
            ChartLength = length,
            LaneCount = 4
        };
    }
}
