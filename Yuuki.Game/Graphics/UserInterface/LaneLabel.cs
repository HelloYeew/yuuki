// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A lane count ("4K", "4-7K") with the lanes icon, printed straight on the surface in plain ink: on the
/// glass band, the focus window and the docked song.
/// </summary>
public partial class LaneLabel : FlowContainer
{
    private readonly IconSprite icon;
    private readonly SpriteText text;

    public LaneLabel(float textSize = 14f)
    {
        AutoSizeAxes = Axes.Both;
        Direction = FlowDirection.Horizontal;
        Spacing = new Vector2(textSize * 0.3f, 0);
        Alpha = 0;

        Children = new Drawable[]
        {
            icon = new IconSprite
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Icon = IconUsage.ViewColumn,
                IconSize = textSize * 1.15f
            },
            text = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Font = YuukiFontUsage.GetFontUsage(Typeface.Nunito, (int)textSize, FontWeight.ExtraBold)
            }
        };
    }

    public new Color Color
    {
        get => text.Color;
        set
        {
            text.Color = value;
            icon.Color = value;
        }
    }

    public string Text
    {
        get => text.Text;
        set
        {
            text.Text = value;
            Alpha = string.IsNullOrEmpty(value) ? 0 : 1;
        }
    }
}
