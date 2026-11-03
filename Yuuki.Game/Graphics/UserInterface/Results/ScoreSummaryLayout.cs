// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// How the results screen's score summary sits in the width its column has.
/// </summary>
public enum ScoreSummaryArrangement
{
    /// <summary>
    /// The score, and its statistics beside it in a line, each one's title over its value.
    /// </summary>
    OneLine,

    /// <summary>
    /// The score, and its statistics beside it as rows: title, value and change on one line each. A phone's column.
    /// </summary>
    Rows,

    /// <summary>
    /// The score, and its statistics in a line under it: the narrowest column.
    /// </summary>
    Stacked
}

/// <summary>
/// How wide one of the score's statistics is: its title, its value at <see cref="ScoreSummaryLayout.VALUE_SIZE"/> once it
/// has finished rolling up, and its change against the best, if there's one.
/// </summary>
public readonly record struct SummaryStatisticWidths(float Title, float Value, float? Delta = null);

/// <summary>
/// Picks the arrangement of the score summary that fits its column, from how wide its pieces are when the numbers have
/// rolled up (so it doesn't change while they roll), and how tall it is then.
/// </summary>
public static class ScoreSummaryLayout
{
    /// <summary>
    /// Between the score and its statistics on one line, and beside it as rows.
    /// </summary>
    public const float ONE_LINE_GAP = 32;

    public const float ROWS_GAP = 16;

    /// <summary>
    /// Between the score and the line of statistics under it.
    /// </summary>
    public const float STACKED_GAP = 8;

    /// <summary>
    /// A statistic's value on one line or stacked, and in a row.
    /// </summary>
    public const int VALUE_SIZE = 36;

    public const int ROW_VALUE_SIZE = 26;

    /// <summary>
    /// Between the statistics in a line: on one line with the score, and stacked under it.
    /// </summary>
    public const float COLUMN_SPACING = 56;

    public const float STACKED_COLUMN_SPACING = 32;

    /// <summary>
    /// In a row, between the titles' column, the value and the change; and between the rows.
    /// </summary>
    public const float ROW_SPACING = 8;

    public const float ROWS_SPACING = 4;

    /// <summary>
    /// How wide the statistics are in a line, each as wide as the widest of its title, value and change.
    /// </summary>
    public static float LineWidth(IReadOnlyList<SummaryStatisticWidths> statistics, float spacing)
    {
        float width = 0;

        foreach (SummaryStatisticWidths statistic in statistics)
            width += Math.Max(Math.Max(statistic.Title, statistic.Value), statistic.Delta ?? 0);

        return width + spacing * Math.Max(0, statistics.Count - 1);
    }

    /// <summary>
    /// How wide the titles' column is in rows: the widest title, so the values start together.
    /// </summary>
    public static float TitleColumnWidth(IReadOnlyList<SummaryStatisticWidths> statistics)
    {
        float width = 0;

        foreach (SummaryStatisticWidths statistic in statistics)
            width = Math.Max(width, statistic.Title);

        return width;
    }

    /// <summary>
    /// How wide the statistics are as rows: the titles' column, then the widest value and change after it, with the value
    /// at <see cref="ROW_VALUE_SIZE"/>.
    /// </summary>
    public static float RowsWidth(IReadOnlyList<SummaryStatisticWidths> statistics)
    {
        float rest = 0;

        foreach (SummaryStatisticWidths statistic in statistics)
        {
            float row = ROW_SPACING + statistic.Value * ROW_VALUE_SIZE / VALUE_SIZE;

            if (statistic.Delta is float delta)
                row += ROW_SPACING + delta;

            rest = Math.Max(rest, row);
        }

        return TitleColumnWidth(statistics) + rest;
    }

    /// <summary>
    /// How wide the summary has to be for its statistics to go beside the score as rows.
    /// </summary>
    public static float RowsNeed(float score, IReadOnlyList<SummaryStatisticWidths> statistics) => score + ROWS_GAP + RowsWidth(statistics);

    /// <summary>
    /// The arrangement for a column whose summary is <paramref name="width"/> wide, with a score (its title row or its
    /// number, whichever is wider) <paramref name="score"/> wide: one line where it fits, then rows, then stacked.
    /// </summary>
    public static ScoreSummaryArrangement Choose(float width, float score, IReadOnlyList<SummaryStatisticWidths> statistics)
    {
        if (score + ONE_LINE_GAP + LineWidth(statistics, COLUMN_SPACING) <= width)
            return ScoreSummaryArrangement.OneLine;

        if (RowsNeed(score, statistics) <= width)
            return ScoreSummaryArrangement.Rows;

        return ScoreSummaryArrangement.Stacked;
    }

    /// <summary>
    /// How tall the summary is: as tall as the taller of the score and its statistics side by side, or both and the gap
    /// between them, stacked.
    /// </summary>
    public static float Height(ScoreSummaryArrangement arrangement, float score, float statistics) =>
        arrangement == ScoreSummaryArrangement.Stacked ? score + STACKED_GAP + statistics : Math.Max(score, statistics);
}
