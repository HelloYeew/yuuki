// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// The kind of <see cref="BeatmapNote"/>. Stored by number in beatmap files, so new kinds go on the
/// end, and existing numbers never change meaning.
/// </summary>
public enum NoteType
{
    /// <summary>
    /// A single-press note. Hit once when it reaches the judgment line.
    /// </summary>
    Tap,

    /// <summary>
    /// A note held for a duration (<see cref="BeatmapNote.StartTime"/> to <see cref="BeatmapNote.EndTime"/>),
    /// judged at the press and at the release. May drift to <see cref="BeatmapNote.EndLane"/>.
    /// </summary>
    Hold,

    /// <summary>
    /// A wide note across every lane, hit by the swipe input its <see cref="BeatmapNote.Direction"/> allows.
    /// </summary>
    Swipe,

    /// <summary>
    /// Nothing to press: the character has to be standing in the lane as it passes. Weighs much less.
    /// </summary>
    Pass,

    /// <summary>
    /// A tap note that sets off its <see cref="BeatmapNote.Skill"/> when hit (heal, Break -> Critical…).
    /// </summary>
    Skill,

    /// <summary>
    /// Reserved (Boss phase): a bullet.
    /// </summary>
    Bullet,

    /// <summary>
    /// Reserved (Boss phase): a swipe bullet.
    /// </summary>
    SwipeBullet,

    /// <summary>
    /// VOEZ-style: no fresh press needed, have the lane held down as it reaches the line.
    /// </summary>
    Touch
}
