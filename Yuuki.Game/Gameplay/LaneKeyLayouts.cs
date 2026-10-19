// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Input;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// The default keyboard layout for each lane count
/// </summary>
public static class LaneKeyLayouts
{
    public const int MIN_LANES = 4;
    public const int MAX_LANES = 8;

    private static readonly Dictionary<int, Key[]> defaults = new Dictionary<int, Key[]>
    {
        [4] = new[] { Key.D, Key.F, Key.J, Key.K },
        [5] = new[] { Key.D, Key.F, Key.Space, Key.J, Key.K },
        [6] = new[] { Key.S, Key.D, Key.F, Key.J, Key.K, Key.L },
        [7] = new[] { Key.S, Key.D, Key.F, Key.Space, Key.J, Key.K, Key.L },
        [8] = new[] { Key.A, Key.S, Key.D, Key.F, Key.J, Key.K, Key.L, Key.Semicolon }
    };

    public static int Clamp(int laneCount) => Math.Clamp(laneCount, MIN_LANES, MAX_LANES);

    /// <summary>
    /// The default keys for <paramref name="laneCount"/> lanes, left to right.
    /// </summary>
    public static Key[] Default(int laneCount) => defaults[Clamp(laneCount)].ToArray();

    /// <summary>
    /// Reads a saved layout ("D,F,J,K"). Anything unreadable, or the wrong number of keys, falls back
    /// to the default — a broken setting should never leave a lane unplayable.
    /// </summary>
    public static Key[] Parse(string? saved, int laneCount)
    {
        laneCount = Clamp(laneCount);

        if (string.IsNullOrWhiteSpace(saved))
            return Default(laneCount);

        string[] parts = saved.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != laneCount)
            return Default(laneCount);

        var keys = new Key[laneCount];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!Enum.TryParse(parts[i], out keys[i]))
                return Default(laneCount);
        }

        return keys;
    }

    public static string Format(IEnumerable<Key> keys) => string.Join(",", keys);
}
