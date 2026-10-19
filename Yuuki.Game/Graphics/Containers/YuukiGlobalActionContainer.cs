// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input.Bindings;
using Yuuki.Game.Graphics.UserInterface.Overlay;

namespace Yuuki.Game.Graphics.Containers;

public partial class YuukiGlobalActionContainer : Component, IKeyBindingHandler<YuukiGlobalAction>
{
    // optional so the volume keys can be tested without a whole game.
    [Resolved(canBeNull: true)]
    private YuukiGame? game { get; set; }

    [Resolved(canBeNull: true)]
    private VolumeOverlay? volume { get; set; }

    public bool OnPressed(KeyBindingPressEvent<YuukiGlobalAction> e)
    {
        switch (e.Action)
        {
            case YuukiGlobalAction.ToggleFooter when !e.Repeat:
                game?.ShowFooter.Toggle();
                break;

            case YuukiGlobalAction.ToggleSettings when !e.Repeat:
                game?.ToggleSettingsOverlay();
                break;

            case YuukiGlobalAction.ToggleNotifications when !e.Repeat:
                game?.ToggleNotificationOverlay();
                break;

            case YuukiGlobalAction.IncreaseVolume:
                return volume?.Adjust(1) ?? false;

            case YuukiGlobalAction.DecreaseVolume:
                return volume?.Adjust(-1) ?? false;

            case YuukiGlobalAction.PreviousVolumeMeter:
                return volume?.SelectNext(-1) ?? false;

            case YuukiGlobalAction.NextVolumeMeter:
                return volume?.SelectNext() ?? false;

            case YuukiGlobalAction.ToggleMute when !e.Repeat:
                volume?.ToggleMute();
                return volume != null;
        }

        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<YuukiGlobalAction> e)
    {

    }
}
