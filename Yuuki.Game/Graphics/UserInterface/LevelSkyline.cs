// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Beatmaps;
using Yuuki.Game.Utilities;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A song's difficulties at a glance: one small bar per difficulty, its height the level and its colour the
/// tier, so a gentle ladder and a set of only hard charts look different before you read a number.
/// </summary>
public partial class LevelSkyline : Container
{
    /// <summary>
    /// More difficulties than this show only the first ones.
    /// </summary>
    public const int MAX_BARS = 6;

    private readonly float barWidth;
    private readonly float gap;

    public LevelSkyline(float height = 26f, float barWidth = 6f, float gap = 3f)
    {
        this.barWidth = barWidth;
        this.gap = gap;
        Height = height;
    }

    public void SetDifficulties(IReadOnlyList<Beatmap> difficulties)
    {
        Clear();

        int count = Math.Min(difficulties.Count, MAX_BARS);
        Width = Math.Max(0, count * (barWidth + gap) - gap);

        for (int i = 0; i < count; i++)
        {
            int level = BeatmapFilter.LevelOf(difficulties[i]);

            Add(new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                X = i * (barWidth + gap),
                Size = new Vector2(barWidth, Height * Math.Clamp(level / (float)BeatmapFilter.MAX_LEVEL, 0.14f, 1f)),
                Color = BeatmapUtility.ColorForDifficulty(difficulties[i].DifficultyRating)
            });
        }
    }
}
