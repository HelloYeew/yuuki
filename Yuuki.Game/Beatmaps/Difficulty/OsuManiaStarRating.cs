// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Yuuki.Game.Beatmaps.Difficulty;

/// <summary>
/// osu!mania's classic strain-based star rating, ported from osu!'s <c>ManiaDifficultyCalculator</c> /
/// <c>Strain</c> skill (no mods)
/// </summary>
public static class OsuManiaStarRating
{
    private const double star_scaling_factor = 0.018;
    private const double section_length = 400;
    private const double decay_weight = 0.9;

    private const double individual_decay_base = 0.125;
    private const double overall_decay_base = 0.30;
    private const double release_threshold = 30;

    public static double Calculate(IEnumerable<BeatmapNote> notes, int laneCount)
    {
        // osu! sorts by rounded start time (stable for chords) and rates each object against the one
        // before it, so the very first object never contributes.
        BeatmapNote[] sorted = notes.OrderBy(n => Math.Round(n.StartTime)).ToArray();
        if (sorted.Length < 2)
            return 0;

        double[] startTimes = new double[laneCount];
        double[] endTimes = new double[laneCount];
        double[] individualStrains = new double[laneCount];

        double individualStrain = 0;
        double overallStrain = 1;

        var peaks = new List<double>();
        double sectionEnd = Math.Ceiling(sorted[1].StartTime / section_length) * section_length;
        double sectionPeak = 0;

        for (int i = 1; i < sorted.Length; i++)
        {
            BeatmapNote note = sorted[i];
            double previousStartTime = sorted[i - 1].StartTime;

            while (note.StartTime > sectionEnd)
            {
                peaks.Add(sectionPeak);

                // A new section starts from whatever strain is left, decayed to the section's start.
                double sinceLast = sectionEnd - previousStartTime;
                sectionPeak = applyDecay(individualStrain, sinceLast, individual_decay_base)
                              + applyDecay(overallStrain, sinceLast, overall_decay_base);
                sectionEnd += section_length;
            }

            double startTime = note.StartTime;
            double endTime = note.EndTime;
            double deltaTime = startTime - previousStartTime;
            int column = Math.Clamp((int)note.Lane, 0, laneCount - 1);

            bool isOverlapping = false;
            double closestEndTime = Math.Abs(endTime - startTime);
            double holdFactor = 1.0;
            double holdAddition = 0;

            for (int c = 0; c < laneCount; c++)
            {
                // Overlapped if an earlier note's end falls inside this note's body.
                isOverlapping |= definitelyBigger(endTimes[c], startTime)
                                 && definitelyBigger(endTime, endTimes[c])
                                 && definitelyBigger(startTime, startTimes[c]);

                // A slight bonus to everything while something else is still held.
                if (definitelyBigger(endTimes[c], endTime) && definitelyBigger(startTime, startTimes[c]))
                    holdFactor = 1.25;

                closestEndTime = Math.Min(closestEndTime, Math.Abs(endTime - endTimes[c]));
            }

            // Releasing awkwardly is harder, but releasing together with another hold is as easy as one.
            if (isOverlapping)
                holdAddition = 1 / (1 + Math.Exp(0.27 * (release_threshold - closestEndTime)));

            individualStrains[column] = applyDecay(individualStrains[column], startTime - startTimes[column], individual_decay_base);
            individualStrains[column] += 2.0 * holdFactor;

            // Within a chord, the hardest column counts.
            individualStrain = deltaTime <= 1 ? Math.Max(individualStrain, individualStrains[column]) : individualStrains[column];

            overallStrain = applyDecay(overallStrain, deltaTime, overall_decay_base);
            overallStrain += (1 + holdAddition) * holdFactor;

            startTimes[column] = startTime;
            endTimes[column] = endTime;

            sectionPeak = Math.Max(sectionPeak, individualStrain + overallStrain);
        }

        peaks.Add(sectionPeak);

        double difficulty = 0;
        double weight = 1;

        foreach (double peak in peaks.Where(p => p > 0).OrderByDescending(p => p))
        {
            difficulty += peak * weight;
            weight *= decay_weight;
        }

        return difficulty * star_scaling_factor;
    }

    private static double applyDecay(double value, double deltaTime, double decayBase)
        => value * Math.Pow(decayBase, deltaTime / 1000);

    private static bool definitelyBigger(double value1, double value2) => value1 - 1 > value2;
}
