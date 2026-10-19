// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Yuuki.Game.Graphics.Screens;

public interface IHaveScreenAction
{
    IReadOnlyList<ScreenAction> ScreenActions { get; }
}
