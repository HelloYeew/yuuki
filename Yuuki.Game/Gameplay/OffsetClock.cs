// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Timing;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// Another clock's time, a set amount further along: what the notes are drawn on when they're drawn somewhere other
/// than the time they're judged at (<see cref="Playfield.VisualOffset"/>).
/// </summary>
public sealed class OffsetClock : IFrameBasedClock
{
    private readonly Func<IFrameBasedClock> source;

    /// <summary>
    /// How far along the source this reads, in ms.
    /// </summary>
    public double Offset { get; set; }

    /// <param name="source">The clock to follow, read each time: a drawable's, which it may only have once it's loaded.</param>
    public OffsetClock(Func<IFrameBasedClock> source)
    {
        this.source = source;
    }

    public double CurrentTime => source().CurrentTime + Offset;

    public double Rate => source().Rate;

    public bool IsRunning => source().IsRunning;

    public double ElapsedFrameTime => source().ElapsedFrameTime;

    public double FramesPerSecond => source().FramesPerSecond;

    // The source's owner advances it.
    public void ProcessFrame()
    {
    }
}
