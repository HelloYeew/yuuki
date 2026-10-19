// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using Sakura.Framework.Input;
using Sakura.Framework.Input.Bindings;

namespace Yuuki.Game.Graphics.Containers;

public partial class YuukiKeyBindingContainer : KeyBindingContainer<YuukiGlobalAction>
{
    public YuukiKeyBindingContainer()
    {
        SimultaneousMode = SimultaneousBindingMode.None;
        MatchingMode = KeyCombinationMatchingMode.Any;
        // Held, Alt+up/down keeps turning the volume (faster and faster) the toggles skip repeats.
        SendRepeats = true;
    }

    public override IEnumerable<KeyBinding> DefaultKeyBindings => new[]
    {
        new KeyBinding(new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.T)),
            YuukiGlobalAction.ToggleFooter),
        new KeyBinding(new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.O)),
            YuukiGlobalAction.ToggleSettings),
        new KeyBinding(new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.N)),
            YuukiGlobalAction.ToggleNotifications),

        // volume control
        new KeyBinding(new KeyCombination(InputKey.Alt, InputKeyExtensions.FromKey(Key.Up)),
            YuukiGlobalAction.IncreaseVolume),
        new KeyBinding(new KeyCombination(InputKey.Alt, InputKeyExtensions.FromKey(Key.Down)),
            YuukiGlobalAction.DecreaseVolume),
        new KeyBinding(new KeyCombination(InputKey.Alt, InputKeyExtensions.FromKey(Key.Left)),
            YuukiGlobalAction.PreviousVolumeMeter),
        new KeyBinding(new KeyCombination(InputKey.Alt, InputKeyExtensions.FromKey(Key.Right)),
            YuukiGlobalAction.NextVolumeMeter),
        new KeyBinding(new KeyCombination(InputKey.Control, InputKeyExtensions.FromKey(Key.F4)),
            YuukiGlobalAction.ToggleMute)
    };
}
