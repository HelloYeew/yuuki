// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// What the footer's clock shows
/// </summary>
public enum ClockDisplayMode
{
    /// <summary>
    /// A clock face only.
    /// </summary>
    Analog,

    /// <summary>
    /// The time in digits.
    /// </summary>
    Digital,

    /// <summary>
    /// The time in digits, with how long the game has been running under it.
    /// </summary>
    DigitalWithRuntime,

    /// <summary>
    /// The face, the digits and the running time.
    /// </summary>
    Full
}
