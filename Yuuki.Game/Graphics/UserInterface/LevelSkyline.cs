// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Graphics.Colors;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A song's difficulties at a glance: one small bar per difficulty, its height the level and its colour the
/// tier, so a gentle ladder and a set of only hard charts look different before you read a number.
/// </summary>
/// <remarks>
/// It shows every difficulty, filtered or not: the ones the song list's filter leaves out are dimmed, so
/// the song still shows what it has.
/// </remarks>
public partial class LevelSkyline : Container
{
    /// <summary>
    /// More difficulties than this show only the first ones.
    /// </summary>
    public const int MAX_BARS = 6;

    /// <summary>
    /// How much of a bar shows when the filter leaves its difficulty out.
    /// </summary>
    public const float FILTERED_OUT_ALPHA = 0.3f;

    [Resolved(canBeNull: true)]
    private SongListFilter? listFilter { get; set; }

    private readonly float barWidth;
    private readonly float gap;

    private readonly List<(Beatmap Beatmap, Box Bar)> bars = new List<(Beatmap, Box)>();
    private int seenFilterVersion = -1;

    public LevelSkyline(float height = 26f, float barWidth = 6f, float gap = 3f)
    {
        this.barWidth = barWidth;
        this.gap = gap;
        Height = height;
    }

    public void SetDifficulties(IReadOnlyList<Beatmap> difficulties)
    {
        Clear();
        bars.Clear();

        int count = Math.Min(difficulties.Count, MAX_BARS);
        Width = Math.Max(0, count * (barWidth + gap) - gap);

        for (int i = 0; i < count; i++)
        {
            int level = BeatmapFilter.LevelOf(difficulties[i]);

            var bar = new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                X = i * (barWidth + gap),
                Size = new Vector2(barWidth, Height * Math.Clamp(level / (float)BeatmapFilter.MAX_LEVEL, 0.14f, 1f)),
                Color = DifficultyColors.For(difficulties[i])
            };

            bars.Add((difficulties[i], bar));
            Add(bar);
        }

        updateDimming();
    }

    /// <summary>
    /// Whether the bar for the difficulty at <paramref name="index"/> is dimmed (tests).
    /// </summary>
    public bool IsDimmed(int index) => bars[index].Bar.Alpha < 1;

    public override void Update()
    {
        base.Update();

        if (listFilter != null && listFilter.Version != seenFilterVersion)
            updateDimming();
    }

    private void updateDimming()
    {
        seenFilterVersion = listFilter?.Version ?? 0;

        foreach (var (beatmap, bar) in bars)
            bar.Alpha = listFilter == null || listFilter.LetsThrough(beatmap) ? 1 : FILTERED_OUT_ALPHA;
    }
}
