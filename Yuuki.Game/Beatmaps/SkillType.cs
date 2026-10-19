// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// What a <see cref="NoteType.Skill"/> note does when hit
/// </summary>
public enum SkillType
{
    /// <summary>
    /// Restores a chunk of health.
    /// </summary>
    Heal,

    /// <summary>
    /// For a while afterward, every Break counts as a Critical.
    /// </summary>
    BreakToCritical
}
