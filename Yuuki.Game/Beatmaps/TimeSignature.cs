// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Text.Json.Serialization;

namespace Yuuki.Game.Beatmaps;

public readonly record struct TimeSignature
{
    /// <summary>
    /// The number of beats per bar (the top number).
    /// </summary>
    public int Numerator { get; }

    /// <summary>
    /// The note value that represents one beat (the bottom number). 4 = quarter note.
    /// </summary>
    public int Denominator { get; }

    [JsonConstructor]
    public TimeSignature(int numerator, int denominator = 4)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>
    /// Common time, 4/4
    /// </summary>
    public static TimeSignature CommonTime { get; } = new TimeSignature(4, 4);

    /// <summary>
    /// Waltz time, 3/4.
    /// </summary>
    public static TimeSignature SimpleTriple { get; } = new TimeSignature(3, 4);

    public override string ToString() => $"{Numerator}/{Denominator}";
}
