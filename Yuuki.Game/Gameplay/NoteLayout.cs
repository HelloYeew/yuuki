// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Yuuki.Game.Gameplay;

/// <summary>
/// What a <see cref="DrawableNote"/> needs to project itself onto the perspective highway, handed
/// over by the <see cref="Playfield"/> when the note is spawned.
/// </summary>
public readonly struct NoteLayout
{
    /// <summary>
    /// The shared highway projection.
    /// </summary>
    public readonly LanePerspective Perspective;

    /// <summary>
    /// This note's lane offset (world X at the player edge). 0 for full-width swipe notes.
    /// </summary>
    public readonly float LaneOffset;

    /// <summary>
    /// Where a lane-changing hold's tail ends; equal to <see cref="LaneOffset"/> otherwise.
    /// </summary>
    public readonly float EndLaneOffset;

    /// <summary>
    /// Live scroll speed: how long (ms) a note is visible before the judgement line. A source
    /// (rather than a captured value) so changing scroll speed mid-play affects notes already on
    /// screen, not just newly-spawned ones.
    /// </summary>
    public readonly Func<double> PreemptSource;

    /// <summary>
    /// The note's width at the player edge (before perspective scaling).
    /// </summary>
    public readonly float NoteWidth;

    public double Preempt => PreemptSource();

    public NoteLayout(LanePerspective perspective, float laneOffset, Func<double> preemptSource, float noteWidth, float? endLaneOffset = null)
    {
        Perspective = perspective;
        LaneOffset = laneOffset;
        EndLaneOffset = endLaneOffset ?? laneOffset;
        PreemptSource = preemptSource;
        NoteWidth = noteWidth;
    }
}
