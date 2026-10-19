// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Reactive;
using Yuuki.Game.Beatmaps;

namespace Yuuki.Game.Gameplay.Scoring;

/// <summary>
/// Score, combo and accuracy from incoming <see cref="JudgementResult"/>s, normalized: a perfect run
/// of any chart is exactly <see cref="MAX_SCORE"/> — 90% for accuracy, 10% for combo. The rank comes
/// from accuracy, not score.
/// </summary>
public class ScoreProcessor
{
    public const int MAX_SCORE = 1_000_000;

    private const double accuracy_portion = 0.9;
    private const double combo_portion = 0.1;

    /// <summary>
    /// Combo beyond this earns no extra combo weight, so one long streak can't dwarf the rest.
    /// </summary>
    private const int combo_cap = 400;

    public Reactive<int> Score { get; } = new Reactive<int>(0);
    public Reactive<int> Combo { get; } = new Reactive<int>(0);
    public Reactive<int> MaxCombo { get; } = new Reactive<int>(0);

    /// <summary>
    /// Weighted accuracy of the judgements so far, 0–1 (1 before anything is judged).
    /// </summary>
    public Reactive<double> Accuracy { get; } = new Reactive<double>(1);

    /// <summary>
    /// How many judgements the prepared chart has (a hold counts twice). 0 before <see cref="Prepare"/>.
    /// </summary>
    public int TotalJudgements { get; private set; }

    // The chart's totals, set by Prepare. Without them (a bare processor in a test) the run so far is
    // treated as the whole chart.
    private double totalWeight;
    private double maxComboTotal;

    private double judgedWeight;
    private double accuracyTotal;
    private double comboTotal;

    /// <summary>
    /// Sets the chart's totals so the score can be normalized to <see cref="MAX_SCORE"/>.
    /// </summary>
    public void Prepare(PlayableBeatmap chart)
    {
        totalWeight = 0;
        int judgements = 0;

        foreach (BeatmapNote note in chart.Notes)
        {
            double weight = JudgementWeights.For(note);

            // A hold is judged twice: the press and the release.
            int count = note.Type == NoteType.Hold ? 2 : 1;
            totalWeight += weight * count;
            judgements += count;
        }

        maxComboTotal = comboTotalFor(judgements);
        TotalJudgements = judgements;
        refresh();
    }

    public void Apply(JudgementResult result)
    {
        double weight = JudgementWeights.For(result.Note);
        double value = result.Result.Value();

        judgedWeight += weight;
        accuracyTotal += weight * value;

        if (result.Result.IsHit())
        {
            Combo.Value++;
            if (Combo.Value > MaxCombo.Value)
                MaxCombo.Value = Combo.Value;

            comboTotal += Math.Min(Combo.Value, combo_cap);
        }
        else
        {
            Combo.Value = 0;
        }

        refresh();
    }

    /// <summary>
    /// Drops the combo without a judgement — a hold let go of mid-body.
    /// </summary>
    public void BreakCombo() => Combo.Value = 0;

    /// <summary>
    /// Resets the score processor to its initial state.
    /// </summary>
    public void Reset()
    {
        judgedWeight = accuracyTotal = comboTotal = 0;
        Combo.Value = 0;
        MaxCombo.Value = 0;
        refresh();
    }

    private void refresh()
    {
        Accuracy.Value = judgedWeight > 0 ? accuracyTotal / judgedWeight : 1;

        double weightBase = totalWeight > 0 ? totalWeight : judgedWeight;
        double comboBase = maxComboTotal > 0 ? maxComboTotal : comboTotalFor((int)Math.Round(judgedWeight));

        double normalised = (weightBase > 0 ? accuracy_portion * accuracyTotal / weightBase : 0)
                            + (comboBase > 0 ? combo_portion * comboTotal / comboBase : 0);

        Score.Value = (int)Math.Round(MAX_SCORE * normalised);
    }

    /// <summary>
    /// The combo total of a flawless run of <paramref name="judgements"/> judgements.
    /// </summary>
    private static double comboTotalFor(int judgements)
    {
        double total = 0;
        for (int combo = 1; combo <= judgements; combo++)
            total += Math.Min(combo, combo_cap);
        return total;
    }
}

/// <summary>
/// How much each note counts toward score and accuracy. Everything weighs 1 except where the design
/// says otherwise — Pass notes (nothing to press, just be in the lane) count for a lot less.
/// </summary>
public static class JudgementWeights
{
    public const double PASS_WEIGHT = 0.3;

    public static double For(BeatmapNote note) => note.Type == NoteType.Pass ? PASS_WEIGHT : 1;
}
