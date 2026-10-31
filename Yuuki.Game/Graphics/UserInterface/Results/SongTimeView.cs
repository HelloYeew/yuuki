// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Yuuki.Game.Graphics.UserInterface.Results;

/// <summary>
/// How much of the song the graphs through it show, and where: one of these shared keeps them lined up, each moment in the
/// same place on them all.
/// </summary>
public sealed class SongTimeView
{
    private static readonly int[] zooms = { 1, 2, 4 };

    private int level;

    /// <summary>
    /// The middle of what's shown, as a share of the song.
    /// </summary>
    public double Middle { get; private set; } = 0.5;

    /// <summary>
    /// How many times over the song is magnified.
    /// </summary>
    public int Zoom => zooms[level];

    /// <summary>
    /// How far zoomed in it is, in steps from all of it.
    /// </summary>
    public int Level => level;

    /// <summary>
    /// Goes up each time what's shown changes, for what's drawn from it to tell.
    /// </summary>
    public int Version { get; private set; }

    public bool CanZoomIn => level < zooms.Length - 1;

    public bool CanZoomOut => level > 0;

    /// <summary>
    /// Where what's shown starts, as a share of the song.
    /// </summary>
    public double Start => Middle - 0.5 / Zoom;

    public bool CanMoveBack => Start > 0;

    public bool CanMoveOn => Start + 1.0 / Zoom < 1;

    public void ZoomIn()
    {
        if (CanZoomIn)
            setLevel(level + 1);
    }

    public void ZoomOut()
    {
        if (CanZoomOut)
            setLevel(level - 1);
    }

    /// <summary>
    /// Moves what's shown along the song, by a share of the song, as far as there's song.
    /// </summary>
    public void Move(double share) => setMiddle(Middle + share);

    /// <summary>
    /// Moves what's shown along the song by a share of what's shown.
    /// </summary>
    public void MoveByView(double share) => Move(share / Zoom);

    private void setLevel(int value)
    {
        level = value;
        setMiddle(Middle);
        Version++;
    }

    private void setMiddle(double value)
    {
        double half = 0.5 / Zoom;
        double middle = Math.Clamp(value, half, 1 - half);

        if (middle == Middle)
            return;

        Middle = middle;
        Version++;
    }
}
