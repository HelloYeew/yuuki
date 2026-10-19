// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Transforms;

namespace Yuuki.Game.Audio;

/// <summary>
/// How <see cref="YuukiAudioManager.Duck"/> muffles the music
/// </summary>
public class DuckParameters
{
    /// <summary>
    /// How long the cutoff takes to come down, in ms.
    /// </summary>
    public double DuckDuration = 100;

    /// <summary>
    /// The low-pass cutoff to reach, in Hz.
    /// </summary>
    public double DuckCutoffTo = 300;

    /// <summary>
    /// The curve going down
    /// </summary>
    public Easing DuckEasing = Easing.OutQuad;

    /// <summary>
    /// How long it takes to clear again once let go, in ms.
    /// </summary>
    public double RestoreDuration = 500;

    /// <summary>
    /// The curve coming back
    /// </summary>
    public readonly Easing RestoreEasing = Easing.InQuad;
}
