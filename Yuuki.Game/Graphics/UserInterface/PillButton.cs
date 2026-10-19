// This code is part of yuuki. project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Yuuki.Game.Graphics.Text;

namespace Yuuki.Game.Graphics.UserInterface;

/// <summary>
/// A small rounded button for actions inside a panel (the shear buttons are for screen-level
/// navigation). Sizes itself to its text; lightens on hover and dims when disabled.
/// </summary>
public partial class PillButton : ClickableContainer
{
    private readonly Box background;
    private readonly SpriteText label;
    private readonly Color backgroundColor;

    public string Text
    {
        get => label.Text;
        set => label.Text = value;
    }

    public PillButton(Color backgroundColor, Color textColor)
    {
        this.backgroundColor = backgroundColor;

        AutoSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 15;

        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = backgroundColor
            },
            label = new SpriteText
            {
                Margin = new MarginPadding
                {
                    Left = 16,
                    Right = 16,
                    Top = 5,
                    Bottom = 6
                },
                Font = YuukiFontUsage.GetFontUsage(Typeface.Quicksand, 18, FontWeight.Bold),
                Color = textColor
            }
        };

        Enabled.ValueChanged += e => this.FadeTo(e.NewValue ? 1f : 0.5f, 200, Easing.OutQuint);

        Add(new HoverClickSounds { Enabled = () => Enabled.Value });
    }

    public override bool OnHover(MouseEvent e)
    {
        if (Enabled.Value)
            background.FadeToColor(backgroundColor.Lighten(0.25f), 150, Easing.OutQuint);

        return base.OnHover(e);
    }

    public override bool OnHoverLost(MouseEvent e)
    {
        background.FadeToColor(backgroundColor, 150, Easing.OutQuint);
        return base.OnHoverLost(e);
    }
}
