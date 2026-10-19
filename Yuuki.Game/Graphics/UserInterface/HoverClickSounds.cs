// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Yuuki.Game.Audio;

namespace Yuuki.Game.Graphics.UserInterface;

public partial class HoverClickSounds : Container
{
    /// <summary>
    /// What a press plays; null for none (the control plays its own, e.g. a toggle).
    /// </summary>
    public UISound? ClickSound { get; set; } = UISound.Click;

    /// <summary>
    /// Whether the control is usable now. While it isn't, hovering is silent and a press plays
    /// <see cref="UISound.Denied"/>. Always usable when unset.
    /// </summary>
    public Func<bool>? Enabled { get; set; }

    /// <summary>
    /// How loud the hover sounds are (busy lists use them quieter).
    /// </summary>
    public double HoverVolume { get; set; } = 1;

    [Resolved(canBeNull: true)]
    private UISoundPlayer? sounds { get; set; }

    private bool enabled => Enabled?.Invoke() ?? true;

    public HoverClickSounds(UISound? clickSound = UISound.Click)
    {
        ClickSound = clickSound;
        RelativeSizeAxes = Axes.Both;
    }

    public override bool HandleNonPositionalInput => false;

    public override bool OnHover(MouseEvent e)
    {
        if (enabled)
            sounds?.Play(UISound.Hover, volume: HoverVolume);

        return false;
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        if (enabled)
            sounds?.Play(UISound.HoverLost, volume: HoverVolume);

        return false;
    }

    // Not the base implementation: it would mark this as being dragged and could hold the drag.
    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (e.Button != MouseButton.Left)
            return false;

        if (!enabled)
            sounds?.Play(UISound.Denied);
        else if (ClickSound is UISound sound)
            sounds?.Play(sound);

        return false;
    }
}
