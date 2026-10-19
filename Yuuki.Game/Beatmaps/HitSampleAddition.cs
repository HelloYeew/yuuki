// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Yuuki.Game.Beatmaps;

/// <summary>
/// Optional extra hitsounds layered on top of a note's base <c>hitnormal</c>.
/// A note always plays its bank's <c>hitnormal</c>; each flag set here adds the
/// matching bank sample (<c>hitwhistle</c>/<c>hitfinish</c>/<c>hitclap</c>) on top.
/// </summary>
[Flags]
public enum HitSampleAddition
{
    /// <summary>
    /// No additions — just the base <c>hitnormal</c>.
    /// </summary>
    None = 0,

    /// <summary>
    /// Adds <c>{bank}-hitwhistle</c>.
    /// </summary>
    Whistle = 1 << 0,

    /// <summary>
    /// Adds <c>{bank}-hitfinish</c>.
    /// </summary>
    Finish = 1 << 1,

    /// <summary>
    /// Adds <c>{bank}-hitclap</c>.
    /// </summary>
    Clap = 1 << 2
}
