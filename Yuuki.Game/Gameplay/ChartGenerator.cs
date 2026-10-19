// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Beatmaps.ControlPoints;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// Builds a <see cref="PlayableBeatmap"/> procedurally from a beatmap's BPM and length, so the
/// prototype has something to play without an authored chart or on-disk format. Deterministic for a
/// given seed, which keeps test scenes repeatable.
/// </summary>
public static class ChartGenerator
{
    /// <summary>
    /// Generates a chart from a <see cref="BeatmapSet"/>.
    /// </summary>
    /// <param name="beatmapSet">Source of BPM and total length.</param>
    /// <param name="laneCount">Number of active lanes.</param>
    /// <param name="seed">Seed for deterministic output.</param>
    /// <param name="density">Notes per beat on average (1 = one per beat, 2 = eighths).</param>
    public static PlayableBeatmap Generate(BeatmapSet beatmapSet, int laneCount = 4, int seed = 1, double density = 1)
    {
        double bpm = beatmapSet.BPM > 0 ? beatmapSet.BPM : 120;
        double length = beatmapSet.TotalLength > 0 ? beatmapSet.TotalLength : 60000;
        return Generate(bpm, length, laneCount, seed, density);
    }

    /// <param name="bpm">Beats per minute.</param>
    /// <param name="totalLength">Total playable length in milliseconds.</param>
    /// <param name="laneCount">Number of active lanes.</param>
    /// <param name="seed">Seed for deterministic output.</param>
    /// <param name="density">Notes per beat on average (1 = one per beat, 2 = eighths).</param>
    public static PlayableBeatmap Generate(double bpm, double totalLength, int laneCount = 4, int seed = 1, double density = 1)
    {
        var random = new Random(seed);
        var notes = new List<BeatmapNote>();

        double beatLength = 60000.0 / Math.Max(1, bpm);
        double step = beatLength / Math.Max(0.25, density);

        // Start a couple of beats in so the player has lead-in before the first note.
        double startTime = beatLength * 4;
        // Stop a beat short of the end so a hold never runs past the track.
        double endTime = Math.Max(startTime, totalLength - beatLength);

        int lastLane = -1;
        int beatIndex = 0;
        int swipeIndex = 0;
        int holdIndex = 0;
        int tapIndex = 0;

        for (double time = startTime; time < endTime; time += step, beatIndex++)
        {
            // Occasionally skip a step so the pattern breathes rather than being a constant stream.
            if (random.NextDouble() < 0.2)
                continue;

            int lane = random.Next(laneCount);
            // Avoid repeating the same lane back-to-back so it reads more like a real chart.
            if (lane == lastLane)
                lane = (lane + 1) % laneCount;
            lastLane = lane;

            double roll = random.NextDouble();
            NoteType type;
            double end;

            if (roll < 0.12)
            {
                // Hold spanning one or two beats, clamped to the end.
                type = NoteType.Hold;
                end = Math.Min(endTime, time + beatLength * (random.Next(2) + 1));
            }
            else if (roll < 0.18)
            {
                type = NoteType.Swipe;
                end = time;
            }
            else
            {
                // Every so often a tap becomes one of the special kinds, counted rather than rolled so
                // the random sequence (and every seeded chart's layout) is unchanged.
                tapIndex++;
                type = tapIndex % 25 == 0 ? NoteType.Skill
                    : tapIndex % 17 == 0 ? NoteType.Pass
                    : tapIndex % 12 == 0 ? NoteType.Touch
                    : NoteType.Tap;
                end = time;
            }

            // Every fourth hold drifts one lane over by its end.
            NoteLane? endLane = null;
            if (type == NoteType.Hold && ++holdIndex % 4 == 0 && laneCount > 1)
                endLane = (NoteLane)(lane + 1 < laneCount ? lane + 1 : lane - 1);

            notes.Add(new BeatmapNote
            {
                Lane = (NoteLane)lane,
                Type = type,
                StartTime = time,
                EndTime = end,
                // Give each note type its own bank so the three sample sets are all exercised, and
                // sprinkle additions (a finish on the downbeat, occasional clap/whistle) so the full
                // hitsound stack is audible in the prototype.
                Bank = bankFor(type),
                Additions = additionsFor(beatIndex, random),
                // Cycle Any -> Left -> Right so every swipe kind shows up. Counted rather than rolled so
                // the generator's random sequence (and every seeded chart) stays as it was.
                Direction = type == NoteType.Swipe ? (SwipeDirection)(swipeIndex++ % 3) : SwipeDirection.Any,
                Skill = (tapIndex / 25) % 2 == 0 ? SkillType.Heal : SkillType.BreakToCritical,
                EndLane = endLane
            });
        }

        return new PlayableBeatmap(notes.ToArray(), laneCount) { ControlPoints = ControlPointInfo.FromBpm(bpm) };
    }

    /// <summary>
    /// Distinct sample bank per note type so all three banks are heard in a generated chart.
    /// </summary>
    private static SampleBank bankFor(NoteType type) => type switch
    {
        NoteType.Hold => SampleBank.Drum,
        NoteType.Swipe => SampleBank.Soft,
        _ => SampleBank.Normal
    };

    /// <summary>
    /// A finish on every fourth beat plus occasional clap/whistle, for hitsound variety.
    /// </summary>
    private static HitSampleAddition additionsFor(int beatIndex, Random random)
    {
        HitSampleAddition additions = HitSampleAddition.None;

        if (beatIndex % 4 == 0)
            additions |= HitSampleAddition.Finish;
        if (random.NextDouble() < 0.25)
            additions |= HitSampleAddition.Clap;
        if (random.NextDouble() < 0.15)
            additions |= HitSampleAddition.Whistle;

        return additions;
    }
}
