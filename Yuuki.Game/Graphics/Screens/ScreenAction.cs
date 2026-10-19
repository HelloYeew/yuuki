// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Input.Bindings;

namespace Yuuki.Game.Graphics.Screens;

public struct ScreenAction : IEquatable<ScreenAction>
{
    public Action Action;
    public string Name;
    public KeyCombination KeyCombination;
    public KeyCombination GamepadKeyCombination;

    public bool Equals(ScreenAction other)
    {
        return Name == other.Name && KeyCombination.Equals(other.KeyCombination) && GamepadKeyCombination.Equals(other.GamepadKeyCombination);
    }

    public override bool Equals(object? obj)
    {
        return obj is ScreenAction other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, KeyCombination, GamepadKeyCombination);
    }
}
