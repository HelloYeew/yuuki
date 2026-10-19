// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;

namespace Yuuki.Game.Edit;

/// <summary>
/// What a beat is split into for snapping
/// </summary>
public class EditorBeatDivisor
{
    public static readonly int[] COMMON = { 1, 2, 4, 8, 16 };

    public static readonly int[] TRIPLETS = { 1, 3, 6, 12 };

    /// <summary>
    /// Every divisor a tick can be classified as, smallest first.
    /// </summary>
    public static readonly int[] ALL = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16 };

    public Reactive<int> Value { get; } = new Reactive<int>(4);

    /// <summary>
    /// The preset list the value moves through.
    /// </summary>
    public int[] Presets => TRIPLETS.Contains(Value.Value) && !COMMON.Contains(Value.Value) ? TRIPLETS : COMMON;

    public bool IsTriplets => Presets == TRIPLETS;

    public void Next() => step(1);

    public void Previous() => step(-1);

    /// <summary>
    /// Swaps between the common and triplet lists, landing on 4 or 6.
    /// </summary>
    public void ToggleTriplets() => Value.Value = IsTriplets ? 4 : 6;

    private void step(int direction)
    {
        int[] presets = Presets;
        int index = Array.IndexOf(presets, Value.Value);
        Value.Value = presets[Math.Clamp(index + direction, 0, presets.Length - 1)];
    }

    /// <summary>
    /// color for a division of the beat: white beats, red halves, blue quarters, yellow eighths,
    /// purple sixteenths, and the triplet family in purple and oranges.
    /// </summary>
    public static Color ColorFor(int divisor) => divisor switch
    {
        1 => Color.White,
        2 => ColorExtensions.FromHex("ed1121"),
        4 => ColorExtensions.FromHex("66ccff"),
        8 => ColorExtensions.FromHex("ffcc22"),
        16 => ColorExtensions.FromHex("6644cc"),
        3 => ColorExtensions.FromHex("8866ee"),
        6 => ColorExtensions.FromHex("eeaa00"),
        12 => ColorExtensions.FromHex("cc6600"),
        5 or 7 or 9 => ColorExtensions.FromHex("b3d944"),
        _ => ColorExtensions.FromHex("ed1121")
    };

    /// <summary>
    /// How prominent a tick of <paramref name="divisor"/> is drawn, as (width, length) fractions.
    /// </summary>
    public static Vector2 SizeFor(int divisor) => divisor switch
    {
        1 or 2 => new Vector2(1, 0.9f),
        3 or 4 => new Vector2(0.8f, 0.8f),
        6 or 8 => new Vector2(0.8f, 0.7f),
        _ => new Vector2(0.8f, 0.6f)
    };

    /// <summary>
    /// Which division of the beat tick <paramref name="index"/> of a 1/<paramref name="divisor"/> grid
    /// falls on: the smallest divisor whose grid also has a tick there.
    /// </summary>
    public static int DivisorForTick(long index, int divisor)
    {
        long within = ((index % divisor) + divisor) % divisor;

        foreach (int candidate in ALL)
        {
            if (within * candidate % divisor == 0)
                return candidate;
        }

        return divisor;
    }
}
